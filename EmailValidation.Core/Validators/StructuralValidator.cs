namespace EmailValidation.Core.Validators;

/// <summary>
/// Layer 1: Structural split and basic input sanitation.
/// NOT RFC validation - just minimal parsing.
/// </summary>
public static class StructuralValidator
{
    /// <summary>
    /// Validates basic structure and splits email into local and domain parts.
    /// </summary>
    /// <param name="email">Email address to validate</param>
    /// <param name="allowLocalDelivery">Allow local-only mailboxes (no @)</param>
    /// <param name="localPart">Output: local part (username)</param>
    /// <param name="domain">Output: domain part (may be null for local delivery)</param>
    /// <returns>Validation result</returns>
    public static ValidationResult Validate(
        string email,
        bool allowLocalDelivery,
        out string? localPart,
        out string? domain)
    {
        localPart = null;
        domain = null;

        if (string.IsNullOrWhiteSpace(email))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                "Email address cannot be empty or whitespace");
        }

        // Trim only leading/trailing whitespace for normalization
        email = email.Trim();

        // Check for whitespace within the address
        if (email.Any(char.IsWhiteSpace))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                "Email address cannot contain whitespace");
        }

        // Check for control characters
        if (email.Any(char.IsControl))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                "Email address cannot contain control characters");
        }

        // Split on @
        var atIndex = email.IndexOf('@');

        // No @ - check policy
        if (atIndex == -1)
        {
            if (!allowLocalDelivery)
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.LocalDeliveryNotAllowed,
                    "Local-only mailboxes are not allowed (missing @ and domain)");
            }

            // Local delivery allowed
            if (string.IsNullOrWhiteSpace(email))
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.InvalidFormat,
                    "Local part cannot be empty");
            }

            localPart = email;
            domain = null;
            return ValidationResult.Success(email, localPart, string.Empty);
        }

        // Check for multiple @
        if (email.LastIndexOf('@') != atIndex)
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                "Email address must contain exactly one @");
        }

        // Extract parts
        localPart = email[..atIndex];
        domain = email[(atIndex + 1)..];

        // Validate parts are non-empty
        if (string.IsNullOrWhiteSpace(localPart))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                "Local part (before @) cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                "Domain part (after @) cannot be empty");
        }

        // Reject IP address literals: user@[192.0.2.1] or user@[IPv6:2001:db8::1]
        // RFC 5321 allows these, but our simplified implementation does not support them
        if (domain.StartsWith('[') || domain.EndsWith(']'))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                "IP address literals (e.g., user@[192.0.2.1]) are not supported in this implementation");
        }

        // Reject comments (parentheses)
        // RFC 5322 allows comments like user(comment)@example.com
        // Our simplified implementation does not support them
        if (localPart.Contains('(') || localPart.Contains(')') ||
            domain.Contains('(') || domain.Contains(')'))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                "Comments (parentheses) are not supported in this implementation");
        }

        // Basic success - further validation required
        return ValidationResult.Success(email, localPart, domain);
    }
}
