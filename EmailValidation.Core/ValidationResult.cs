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

    /// <summary>
    /// True if the domain is a known disposable/temporary email provider.
    /// Informational only - does not affect IsValid (disposable addresses are still deliverable).
    /// </summary>
    public bool IsDisposable { get; init; }

    /// <summary>
    /// True if the local-part is a known role-based mailbox (e.g. admin, support, noreply)
    /// rather than an individual's address.
    /// Informational only - does not affect IsValid (role addresses are still deliverable).
    /// </summary>
    public bool IsRoleBased { get; init; }

    public static ValidationResult Success(
        string normalizedEmail,
        string localPart,
        string domain,
        string[]? mxRecords = null,
        bool isDisposable = false,
        bool isRoleBased = false) =>
        new()
        {
            IsValid = true,
            NormalizedEmail = normalizedEmail,
            LocalPart = localPart,
            Domain = domain,
            MxRecords = mxRecords,
            IsDisposable = isDisposable,
            IsRoleBased = isRoleBased
        };

    public static ValidationResult Failure(ValidationFailureReason reason, string message) =>
        new()
        {
            IsValid = false,
            FailureReason = reason,
            FailureMessage = message
        };
}
