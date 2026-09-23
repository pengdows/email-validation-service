namespace EmailValidation.Core;

public interface IDnsValidator
{
    ValueTask<ValidationResult> ValidateDomainExistsAsync(string domain, EmailValidatorOptions options, CancellationToken cancellationToken = default);
    ValueTask<ValidationResult> ValidateMxRecordsAsync(string domain, EmailValidatorOptions options, CancellationToken cancellationToken = default);
}
