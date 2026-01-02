namespace EmailValidation.Core;

/// <summary>
/// Result of email validation.
/// Validation is a pipeline, not a binary check.
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid { get; init; }
    public ValidationFailureReason? FailureReason { get; init; }
    public string? FailureMessage { get; init; }

    /// <summary>
    /// The normalized email address (if valid)
    /// </summary>
    public string? NormalizedEmail { get; init; }

    /// <summary>
    /// Local part (username) after structural split
    /// </summary>
    public string? LocalPart { get; init; }

    /// <summary>
    /// Domain part after structural split
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>
    /// MX records found (if DNS check completed)
    /// </summary>
    public string[]? MxRecords { get; init; }

    public static ValidationResult Success(string normalizedEmail, string localPart, string domain, string[]? mxRecords = null) =>
        new()
        {
            IsValid = true,
            NormalizedEmail = normalizedEmail,
            LocalPart = localPart,
            Domain = domain,
            MxRecords = mxRecords
        };

    public static ValidationResult Failure(ValidationFailureReason reason, string message) =>
        new()
        {
            IsValid = false,
            FailureReason = reason,
            FailureMessage = message
        };
}
