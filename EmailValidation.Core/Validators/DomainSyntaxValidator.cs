namespace EmailValidation.Core.Validators;

/// <summary>
/// Layer 2b: Domain-part syntax validation - RFC 1035/952/1123 hostname label rules.
/// NO REGEX. Character-by-character validation, matching LocalPartValidator's approach.
///
/// This is separate from DNS existence (DnsValidator): a domain can be
/// syntactically well-formed and still not exist. RFC 2181 allows arbitrary
/// binary DNS labels, but email domains are held to the stricter hostname
/// subset (RFC 952/1123), which is what mail routing software actually expects.
///
/// A single label with no dot (e.g. "localhost") is intentionally valid here -
/// it's a legitimate hostname, just not an Internet-routable one. That's a DNS/
/// MX question, not a syntax question.
/// </summary>
public static class DomainSyntaxValidator
{
    public static ValidationResult Validate(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                "Domain cannot be empty");
        }

        if (domain[0] == '.' || domain[^1] == '.')
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                "Domain cannot start or end with a dot");
        }

        foreach (var label in domain.Split('.'))
        {
            var labelResult = ValidateLabel(label);
            if (!labelResult.IsValid)
            {
                return labelResult;
            }
        }

        return ValidationResult.Success(domain, string.Empty, domain);
    }

    private static ValidationResult ValidateLabel(string label)
    {
        if (label.Length == 0)
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                "Domain cannot contain consecutive dots (empty label)");
        }

        // RFC 1035 Section 2.3.4: labels are 63 octets or less
        if (label.Length > 63)
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                $"Domain label '{label}' exceeds maximum length of 63 characters");
        }

        if (label[0] == '-' || label[^1] == '-')
        {
            return ValidationResult.Failure(
                ValidationFailureReason.InvalidFormat,
                $"Domain label '{label}' cannot start or end with a hyphen");
        }

        foreach (var c in label)
        {
            var isAsciiLetter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
            var isAsciiDigit = c >= '0' && c <= '9';
            var isHyphen = c == '-';

            if (!isAsciiLetter && !isAsciiDigit && !isHyphen)
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.InvalidFormat,
                    $"Domain label contains invalid character: '{c}' (0x{(int)c:X2}). " +
                    "Only ASCII letters, digits, and hyphens are permitted (RFC 1123 hostname syntax).");
            }
        }

        return ValidationResult.Success(label, string.Empty, label);
    }
}
