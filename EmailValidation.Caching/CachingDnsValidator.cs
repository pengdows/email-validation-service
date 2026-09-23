using System.Text.Json;
using EmailValidation.Core;
using pengdows.crud;

namespace EmailValidation.Caching;

/// <summary>
/// Decorates an IDnsValidator with a persistent cache (via pengdows.crud, so the
/// backing store can be swapped from SQLite to any provider it supports without
/// touching this class). Domain-exists and MX results are cached independently,
/// since EmailValidator's pipeline treats them as independently-true facts (a
/// domain can have MX with no A/AAAA record - see DomainDoesNotExist vs
/// InternalAddressBlocked handling in EmailValidator).
///
/// Failures get a shorter TTL than successes on purpose: a transient DNS blip
/// shouldn't wedge a real domain into "invalid" for as long as a confirmed
/// negative result deserves to stick.
/// </summary>
public sealed class CachingDnsValidator : IDnsValidator
{
    private readonly IDnsValidator _inner;
    private readonly ITableGateway<DnsLookupCacheEntry, string> _cache;
    private readonly TimeSpan _positiveTtl;
    private readonly TimeSpan _negativeTtl;

    public CachingDnsValidator(
        IDnsValidator inner,
        ITableGateway<DnsLookupCacheEntry, string> cache,
        TimeSpan? positiveTtl = null,
        TimeSpan? negativeTtl = null)
    {
        _inner = inner;
        _cache = cache;
        _positiveTtl = positiveTtl ?? TimeSpan.FromMinutes(30);
        _negativeTtl = negativeTtl ?? TimeSpan.FromMinutes(5);
    }

    public ValueTask<ValidationResult> ValidateDomainExistsAsync(
        string domain, EmailValidatorOptions options, CancellationToken cancellationToken = default)
        => GetOrComputeAsync($"{domain}|exists", domain, () => _inner.ValidateDomainExistsAsync(domain, options, cancellationToken), cancellationToken);

    public ValueTask<ValidationResult> ValidateMxRecordsAsync(
        string domain, EmailValidatorOptions options, CancellationToken cancellationToken = default)
        => GetOrComputeAsync($"{domain}|mx", domain, () => _inner.ValidateMxRecordsAsync(domain, options, cancellationToken), cancellationToken);

    private async ValueTask<ValidationResult> GetOrComputeAsync(
        string cacheKey,
        string domain,
        Func<ValueTask<ValidationResult>> compute,
        CancellationToken cancellationToken)
    {
        var cached = await _cache.RetrieveOneAsync(cacheKey, cancellationToken: cancellationToken);
        if (cached is not null && cached.ExpiresAtUtc > DateTime.UtcNow)
        {
            return ToValidationResult(domain, cached);
        }

        var result = await compute();

        var entry = new DnsLookupCacheEntry
        {
            CacheKey = cacheKey,
            IsValid = result.IsValid,
            FailureReason = result.FailureReason?.ToString(),
            FailureMessage = result.FailureMessage,
            MxRecordsJson = result.MxRecords is null ? null : JsonSerializer.Serialize(result.MxRecords),
            CheckedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(result.IsValid ? _positiveTtl : _negativeTtl)
        };

        await _cache.UpsertAsync(entry, cancellationToken: cancellationToken);

        return result;
    }

    private static ValidationResult ToValidationResult(string domain, DnsLookupCacheEntry entry)
    {
        if (!entry.IsValid)
        {
            var reason = entry.FailureReason is not null
                ? Enum.Parse<ValidationFailureReason>(entry.FailureReason)
                : ValidationFailureReason.DomainDoesNotAcceptMail;

            return ValidationResult.Failure(reason, entry.FailureMessage ?? "cached failure");
        }

        var mxRecords = entry.MxRecordsJson is null
            ? null
            : JsonSerializer.Deserialize<string[]>(entry.MxRecordsJson);

        return ValidationResult.Success(domain, string.Empty, domain, mxRecords);
    }
}
