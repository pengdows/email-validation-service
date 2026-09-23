using System.Data;
using pengdows.crud.attributes;

namespace EmailValidation.Caching;

/// <summary>
/// One cached DNS check result. Two rows exist per domain in normal operation -
/// one keyed "{domain}|exists" (the A/AAAA check) and one "{domain}|mx" (the MX
/// check) - since EmailValidator's pipeline treats them as independently
/// cacheable facts (a domain can fail one and pass the other).
/// </summary>
[Table("dns_lookup_cache")]
public class DnsLookupCacheEntry
{
    [Id(true)]
    [Column("cache_key", DbType.String)]
    public string CacheKey { get; set; } = string.Empty;

    [Column("is_valid", DbType.Boolean)]
    public bool IsValid { get; set; }

    [Column("failure_reason", DbType.String)]
    public string? FailureReason { get; set; }

    [Column("failure_message", DbType.String)]
    public string? FailureMessage { get; set; }

    /// <summary>MX exchange hostnames, JSON-encoded (null for non-MX cache entries).</summary>
    [Column("mx_records_json", DbType.String)]
    public string? MxRecordsJson { get; set; }

    [Column("checked_at_utc", DbType.DateTime)]
    public DateTime CheckedAtUtc { get; set; }

    [Column("expires_at_utc", DbType.DateTime)]
    public DateTime ExpiresAtUtc { get; set; }
}
