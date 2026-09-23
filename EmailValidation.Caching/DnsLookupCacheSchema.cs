using System.Data;
using pengdows.crud;

namespace EmailValidation.Caching;

/// <summary>
/// Creates the dns_lookup_cache table if it doesn't exist yet.
/// pengdows.crud does not run migrations itself, so callers own schema creation.
///
/// This DDL is written for SQLite specifically. CachingDnsValidator and
/// DnsLookupCacheEntry are not SQLite-specific - pengdows.crud's multi-database
/// support means the same entity/gateway works unmodified against any of its
/// supported providers (PostgreSQL, MySQL, SQL Server, etc.). Only this bootstrap
/// DDL would need a per-provider variant if this cache moves off SQLite.
/// </summary>
public static class DnsLookupCacheSchema
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS dns_lookup_cache (
            cache_key TEXT PRIMARY KEY,
            is_valid INTEGER NOT NULL,
            failure_reason TEXT NULL,
            failure_message TEXT NULL,
            mx_records_json TEXT NULL,
            checked_at_utc TEXT NOT NULL,
            expires_at_utc TEXT NOT NULL
        )
        """;

    public static async Task EnsureCreatedAsync(IDatabaseContext context, CancellationToken cancellationToken = default)
    {
        var sc = context.CreateSqlContainer(CreateTableSql);
        await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken);
    }
}
