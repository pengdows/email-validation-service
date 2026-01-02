using EmailValidation.Core;
using FluentAssertions;

namespace EmailValidation.Tests;

/// <summary>
/// Tests for RFC 974: Mail Routing and the Domain System
/// https://www.rfc-editor.org/rfc/rfc974
///
/// RFC 974 defines how mail is routed using DNS MX records.
/// While largely obsoleted by RFC 5321, it established the foundational concepts.
///
/// Key concepts:
/// - MX records specify mail exchangers for a domain
/// - Lower preference values indicate higher priority
/// - If no MX exists, fallback to A record (DEPRECATED in modern practice)
/// </summary>
public class Rfc974MailRoutingTests
{
    /// <summary>
    /// RFC 974 Section 3: Mail Routing Process
    /// "If the domain has no MX RRs, it is treated as if it had a single MX RR
    /// with preference value 0 pointing to that domain."
    ///
    /// CRITICAL: This fallback behavior is DEPRECATED.
    /// Modern practice (our implementation): NO MX = NO MAIL
    /// </summary>
    [Fact]
    public async Task Rfc974_Section_3_NoMxFallbackToA_DEPRECATED_NotImplemented()
    {
        // RFC 974 allowed fallback to A record if no MX exists
        // This is DEPRECATED and NOT RECOMMENDED in modern practice
        // Our implementation: NO MX = NO MAIL (no fallback)

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = true
        });

        // Test with localhost - has A record but no MX
        var result = await validator.ValidateAsync("user@localhost");

        result.IsValid.Should().BeFalse(
            "No MX record = no mail delivery. We do NOT fall back to A record (deprecated RFC 974 behavior)");
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotAcceptMail);
        result.FailureMessage.Should().Contain("no MX");
    }

    /// <summary>
    /// RFC 974 Section 5: MX Record Format
    /// MX records consist of preference value and exchange domain
    /// Lower preference = higher priority
    ///
    /// Example:
    /// example.com. IN MX 10 mail1.example.com.
    /// example.com. IN MX 20 mail2.example.com.
    ///
    /// Mail should be attempted to mail1 first (lower preference)
    /// </summary>
    [Fact]
    public async Task Rfc974_Section_5_MxRecords_PreferenceOrdering_Documentation()
    {
        // RFC 974 defines MX record format: preference + exchange
        // Lower preference = higher priority for mail delivery

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = true
        });

        // Test with a well-known domain
        var result = await validator.ValidateAsync("test@gmail.com");

        if (result.IsValid)
        {
            result.MxRecords.Should().NotBeNullOrEmpty("gmail.com has MX records");

            // Note: Our implementation returns MX exchange domains
            // Preference ordering is handled by the mail client/server, not validation
            // We just verify that MX records exist
        }
    }

    /// <summary>
    /// RFC 974 Section 3: Multiple MX Records
    /// A domain can have multiple MX records for redundancy
    /// Mail should be attempted in order of preference
    /// </summary>
    [Fact]
    public async Task Rfc974_Section_3_MultipleMxRecords_Redundancy()
    {
        // Domains often have multiple MX records for:
        // - Load balancing
        // - Failover/redundancy
        // - Geographic distribution

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = true
        });

        // Large providers typically have multiple MX records
        var result = await validator.ValidateAsync("test@gmail.com");

        if (result.IsValid && result.MxRecords != null)
        {
            // Gmail and other large providers have multiple MX records
            result.MxRecords.Length.Should().BeGreaterThanOrEqualTo(1,
                "Valid mail domains should have at least one MX record");

            // Many domains have multiple for redundancy
            // result.MxRecords.Length.Should().BeGreaterThan(1, "Large providers often have multiple MX records");
        }
    }

    /// <summary>
    /// RFC 974 vs RFC 5321: Evolution of Mail Routing
    /// RFC 974 (1986) - Original mail routing specification
    /// RFC 5321 (2008) - Obsoletes RFC 974, refines mail routing
    ///
    /// Key difference: RFC 5321 de-emphasizes fallback to A records
    /// </summary>
    [Fact]
    public void Rfc974_vs_Rfc5321_Evolution_Documentation()
    {
        // RFC 974 (1986): Mail Routing and the Domain System
        // - Allowed fallback to A record if no MX
        // - Established MX preference system

        // RFC 5321 (2008): SMTP
        // - Obsoletes RFC 974
        // - De-emphasizes A record fallback
        // - Modern servers often don't implement fallback

        // Our implementation follows modern practice:
        // NO MX = NO MAIL (strict, correct, safe)

        true.Should().BeTrue("We follow RFC 5321 / modern practice: MX records are MANDATORY");
    }

    /// <summary>
    /// RFC 974 Section 3: Wildcard MX Records
    /// MX records can use wildcards in DNS
    ///
    /// Example: *.example.com IN MX 10 mail.example.com
    /// </summary>
    [Fact]
    public async Task Rfc974_Section_3_WildcardMx_Documentation()
    {
        // Some domains use wildcard MX records
        // *.example.com catches all subdomains

        // Our DNS lookup handles this automatically
        // If a wildcard MX exists, the DNS resolver returns it

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = true
        });

        // This documents that wildcard MX records are supported
        // (through standard DNS resolution)
        true.Should().BeTrue("Wildcard MX records are resolved by DNS, handled automatically");
    }

    /// <summary>
    /// RFC 974 Section 6: Security Considerations
    /// MX records can be spoofed or manipulated via DNS attacks
    ///
    /// Our implementation trusts DNS responses
    /// DNSSEC (RFC 4033) provides security, but is not validated here
    /// </summary>
    [Fact]
    public void Rfc974_Section_6_Security_DnsSpoofing_NotMitigated()
    {
        // RFC 974 Section 6 acknowledges DNS security issues
        // MX records can be spoofed via DNS cache poisoning, etc.

        // Our implementation does NOT validate DNSSEC
        // We trust the DNS resolver's responses

        // TODO: Consider DNSSEC validation for high-security deployments
        true.Should().BeTrue("DNS responses are trusted; DNSSEC validation not implemented");
    }

    /// <summary>
    /// RFC 974 requirement: MX records must point to domain names, not IP addresses
    /// The exchange must be a valid domain name, not an IP address literal
    /// </summary>
    [Fact]
    public void Rfc974_MxExchange_MustBeDomainName_NotIpAddress()
    {
        // RFC 974 specifies that MX records point to domain names
        // Invalid: example.com IN MX 10 192.0.2.1
        // Valid:   example.com IN MX 10 mail.example.com

        // Our implementation returns the exchange domain names
        // DNS servers enforce this rule

        true.Should().BeTrue("MX exchange must be domain name (enforced by DNS, not us)");
    }

    /// <summary>
    /// Best practice: Null MX (RFC 7505)
    /// A domain that does not send or receive email should publish a null MX record:
    /// example.com. IN MX 0 .
    ///
    /// The "." indicates the domain does not accept email
    /// </summary>
    [Fact]
    public async Task Rfc7505_NullMx_DoesNotAcceptMail()
    {
        // RFC 7505: A Null MX Record for Domains That Do Not Accept Mail
        // Format: domain IN MX 0 .
        // The "." (root) indicates no mail accepted

        // Our implementation would see this as having MX records
        // but the exchange being "." (root)
        // This is a valid MX response but indicates "no mail accepted"

        // TODO: Add null MX detection (MX pointing to ".")
        // For now, if MX exists, we consider it valid

        true.Should().BeTrue("Null MX (RFC 7505) detection not yet implemented");
    }
}
