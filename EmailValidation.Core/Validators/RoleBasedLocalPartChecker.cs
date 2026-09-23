namespace EmailValidation.Core.Validators;

/// <summary>
/// Informational check for role-based mailboxes (e.g. "support@", "noreply@")
/// as opposed to an individual's personal address.
/// NO REGEX. Plain local-part lookup - matches the rest of the pipeline's approach.
///
/// This does NOT affect IsValid: role addresses are still real, deliverable
/// mailboxes. It's a signal for callers who want to treat them differently by
/// policy (e.g. lead-quality scoring), not a correctness check.
/// </summary>
public static class RoleBasedLocalPartChecker
{
    private static readonly HashSet<string> KnownRoleBasedLocalParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin",
        "administrator",
        "support",
        "help",
        "info",
        "sales",
        "contact",
        "webmaster",
        "postmaster",
        "hostmaster",
        "abuse",
        "noreply",
        "no-reply",
        "donotreply",
        "do-not-reply",
        "root",
        "billing",
        "marketing",
        "security",
        "privacy",
        "legal",
        "press",
        "jobs",
        "careers",
        "feedback",
        "office",
        "team",
    };

    public static bool IsRoleBased(string localPart, IReadOnlySet<string>? additionalLocalParts = null)
    {
        if (string.IsNullOrWhiteSpace(localPart))
        {
            return false;
        }

        if (KnownRoleBasedLocalParts.Contains(localPart))
        {
            return true;
        }

        return additionalLocalParts is not null && additionalLocalParts.Contains(localPart);
    }
}
