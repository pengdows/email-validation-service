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
    public async Task<ValidationResult> ValidateAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

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
            return ValidationResult.Success(normalizedEmail, localPart!, string.Empty);
        }

        // Layer 2: Local-part validation
        var localPartResult = LocalPartValidator.Validate(localPart!);
        if (!localPartResult.IsValid)
        {
            return localPartResult;
        }

        // Layer 3: DNS A/AAAA check (domain exists)
        if (_options.CheckDomainExists)
        {
            var domainExistsResult = await _dnsValidator.ValidateDomainExistsAsync(domain, cancellationToken);
            if (!domainExistsResult.IsValid)
            {
                return domainExistsResult;
            }
        }

        // Layer 4: MX record check (domain accepts mail) - MANDATORY for Internet mail
        if (_options.CheckMxRecords)
        {
            var mxResult = await _dnsValidator.ValidateMxRecordsAsync(domain, cancellationToken);
            if (!mxResult.IsValid)
            {
                return mxResult;
            }

            // Success with MX records
            return ValidationResult.Success(
                $"{localPart}@{domain}",
                localPart!,
                domain,
                mxResult.MxRecords);
        }

        // Success without MX check (not recommended for production)
        return ValidationResult.Success($"{localPart}@{domain}", localPart!, domain);
    }

    /// <summary>
    /// Validates multiple email addresses concurrently.
    /// </summary>
    public async Task<Dictionary<string, ValidationResult>> ValidateBatchAsync(
        IEnumerable<string> emails,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, ValidationResult>(StringComparer.OrdinalIgnoreCase);
        var domainGroups = new Dictionary<string, List<(string Email, string LocalPart, string Domain)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var email in emails)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();

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

            if (string.IsNullOrEmpty(domain))
            {
                results[normalizedEmail] = ValidationResult.Success(normalizedEmail, localPart!, string.Empty);
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

            if (_options.CheckDomainExists)
            {
                var domainExistsResult = await _dnsValidator.ValidateDomainExistsAsync(domain, cancellationToken);
                if (!domainExistsResult.IsValid)
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
                var mxResult = await _dnsValidator.ValidateMxRecordsAsync(domain, cancellationToken);
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
                    mxRecords);
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
}
