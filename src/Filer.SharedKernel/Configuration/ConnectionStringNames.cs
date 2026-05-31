namespace Filer.SharedKernel.Configuration;

/// <summary>
/// Names of the connection strings the host exposes via configuration. App-wide
/// infrastructure naming — every module that opens its own <c>DbContext</c> reads
/// the same physical Postgres instance (one database, schema-per-module —
/// 10-solution-structure.md), so the key lives here rather than in any one module.
/// </summary>
public static class ConnectionStringNames
{
    /// <summary>The primary PostgreSQL connection (ADR-002).</summary>
    public const string Postgres = "Postgres";
}
