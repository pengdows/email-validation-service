using EmailValidation.Core;
using Microsoft.Data.Sqlite;
using pengdows.crud;

namespace EmailValidation.Caching;

/// <summary>
/// Convenience wiring for the SQLite-backed cache: opens (creating if needed) a
/// SQLite database at the given path, ensures the cache table exists, and
/// returns a CachingDnsValidator ready to wrap a real IDnsValidator.
///
/// Only this file is SQLite-specific. To move the cache to PostgreSQL, MySQL,
/// SQL Server, etc., swap SqliteFactory.Instance for the target provider's
/// DbProviderFactory and point DnsLookupCacheSchema.EnsureCreatedAsync at
/// provider-appropriate DDL - CachingDnsValidator and DnsLookupCacheEntry need
/// no changes, since pengdows.crud generates dialect-correct SQL for both from
/// the same entity mapping.
/// </summary>
public static class SqliteDnsCache
{
    public static async Task<CachingDnsValidator> CreateAsync(
        IDnsValidator inner,
        string sqliteFilePath,
        TimeSpan? positiveTtl = null,
        TimeSpan? negativeTtl = null,
        CancellationToken cancellationToken = default)
    {
        var context = new DatabaseContext($"Data Source={sqliteFilePath}", SqliteFactory.Instance);
        await DnsLookupCacheSchema.EnsureCreatedAsync(context, cancellationToken);

        var gateway = new TableGateway<DnsLookupCacheEntry, string>(context);
        return new CachingDnsValidator(inner, gateway, positiveTtl, negativeTtl);
    }
}
