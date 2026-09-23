using EmailValidation.Core.Validators;

namespace EmailValidation.Core;

/// <summary>
/// Main email validation orchestrator.
/// Implements the full validation pipeline without regex.
///
/// Pipeline layers:
/// 0. Policy decision (local delivery allowed?)
/// 1. Structural split
/// 2. Local-part validation
/// 3. DNS A/AAAA (domain exists)
/// 4. DNS MX (domain accepts mail) - MANDATORY
/// 5. SMTP verification (optional, not implemented - unreliable)
/// 6. Delivery confirmation (only authoritative method)
///
/// This validator stops at layer 4 (MX records) as the correct stopping point
/// for most systems.
/// </summary>
public class EmailValidator
{
    private readonly EmailValidatorOptions _options;
    private readonly IDnsValidator _dnsValidator;

    public EmailValidator(EmailValidatorOptions? options = null, IDnsValidator? dnsValidator = null)
    {
        _options = options ?? new EmailValidatorOptions();
        _dnsValidator = dnsValidator ?? new DefaultDnsValidator();
    }

    /// <summary>
    /// Validates an email address through the complete pipeline.
    /// </summary>
    /// <param name="email">Email address to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with specific failure reason if invalid</returns>
    public async ValueTask<ValidationResult> ValidateAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();

        // Layer 0: Policy check implicit in Layer 1

        // Layer 1: Structural split
        var structuralResult = StructuralValidator.Validate(
            normalizedEmail,
            _options.AllowLocalDelivery,
            out var localPart,
            out var domain);

        if (!structuralResult.IsValid)
        {
            return structuralResult;
        }

        // If local delivery is allowed and no domain, we're done
        if (string.IsNullOrEmpty(domain))
        {
            return ValidationResult.Success(
                normalizedEmail,
                localPart!,
                string.Empty,
                isRoleBased: RoleBasedLocalPartChecker.IsRoleBased(localPart!, _options.AdditionalRoleBasedLocalParts));
        }

        // Layer 2: Local-part validation
        var localPartResult = LocalPartValidator.Validate(localPart!);
        if (!localPartResult.IsValid)
        {
            return localPartResult;
        }

        // Layer 2b: Domain-part syntax validation (RFC 1035/952/1123 hostname rules)
        var domainSyntaxResult = DomainSyntaxValidator.Validate(domain);
        if (!domainSyntaxResult.IsValid)
        {
            return domainSyntaxResult;
        }

        // Layer 3: DNS A/AAAA check (domain exists)
        if (_options.CheckDomainExists)
        {
            var domainExistsResult = await _dnsValidator.ValidateDomainExistsAsync(domain, _options, cancellationToken);
            if (!domainExistsResult.IsValid)
            {
                // InternalAddressBlocked is a hard SSRF block - never overridden by
                // a later layer, MX success included. Genuine non-existence (no
                // A/AAAA at all) is only fatal here if there's no MX check to defer
                // to - a domain can have MX records with no A/AAAA at the apex.
                if (domainExistsResult.FailureReason == ValidationFailureReason.InternalAddressBlocked
                    || !_options.CheckMxRecords)
                {
                    return domainExistsResult;
                }
            }
        }

        var isDisposable = DisposableDomainChecker.IsDisposable(domain, _options.AdditionalDisposableDomains);
        var isRoleBased = RoleBasedLocalPartChecker.IsRoleBased(localPart!, _options.AdditionalRoleBasedLocalParts);

        // Layer 4: MX record check (domain accepts mail) - MANDATORY for Internet mail
        if (_options.CheckMxRecords)
        {
            var mxResult = await _dnsValidator.ValidateMxRecordsAsync(domain, _options, cancellationToken);
            if (!mxResult.IsValid)
            {
                return mxResult;
            }

            // Success with MX records
            return ValidationResult.Success(
                $"{localPart}@{domain}",
                localPart!,
                domain,
                mxResult.MxRecords,
                isDisposable,
                isRoleBased);
        }

