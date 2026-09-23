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

    public ValueTask<ValidationResult> ValidateDomainExistsAsync(string domain, EmailValidatorOptions options, CancellationToken cancellationToken = default)
    {
        DomainExistsCalls[domain] = DomainExistsCalls.TryGetValue(domain, out var count) ? count + 1 : 1;
        return new ValueTask<ValidationResult>(_domainExists(domain));
    }

    public ValueTask<ValidationResult> ValidateMxRecordsAsync(string domain, EmailValidatorOptions options, CancellationToken cancellationToken = default)
    {
        MxCalls[domain] = MxCalls.TryGetValue(domain, out var count) ? count + 1 : 1;
        return new ValueTask<ValidationResult>(_mxRecords(domain));
    }
}
