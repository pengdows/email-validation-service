namespace EmailValidation.Core.Validators;

/// <summary>
/// Layer 2: Local-part (username) validation - server-agnostic RFC rules.
/// NO REGEX. Character-by-character validation following RFC constraints.
/// </summary>
public static class LocalPartValidator
{
    // Allowed special characters in local part (non-alphanumeric)
    private static readonly HashSet<char> AllowedSpecialChars = new()
    {
        '!', '#', '$', '%', '&', '\'', '*', '+', '-', '/', '=', '?', '^', '_', '`', '{', '|', '}', '~', '.'
    };

    /// <summary>
    /// Validates local part according to RFC rules (simplified, practical subset).
    /// </summary>
    public static ValidationResult Validate(string localPart)
    {
        if (string.IsNullOrWhiteSpace(localPart))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidLocalPart,
                "Local part cannot be empty");
        }

        // Rule: Cannot start with dot
        if (localPart[0] == '.')
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidLocalPart,
                "Local part cannot start with a dot (.)");
        }

        // Rule: Cannot end with dot
        if (localPart[^1] == '.')
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidLocalPart,
                "Local part cannot end with a dot (.)");
        }

        // Rule: Cannot contain consecutive dots
        for (int i = 0; i < localPart.Length - 1; i++)
        {
            if (localPart[i] == '.' && localPart[i + 1] == '.')
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.InvalidLocalPart,
                    "Local part cannot contain consecutive dots (..)");
            }
        }

        // Rule: Only allowed characters (ASCII only - no Unicode)
        // RFC 5321 Section 2.4: SMTP uses ASCII characters only
        // RFC 6531 (SMTPUTF8) extends this, but we implement the basic ASCII-only version
        foreach (char c in localPart)
        {
            bool isAsciiLetter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
            bool isAsciiDigit = (c >= '0' && c <= '9');
            bool isAllowedSpecial = AllowedSpecialChars.Contains(c);
            bool isValid = isAsciiLetter || isAsciiDigit || isAllowedSpecial;

            if (!isValid)
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.InvalidLocalPart,
                    $"Local part contains invalid character: '{c}' (0x{(int)c:X2}). Only ASCII letters, digits, and allowed special characters are permitted.");
            }
        }

        // All checks passed
        return ValidationResult.Success(localPart, localPart, string.Empty);
    }

    /// <summary>
    /// Additional note about provider-specific normalization (e.g., Gmail dots).
    /// Gmail ignores dots in routing but the rules above still apply.
    /// Example:
    ///   - user@gmail.com and u.ser@gmail.com route to same mailbox
    ///   - BUT .user@gmail.com is still INVALID
    ///   - AND user..name@gmail.com is still INVALID
    ///
    /// This validator enforces RFC placement rules regardless of provider behavior.
    /// </summary>
    public static string GetNormalizationNote() =>
        "Note: Some providers (e.g., Gmail) normalize dots in routing, but RFC placement rules still apply at validation time.";
}
