using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gadfly.Server.Data;

/// <summary>
/// The platform's database: every Gadfly table lives in the <c>gadfly</c> schema (the
/// MindAttic.Authentication tables join in their own <c>auth</c> schema from GAD-T41). The entity
/// model itself arrives with GAD-T40; this context is the host, connection and migration seam.
/// </summary>
public sealed class GadflyDbContext(DbContextOptions<GadflyDbContext> options) : DbContext(options)
{
    public const string Schema = "gadfly";

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema(Schema);
    }
}

/// <summary>Where the platform's SQL Server lives.</summary>
public static class GadflyDatabase
{
    /// <summary>Connection-string name: <c>ConnectionStrings:Gadfly</c> (env var <c>ConnectionStrings__Gadfly</c>).</summary>
    public const string ConnectionStringName = "Gadfly";

    /// <summary>
    /// Development default: the per-user SQL Server Express LocalDB instance. Production sets
    /// <c>ConnectionStrings__Gadfly</c> to Azure SQL (Active Directory Default auth, no secret in the string).
    /// </summary>
    public const string LocalDbConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=Gadfly;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";

    public static string Resolve(IConfiguration? configuration) =>
        configuration?.GetConnectionString(ConnectionStringName)
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__" + ConnectionStringName)
        ?? LocalDbConnectionString;

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsHistoryTable("__EFMigrationsHistory", GadflyDbContext.Schema);
            sql.EnableRetryOnFailure();
        });
}

/// <summary>Design-time factory so <c>dotnet ef migrations add … --project Gadfly.Server</c> works without starting the host.</summary>
public sealed class GadflyDbContextFactory : IDesignTimeDbContextFactory<GadflyDbContext>
{
    public GadflyDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<GadflyDbContext>();
        GadflyDatabase.Configure(builder, GadflyDatabase.Resolve(null));
        return new GadflyDbContext(builder.Options);
    }
}
