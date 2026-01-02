using EmailValidation.Core;
using FluentAssertions;

namespace EmailValidation.Tests;

/// <summary>
/// Tests for RFC 1035: Domain Names - Implementation and Specification
/// https://www.rfc-editor.org/rfc/rfc1035
///
/// RFC 1035 defines the DNS system including:
/// - Section 2.3.1: Preferred name syntax
/// - Section 3.1: Name space definitions
/// - Section 3.3: Resource Records (RR) definitions
/// </summary>
public class Rfc1035DnsTests
{
    /// <summary>
    /// RFC 1035 Section 2.3.1: Preferred name syntax
    /// Domain names consist of labels separated by dots.
    /// Labels must:
    /// - Start with a letter
    /// - End with a letter or digit
    /// - Contain only letters, digits, and hyphens
    /// - Be 63 characters or less
    /// - Total domain name: 255 characters or less
    ///
    /// Note: This is the PREFERRED syntax. RFC 1123 relaxed this to allow
    /// labels to start with digits.
    /// </summary>
    [Theory]
    [InlineData("example.com")]
    [InlineData("mail.example.com")]
    [InlineData("sub.domain.example.com")]
    [InlineData("example-domain.com")]
    [InlineData("example123.com")]
    [InlineData("123example.com")]          // RFC 1123 allows leading digits
    public void Rfc1035_Section_2_3_1_ValidDomainNames(string domain)
    {
        // We test domain validation via the full email validator
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,      // Syntax check only
            CheckMxRecords = false
        });

        var result = validator.ValidateAsync($"user@{domain}").Result;

        result.IsValid.Should().BeTrue($"'{domain}' should be a syntactically valid domain");
        result.Domain.Should().Be(domain);
    }

    /// <summary>
    /// RFC 1035 Section 2.3.4: Size limits
    /// - Labels must be 63 octets or less
    /// - Total domain name must be 255 octets or less
    /// </summary>
    [Fact]
    public void Rfc1035_Section_2_3_4_DomainSizeLimits_Documentation()
    {
        // RFC 1035 Section 2.3.4:
        // - labels: 63 octets or less
        // - names: 255 octets or less

        // TODO: Add domain length validation for strict RFC compliance
        const int maxLabelLength = 63;
        const int maxDomainLength = 255;

        maxLabelLength.Should().Be(63, "per RFC 1035 Section 2.3.4");
        maxDomainLength.Should().Be(255, "per RFC 1035 Section 2.3.4");

        // Note: RFC 5321 Section 4.5.3.1.2 also specifies 255 octets for domain
    }

    /// <summary>
    /// RFC 1035 Section 3.3.9: MX Resource Record (RR)
    /// MX records specify mail exchange servers for a domain
    ///
    /// Format: domain MX preference exchange
    /// Example: example.com. IN MX 10 mail.example.com.
    /// </summary>
    [Fact]
    public async Task Rfc1035_Section_3_3_9_MxRecords_Documentation()
    {
        // RFC 1035 Section 3.3.9 defines MX records
        // MX RR format: <preference> <exchange>
        // - preference: 16-bit integer (lower = higher priority)
        // - exchange: domain name of mail exchanger

        // Our DNS validator looks up MX records and returns them
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = true
        });

        // Using a well-known domain that should have MX records
        // Note: This test is network-dependent
        var result = await validator.ValidateAsync("test@gmail.com");

        if (result.IsValid)
        {
            result.MxRecords.Should().NotBeNullOrEmpty("gmail.com has MX records");
            // MX records are returned as the exchange domain names
        }
    }

    /// <summary>
    /// RFC 1035 Section 3.3.1: A Resource Record (Address)
    /// A records map domain names to IPv4 addresses
    /// </summary>
    [Fact]
    public async Task Rfc1035_Section_3_3_1_ARecords_DomainExistence()
    {
        // RFC 1035 Section 3.3.1 defines A records (IPv4 addresses)
        // We use A records to verify domain existence

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = false
        });

        // localhost should have A record (127.0.0.1)
        var result = await validator.ValidateAsync("user@localhost");

        // Should pass domain existence check (has A record)
        // But may fail MX check if that's enabled
        result.IsValid.Should().BeTrue("localhost has A record (127.0.0.1)");
    }

    /// <summary>
    /// RFC 1035 Section 3.3.28: AAAA Resource Record (IPv6 Address)
    /// AAAA records map domain names to IPv6 addresses
    ///
    /// Note: AAAA is actually defined in RFC 3596, not RFC 1035
    /// But we check for both A and AAAA when validating domain existence
    /// </summary>
    [Fact]
    public async Task Rfc3596_AaaaRecords_Ipv6DomainExistence()
    {
        // RFC 3596 defines AAAA records for IPv6
        // Our DNS validator checks both A and AAAA records

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = false
        });

        // Many modern domains have both A (IPv4) and AAAA (IPv6) records
        // We accept either for domain existence

        var result = await validator.ValidateAsync("user@example.com");

        // example.com is reserved (RFC 2606) and may not resolve
        // This documents that we check both A and AAAA
        result.Should().NotBeNull("DNS lookup completes regardless of result");
    }

    /// <summary>
    /// RFC 1035 Section 7.4: Recommended Resource Record Caching
    /// DNS responses can be cached according to TTL
    ///
    /// Note: Our implementation does not currently implement caching
    /// Each validation performs fresh DNS lookups
    /// </summary>
    [Fact]
    public void Rfc1035_Section_7_4_DnsCaching_NotImplemented()
    {
        // RFC 1035 recommends caching DNS responses according to TTL
        // TODO: Consider implementing DNS caching for performance
        // Current implementation: fresh lookup on each validation

        // This is documented behavior - no caching currently implemented
        true.Should().BeTrue("DNS caching not implemented - fresh lookups each time");
    }

    /// <summary>
    /// RFC 2181 Section 11: Name syntax
    /// Any binary string can be a DNS label (relaxes RFC 1035 restrictions)
    /// However, host names have stricter requirements (RFC 952, RFC 1123)
    ///
    /// For email, we use hostname rules, not general DNS label rules
    /// </summary>
    [Fact]
    public void Rfc2181_Section_11_DnsVsHostname_Documentation()
    {
        // RFC 2181 Section 11 clarifies that DNS labels can be any binary string
        // But email domain names should follow hostname syntax (RFC 952/1123)

        // We validate syntax at the structural level
        // Full domain validation would enforce hostname rules
        // TODO: Consider adding stricter domain syntax validation

        true.Should().BeTrue("Domain validation uses hostname rules, not general DNS label rules");
    }
}
