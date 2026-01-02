using EmailValidation.Core.Validators;

namespace EmailValidation.Core;

public sealed class DefaultDnsValidator : IDnsValidator
{
    public Task<ValidationResult> ValidateDomainExistsAsync(string domain, CancellationToken cancellationToken = default) =>
        DnsValidator.ValidateDomainExistsAsync(domain, cancellationToken);

    public Task<ValidationResult> ValidateMxRecordsAsync(string domain, CancellationToken cancellationToken = default) =>
        DnsValidator.ValidateMxRecordsAsync(domain, cancellationToken);
}
