using EmailValidation.Core.Validators;

namespace EmailValidation.Core;

public sealed class DefaultDnsValidator : IDnsValidator
{
    public ValueTask<ValidationResult> ValidateDomainExistsAsync(string domain, EmailValidatorOptions options, CancellationToken cancellationToken = default) =>
        new(DnsValidator.ValidateDomainExistsAsync(domain, options, cancellationToken));

    public ValueTask<ValidationResult> ValidateMxRecordsAsync(string domain, EmailValidatorOptions options, CancellationToken cancellationToken = default) =>
        new(DnsValidator.ValidateMxRecordsAsync(domain, options, cancellationToken));
}
