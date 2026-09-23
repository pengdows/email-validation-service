using EmailValidation.Caching;
using EmailValidation.Core;
using FluentAssertions;

namespace EmailValidation.Tests;

/// <summary>
/// Exercises CachingDnsValidator against a real, temp-file SQLite database via
/// pengdows.crud - not a mock of the persistence layer, since the whole point is
/// to prove the cache actually round-trips through a real backing store.
/// </summary>
public sealed class CachingDnsValidatorTests : IDisposable
{
    private readonly string _dbPath;

    public CachingDnsValidatorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dns-cache-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task<(CachingDnsValidator Caching, CountingDnsValidator Inner)> CreateAsync(
        TimeSpan? positiveTtl = null, TimeSpan? negativeTtl = null)
    {
        var inner = new CountingDnsValidator(
            domainExists: _ => ValidationResult.Success("domain", string.Empty, "domain"),
            mxRecords: _ => ValidationResult.Success("domain", string.Empty, "domain", ["mail1.example.com", "mail2.example.com"]));

        var caching = await SqliteDnsCache.CreateAsync(inner, _dbPath, positiveTtl, negativeTtl);
        return (caching, inner);
    }

    [Fact]
    public async Task ValidateMxRecordsAsync_CacheMiss_CallsInnerAndPersists()
    {
        var (caching, inner) = await CreateAsync();

        var result = await caching.ValidateMxRecordsAsync("example.com", new EmailValidatorOptions());

        result.IsValid.Should().BeTrue();
        result.MxRecords.Should().Equal("mail1.example.com", "mail2.example.com");
        inner.MxCalls["example.com"].Should().Be(1);
    }

    [Fact]
    public async Task ValidateMxRecordsAsync_CacheHit_DoesNotCallInnerAgain()
    {
        var (caching, inner) = await CreateAsync();

        await caching.ValidateMxRecordsAsync("example.com", new EmailValidatorOptions());
        var second = await caching.ValidateMxRecordsAsync("example.com", new EmailValidatorOptions());

        second.IsValid.Should().BeTrue();
        second.MxRecords.Should().Equal("mail1.example.com", "mail2.example.com");
        inner.MxCalls["example.com"].Should().Be(1, "the second call should be served entirely from cache");
    }

    [Fact]
    public async Task ValidateMxRecordsAsync_ExpiredEntry_CallsInnerAgain()
    {
        var (caching, inner) = await CreateAsync(positiveTtl: TimeSpan.FromMilliseconds(1));

        await caching.ValidateMxRecordsAsync("example.com", new EmailValidatorOptions());
        await Task.Delay(20);
        await caching.ValidateMxRecordsAsync("example.com", new EmailValidatorOptions());

        inner.MxCalls["example.com"].Should().Be(2, "an expired entry must be refreshed, not served stale");
    }

    [Fact]
    public async Task ValidateDomainExistsAsync_AndValidateMxRecordsAsync_AreCachedIndependently()
    {
        var (caching, inner) = await CreateAsync();

        await caching.ValidateDomainExistsAsync("example.com", new EmailValidatorOptions());
        await caching.ValidateMxRecordsAsync("example.com", new EmailValidatorOptions());

        // Second round should hit cache for both, independently.
        await caching.ValidateDomainExistsAsync("example.com", new EmailValidatorOptions());
        await caching.ValidateMxRecordsAsync("example.com", new EmailValidatorOptions());

        inner.DomainExistsCalls["example.com"].Should().Be(1);
        inner.MxCalls["example.com"].Should().Be(1);
    }

    [Fact]
    public async Task ValidateMxRecordsAsync_FailureResult_IsCachedAndRoundTrips()
    {
        var inner = new CountingDnsValidator(
            mxRecords: _ => ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotAcceptMail,
                "Domain 'nomx.test' has no MX records - cannot accept mail"));

        var caching = await SqliteDnsCache.CreateAsync(inner, _dbPath);

        var first = await caching.ValidateMxRecordsAsync("nomx.test", new EmailValidatorOptions());
        var second = await caching.ValidateMxRecordsAsync("nomx.test", new EmailValidatorOptions());

        second.IsValid.Should().BeFalse();
        second.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotAcceptMail);
        second.FailureMessage.Should().Be(first.FailureMessage);
        inner.MxCalls["nomx.test"].Should().Be(1, "a cached failure should not re-trigger a lookup before it expires");
    }

    [Fact]
    public async Task ValidateDomainExistsAsync_InternalAddressBlocked_RoundTripsReason()
    {
        var inner = new CountingDnsValidator(
            domainExists: _ => ValidationResult.Failure(
                ValidationFailureReason.InternalAddressBlocked,
                "Domain 'evil.test' resolves to internal/private address"));

        var caching = await SqliteDnsCache.CreateAsync(inner, _dbPath);

        await caching.ValidateDomainExistsAsync("evil.test", new EmailValidatorOptions());
        var cached = await caching.ValidateDomainExistsAsync("evil.test", new EmailValidatorOptions());

        cached.FailureReason.Should().Be(ValidationFailureReason.InternalAddressBlocked);
    }
}