        // Success without MX check (not recommended for production)
        return ValidationResult.Success($"{localPart}@{domain}", localPart!, domain, isDisposable: isDisposable, isRoleBased: isRoleBased);
    }

    /// <summary>
    /// Validates multiple email addresses concurrently.
    /// </summary>
    public async ValueTask<Dictionary<string, ValidationResult>> ValidateBatchAsync(
        IEnumerable<string> emails,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, ValidationResult>(StringComparer.OrdinalIgnoreCase);
        var domainGroups = new Dictionary<string, List<(string Email, string LocalPart, string Domain)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var email in emails)
        {
            var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;

            if (results.ContainsKey(normalizedEmail))
            {
                continue;
            }

            var structuralResult = StructuralValidator.Validate(
                normalizedEmail,
                _options.AllowLocalDelivery,
                out var localPart,
                out var domain);

            if (!structuralResult.IsValid)
            {
                results[normalizedEmail] = structuralResult;
                continue;
            }

            if (!string.IsNullOrEmpty(domain))
            {
                var domainSyntaxResult = DomainSyntaxValidator.Validate(domain);
                if (!domainSyntaxResult.IsValid)
                {
                    results[normalizedEmail] = domainSyntaxResult;
                    continue;
                }
            }

            if (string.IsNullOrEmpty(domain))
            {
                results[normalizedEmail] = ValidationResult.Success(
                    normalizedEmail,
                    localPart!,
                    string.Empty,
                    isRoleBased: RoleBasedLocalPartChecker.IsRoleBased(localPart!, _options.AdditionalRoleBasedLocalParts));
                continue;
            }

            if (!domainGroups.TryGetValue(domain, out var group))
            {
                group = new List<(string Email, string LocalPart, string Domain)>();
                domainGroups[domain] = group;
            }

            group.Add((normalizedEmail, localPart!, domain));
        }

        foreach (var group in domainGroups.Values)
        {
            var domain = group[0].Domain;
            string[]? mxRecords = null;
            var isDisposable = DisposableDomainChecker.IsDisposable(domain, _options.AdditionalDisposableDomains);

            if (_options.CheckDomainExists)
            {
                var domainExistsResult = await _dnsValidator.ValidateDomainExistsAsync(domain, _options, cancellationToken);
                if (!domainExistsResult.IsValid
                    && (domainExistsResult.FailureReason == ValidationFailureReason.InternalAddressBlocked
                        || !_options.CheckMxRecords))
                {
                    foreach (var entry in group)
                    {
                        results[entry.Email] = domainExistsResult;
                    }

                    continue;
                }
            }

            if (_options.CheckMxRecords)
            {
                var mxResult = await _dnsValidator.ValidateMxRecordsAsync(domain, _options, cancellationToken);
                if (!mxResult.IsValid)
                {
                    foreach (var entry in group)
                    {
                        results[entry.Email] = mxResult;
                    }

                    continue;
                }

                mxRecords = mxResult.MxRecords;
            }

            foreach (var entry in group)
            {
                var localPartResult = LocalPartValidator.Validate(entry.LocalPart);
                if (!localPartResult.IsValid)
                {
                    results[entry.Email] = localPartResult;
                    continue;
                }

                results[entry.Email] = ValidationResult.Success(
                    $"{entry.LocalPart}@{entry.Domain}",
                    entry.LocalPart,
                    entry.Domain,
                    mxRecords,
                    isDisposable,
                    RoleBasedLocalPartChecker.IsRoleBased(entry.LocalPart, _options.AdditionalRoleBasedLocalParts));
            }
        }

        return results;
    }
}

/// <summary>
/// Configuration options for email validation.
/// </summary>
public class EmailValidatorOptions
{
    /// <summary>
    /// Allow local-only mailboxes (e.g., "root", "postmaster") without @domain.
    /// Default: false (Internet mail only)
    /// </summary>
    public bool AllowLocalDelivery { get; set; } = false;

    /// <summary>
    /// Check if domain exists via DNS A/AAAA records.
    /// Default: true
    /// </summary>
    public bool CheckDomainExists { get; set; } = true;

    /// <summary>
    /// Check if domain accepts mail via MX records.
    /// CRITICAL: Should always be true for production Internet mail.
    /// No MX record = no mail delivery.
    /// Default: true
    /// </summary>
    public bool CheckMxRecords { get; set; } = true;

    /// <summary>
    /// Allow domains that resolve to internal/private IP addresses (e.g., localhost).
    /// Default: false (SSRF protection)
    /// </summary>
    public bool AllowInternalDomains { get; set; } = false;

    /// <summary>
    /// Primary DNS server to use for lookups. If null, falls back to system defaults.
    /// </summary>
    public string? PrimaryDnsServer { get; set; }

    /// <summary>
    /// Secondary DNS server to use for lookups.
    /// </summary>
    public string? SecondaryDnsServer { get; set; }

    /// <summary>
    /// If true, allows fallback to public DNS (8.8.8.8, 1.1.1.1) if no other servers are configured.
    /// Default: false
    /// </summary>
    public bool AllowPublicDnsFallback { get; set; } = false;

    /// <summary>
    /// If true, exception messages will be included in validation failures.
    /// Default: false (prevents information disclosure)
    /// </summary>
    public bool DetailedErrorMessages { get; set; } = false;

    /// <summary>
    /// Extra disposable-email domains to flag beyond the built-in baseline list.
    /// The baseline list is not exhaustive; production deployments should keep
    /// this current rather than relying on the built-in set alone.
    /// </summary>
    public IReadOnlySet<string>? AdditionalDisposableDomains { get; set; }

    /// <summary>
    /// Extra role-based local-parts to flag beyond the built-in baseline list.
    /// </summary>
    public IReadOnlySet<string>? AdditionalRoleBasedLocalParts { get; set; }
}
