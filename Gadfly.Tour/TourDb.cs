using Microsoft.Data.SqlClient;

namespace Gadfly.Tour;

/// <summary>Read-only SQL the tour asserts with (the tour never writes the application's tables).</summary>
public sealed class TourDb(string connectionString)
{
    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object? Value)[] args)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = Command(conn, sql, args);
        var value = await cmd.ExecuteScalarAsync();
        if (value is null || value is DBNull) return default;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)(target == typeof(Guid) && value is string s ? Guid.Parse(s) : Convert.ChangeType(value, target));
    }

    public async Task<int> CountAsync(string sql, params (string Name, object? Value)[] args) =>
        await ScalarAsync<int>(sql, args);

    public async Task<List<Dictionary<string, object?>>> RowsAsync(string sql, params (string Name, object? Value)[] args)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = Command(conn, sql, args);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>True when the table exists (a feature's schema has landed).</summary>
    public async Task<bool> TableExistsAsync(string schema, string table) =>
        await CountAsync("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @s AND TABLE_NAME = @t", ("@s", schema), ("@t", table)) > 0;

    /// <summary>Waits until <paramref name="probe"/> returns true or the time is up.</summary>
    public static async Task<bool> WaitAsync(Func<Task<bool>> probe, TimeSpan timeout, TimeSpan? every = null)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            if (await probe()) return true;
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(every ?? TimeSpan.FromSeconds(1));
        }
    }

    private static SqlCommand Command(SqlConnection conn, string sql, (string Name, object? Value)[] args)
    {
        var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    /// <summary>Drops the tour database (LocalDB) so the server re-creates it from the migrations.</summary>
    public static async Task DropDatabaseAsync(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var name = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(name) || !name.Contains("Tour", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"--fresh only drops a tour database (its name must contain \"Tour\"), not \"{name}\".");
        builder.InitialCatalog = "master";
        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        var quoted = "[" + name.Replace("]", "]]") + "]";
        await using var cmd = new SqlCommand(
            $"IF DB_ID(@n) IS NOT NULL BEGIN ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted}; END", conn);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.CommandTimeout = 120;
        await cmd.ExecuteNonQueryAsync();
        SqlConnection.ClearAllPools();
    }
}
