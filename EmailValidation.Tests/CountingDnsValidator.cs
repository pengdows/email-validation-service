using EmailValidation.Core;

namespace EmailValidation.Tests;

internal sealed class CountingDnsValidator : IDnsValidator
{
    private readonly Func<string, ValidationResult> _domainExists;
    private readonly Func<string, ValidationResult> _mxRecords;

    public CountingDnsValidator(
        Func<string, ValidationResult>? domainExists = null,
        Func<string, ValidationResult>? mxRecords = null)
    {
        _domainExists = domainExists ?? (_ => ValidationResult.Success("domain", string.Empty, "domain"));
        _mxRecords = mxRecords ?? (_ => ValidationResult.Success("domain", string.Empty, "domain", ["mail.example.com"]));
    }

    public Dictionary<string, int> DomainExistsCalls { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> MxCalls { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<ValidationResult> ValidateDomainExistsAsync(string domain, CancellationToken cancellationToken = default)
    {
        DomainExistsCalls[domain] = DomainExistsCalls.TryGetValue(domain, out var count) ? count + 1 : 1;
        return Task.FromResult(_domainExists(domain));
    }

    public Task<ValidationResult> ValidateMxRecordsAsync(string domain, CancellationToken cancellationToken = default)
    {
        MxCalls[domain] = MxCalls.TryGetValue(domain, out var count) ? count + 1 : 1;
        return Task.FromResult(_mxRecords(domain));
    }
}
