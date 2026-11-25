<#
.SYNOPSIS
  One-off: set the MindAttic.Authentication email of the live accounts ryan and erin (GAD-T779).

.DESCRIPTION
  Reads ConnectionStrings__Gadfly from the mindattic-gadfly app settings (never printed), connects
  with an Azure AD access token for the signed-in az account, and for each account updates
  auth.AuthUsers.Email and auth.AuthUsers.NormalizedEmail (Trim().ToUpperInvariant(), the library's
  own normalization in UserAdminService.NormalizeEmail). No other column or row is touched.

  Usernames are matched case-insensitively (on UserName and NormalizedUserName). If a name matches
  zero rows or more than one row, the script stops before changing anything.

  The serverless database wakes on connect; the web app is never started. Your client IP must be
  allowed by the SQL server firewall.

.PARAMETER DryRun
  Show the matched rows and the planned change without writing.
#>
[CmdletBinding()]
param(
    [string]$ResourceGroup = 'MyApps',
    [string]$App = 'mindattic-gadfly',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$targets = [ordered]@{
    'ryan' = 'ryandebraal@gmail.com'
    'erin' = 'ohleo199@gmail.com'
}

function Get-NormalizedEmail([string]$email) { $email.Trim().ToUpperInvariant() }

# --- connection string (kept in memory only) ---------------------------------------------------
$settings = az webapp config appsettings list -g $ResourceGroup -n $App -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "Could not read app settings for $App." }
$raw = ($settings | Where-Object { $_.name -eq 'ConnectionStrings__Gadfly' }).value
if ([string]::IsNullOrWhiteSpace($raw)) { throw "ConnectionStrings__Gadfly is not set on $App." }

# System.Data.SqlClient (Windows PowerShell) has no 'Active Directory Default'; drop the keyword
# and authenticate with an access token from az instead.
# (Regex, not DbConnectionStringBuilder: PowerShell's dictionary adapter turns .ConnectionString
# into a key instead of the property.)
$connString = ($raw -split ';' | Where-Object { $_ -and $_ -notmatch '^\s*(Authentication|User ID|UID|Password|PWD)\s*=' }) -join ';'
$token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or -not $token) { throw 'Could not get an Azure SQL access token (az login?).' }

$conn = New-Object System.Data.SqlClient.SqlConnection $connString
$conn.AccessToken = $token
Write-Host 'Connecting to the live database (a paused serverless database can take a minute to wake)...'
$conn.Open()

function Invoke-Query([string]$sql, [hashtable]$params, $tx = $null) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.CommandTimeout = 120
    if ($tx) { $cmd.Transaction = $tx }
    foreach ($p in $params.GetEnumerator()) { [void]$cmd.Parameters.AddWithValue($p.Key, $p.Value) }
    $table = New-Object System.Data.DataTable
    $reader = $cmd.ExecuteReader()
    try { $table.Load($reader) } finally { $reader.Dispose() }
    , $table
}

$selectSql = @'
SELECT Id, UserName, NormalizedUserName, Email, NormalizedEmail
FROM auth.AuthUsers
WHERE UPPER(UserName) = @name OR NormalizedUserName = @name
'@

try {
    # --- resolve every target first; any ambiguity stops the whole run -------------------------
    $plan = @()
    foreach ($name in $targets.Keys) {
        $rows = Invoke-Query $selectSql @{ '@name' = $name.ToUpperInvariant() }
        if ($rows.Rows.Count -ne 1) {
            $rows | Format-Table Id, UserName, NormalizedUserName, Email -AutoSize | Out-String | Write-Host
            throw "Username '$name' matched $($rows.Rows.Count) rows (expected exactly 1). Nothing was changed."
        }
        $r = $rows.Rows[0]
        $plan += [pscustomobject]@{ Id = [guid]$r.Id; UserName = $r.UserName; OldEmail = $r.Email; NewEmail = $targets[$name] }
    }
    $plan | Format-Table UserName, OldEmail, NewEmail -AutoSize | Out-String | Write-Host
    if ($DryRun) { Write-Host 'DryRun: no changes written.'; return }

    # --- update exactly those rows, in one transaction -----------------------------------------
    $tx = $conn.BeginTransaction()
    try {
        foreach ($p in $plan) {
            $cmd = $conn.CreateCommand()
            $cmd.Transaction = $tx
            $cmd.CommandText = 'UPDATE auth.AuthUsers SET Email = @email, NormalizedEmail = @norm WHERE Id = @id'
            [void]$cmd.Parameters.AddWithValue('@email', $p.NewEmail)
            [void]$cmd.Parameters.AddWithValue('@norm', (Get-NormalizedEmail $p.NewEmail))
            [void]$cmd.Parameters.AddWithValue('@id', $p.Id)
            $n = $cmd.ExecuteNonQuery()
            if ($n -ne 1) { throw "Update for $($p.UserName) affected $n rows; rolling back." }
        }
        $tx.Commit()
    } catch { $tx.Rollback(); throw }

    # --- read the rows back ----------------------------------------------------------------------
    Write-Host 'After update:'
    foreach ($p in $plan) {
        $rows = Invoke-Query 'SELECT UserName, Email, NormalizedEmail FROM auth.AuthUsers WHERE Id = @id' @{ '@id' = $p.Id }
        $r = $rows.Rows[0]
        $ok = ($r.Email -eq $p.NewEmail) -and ($r.NormalizedEmail -eq (Get-NormalizedEmail $p.NewEmail))
        Write-Host ("  {0,-6} {1,-26} {2,-26} {3}" -f $r.UserName, $r.Email, $r.NormalizedEmail, $(if ($ok) { 'OK' } else { 'MISMATCH' }))
        if (-not $ok) { throw "Read-back for $($p.UserName) does not match." }
    }
}
finally {
    $conn.Dispose()
}
