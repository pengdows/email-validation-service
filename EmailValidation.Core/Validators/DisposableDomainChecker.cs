namespace EmailValidation.Core.Validators;

/// <summary>
/// Informational check for known disposable/temporary email providers.
/// NO REGEX. Plain domain lookup - matches the rest of the pipeline's approach.
///
/// This does NOT affect IsValid: disposable addresses are still real, deliverable
/// mailboxes. It's a signal for callers who want to reject or flag them by policy,
/// not a correctness check.
/// </summary>
public static class DisposableDomainChecker
{
    // Common disposable/temporary email providers. Not exhaustive - the list of
    // disposable domains changes constantly, so production deployments should
    // extend this via EmailValidatorOptions.AdditionalDisposableDomains rather
    // than expecting this baseline list alone to stay current.
    private static readonly HashSet<string> KnownDisposableDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "mailinator.com",
        "guerrillamail.com",
        "10minutemail.com",
        "tempmail.com",
        "temp-mail.org",
        "yopmail.com",
        "throwawaymail.com",
        "trashmail.com",
        "getnada.com",
        "sharklasers.com",
        "dispostable.com",
        "fakeinbox.com",
        "maildrop.cc",
        "mintemail.com",
        "mohmal.com",
        "moakt.com",
        "emailondeck.com",
        "discard.email",
        "mailnesia.com",
        "spamgourmet.com",
    };

    public static bool IsDisposable(string domain, IReadOnlySet<string>? additionalDomains = null)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        if (KnownDisposableDomains.Contains(domain))
        {
            return true;
        }

        return additionalDomains is not null && additionalDomains.Contains(domain);
    }
}
