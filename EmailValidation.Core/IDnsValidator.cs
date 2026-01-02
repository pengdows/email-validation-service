namespace EmailValidation.Core;

public interface IDnsValidator
{
    Task<ValidationResult> ValidateDomainExistsAsync(string domain, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidateMxRecordsAsync(string domain, CancellationToken cancellationToken = default);
}
