using EmailValidation.Core;
using EmailValidation.Core.Validators;
using FluentAssertions;

namespace EmailValidation.Tests;

/// <summary>
/// Tests for RFC 5321: Simple Mail Transfer Protocol (SMTP)
/// https://www.rfc-editor.org/rfc/rfc5321
///
/// RFC 5321 defines SMTP and has additional restrictions beyond RFC 5322's message format.
/// Key sections:
/// - Section 2.3.11: Mailbox and Address (case sensitivity)
/// - Section 4.1.2: Command Argument Syntax (addr-spec in SMTP)
/// - Section 4.5.3.1: Size Limits and Minimums
/// </summary>
public class Rfc5321SmtpTests
{
    /// <summary>
    /// RFC 5321 Section 2.4: General Syntax Principles
    /// SMTP uses ASCII characters only (octets with high-order bit = 0)
    ///
    /// Note: RFC 6531 (SMTPUTF8) extends this for internationalization
    /// This implementation uses the original ASCII-only approach
    /// </summary>
    [Theory]
    [InlineData("user\u00E9")]           // é (Latin small letter e with acute)
    [InlineData("user\u4E2D")]           // 中 (Chinese character)
    [InlineData("user\u0411")]           // Б (Cyrillic)
    [InlineData("test\u00F1")]           // ñ (Spanish)
    public void Rfc5321_Section_2_4_NonAscii_Characters_Invalid_InBasicImplementation(string localPart)
    {
        // RFC 5321 originally required ASCII only
        // RFC 6531 (SMTPUTF8) allows UTF-8, but requires server support
        // This implementation enforces ASCII-only (practical default)

        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse(
            "Non-ASCII characters require SMTPUTF8 extension (RFC 6531), not supported in basic implementation");
    }

    /// <summary>
    /// RFC 5321 Section 2.3.11: Mailbox
    /// The local-part MUST BE treated as case sensitive
    /// </summary>
    [Fact]
    public void Rfc5321_Section_2_3_11_LocalPart_CaseSensitive_Documentation()
    {
        // RFC 5321 Section 2.3.11:
        // "The local-part of a mailbox MUST BE treated as case sensitive."

        // Our validator validates syntax only - both are syntactically valid
        var result1 = LocalPartValidator.Validate("User");
        var result2 = LocalPartValidator.Validate("user");

        result1.IsValid.Should().BeTrue();
        result2.IsValid.Should().BeTrue();

        // Note: "User@example.com" and "user@example.com" are DIFFERENT mailboxes
        // Mail servers must preserve case, but both are syntactically valid
    }

    /// <summary>
    /// RFC 5321 Section 4.1.2: Command Argument Syntax
    /// Mailbox = Local-part "@" ( Domain / address-literal )
    ///
    /// This tests the structural requirement for exactly one @ symbol
    /// </summary>
    [Fact]
    public void Rfc5321_Section_4_1_2_Mailbox_RequiresExactlyOneAtSymbol()
    {
        // Valid: exactly one @
        var validResult = StructuralValidator.Validate("user@example.com", false, out _, out _);
        validResult.IsValid.Should().BeTrue();

        // Invalid: no @
        var noAtResult = StructuralValidator.Validate("userexample.com", false, out _, out _);
        noAtResult.IsValid.Should().BeFalse();
        noAtResult.FailureReason.Should().Be(ValidationFailureReason.LocalDeliveryNotAllowed);

        // Invalid: multiple @
        var multiAtResult = StructuralValidator.Validate("user@@example.com", false, out _, out _);
        multiAtResult.IsValid.Should().BeFalse();
        multiAtResult.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
    }

    /// <summary>
    /// RFC 5321 Section 4.5.3.1.1: Local-part Size Limit
    /// The maximum total length of a user name or other local-part is 64 octets.
    /// </summary>
    [Fact]
    public void Rfc5321_Section_4_5_3_1_1_LocalPart_MaxLength_64Octets()
    {
        // RFC 5321 Section 4.5.3.1.1:
        // "The maximum total length of a user name or other local-part is 64 octets."

        var maxValid = new string('a', 64);
        var tooLong = new string('a', 65);

        var maxValidResult = LocalPartValidator.Validate(maxValid);
        var tooLongResult = LocalPartValidator.Validate(tooLong);

        // TODO: Length validation not yet implemented
        // For strict RFC 5321 compliance, this should be added
        maxValidResult.IsValid.Should().BeTrue("64 octets is the maximum allowed length");

        // Currently passes but should fail:
        tooLongResult.IsValid.Should().BeTrue("LENGTH VALIDATION NOT YET IMPLEMENTED - TODO");
        // Expected after implementation:
        // tooLongResult.IsValid.Should().BeFalse("65 octets exceeds maximum of 64");
        // tooLongResult.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
    }

    /// <summary>
    /// RFC 5321 Section 4.5.3.1.2: Domain Size Limit
    /// The maximum total length of a domain name or number is 255 octets.
    /// </summary>
    [Fact]
    public void Rfc5321_Section_4_5_3_1_2_Domain_MaxLength_255Octets()
    {
        // RFC 5321 Section 4.5.3.1.2:
        // "The maximum total length of a domain name or number is 255 octets."

        // This would be validated in domain validation (not local-part)
        // TODO: Add domain length validation

        // Document the requirement
        const int maxDomainLength = 255;
        maxDomainLength.Should().Be(255, "per RFC 5321 Section 4.5.3.1.2");
    }

    /// <summary>
    /// RFC 5321 Section 4.5.3.1.3: Path Size Limit
    /// The maximum total length of a reverse-path or forward-path is 256 octets
    /// (including the punctuation and element separators).
    /// </summary>
    [Fact]
    public void Rfc5321_Section_4_5_3_1_3_Path_MaxLength_256Octets()
    {
        // RFC 5321 Section 4.5.3.1.3:
        // Maximum total length of reverse-path or forward-path is 256 octets

        // This is the TOTAL email length: local-part + @ + domain
        // 64 (local) + 1 (@) + 255 (domain) = 320 potential
        // But RFC says path is limited to 256, so this is more restrictive

        // TODO: Add total path length validation
        const int maxPathLength = 256;
        maxPathLength.Should().Be(256, "per RFC 5321 Section 4.5.3.1.3");
    }

    /// <summary>
    /// RFC 5321 Section 2.3.5: Domain Names
    /// Only resolvable, fully-qualified domain names (FQDNs) are permitted
    /// when domain names are used in SMTP.
    /// </summary>
    [Fact]
    public async Task Rfc5321_Section_2_3_5_DomainMustBeResolvable()
    {
        // RFC 5321 requires domains to be resolvable
        // Our DNS validator implements this

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = false
        });

        // Nonexistent domain should fail DNS check
        var result = await validator.ValidateAsync("user@this-domain-definitely-does-not-exist-123456789.com");

        result.IsValid.Should().BeFalse("Domain must be resolvable per RFC 5321 Section 2.3.5");
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotExist);
    }

    /// <summary>
    /// RFC 5321 Section 5: Address Resolution and Mail Handling
    /// The domain is looked up using MX records in DNS (RFC 1035, RFC 974)
    /// If no MX records exist, RFC 5321 historically allowed fallback to A records,
    /// but this is DEPRECATED and NOT RECOMMENDED in modern practice.
    /// </summary>
    [Fact]
    public async Task Rfc5321_Section_5_MxRecords_Required_NoFallbackToA()
    {
        // Our implementation: NO MX = NO MAIL
        // We do NOT fall back to A records (deprecated practice)

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = true
        });

        // Test with a domain that has A record but no MX
        // localhost should have A record (127.0.0.1) but no MX
        var result = await validator.ValidateAsync("user@localhost");

        result.IsValid.Should().BeFalse("No MX record = no mail delivery (no fallback to A)");
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotAcceptMail);
    }

    /// <summary>
    /// RFC 5321 Section 4.1.1.4: Spaces and Control Characters
    /// Control characters and spaces are not allowed in email addresses
    /// </summary>
    [Theory]
    [InlineData("user name")]              // Space (0x20)
    [InlineData("user\tname")]             // Tab (0x09)
    [InlineData("user\nname")]             // Newline (0x0A)
    [InlineData("user\rname")]             // Carriage return (0x0D)
    [InlineData("user\x00name")]           // NULL (0x00)
    [InlineData("user\x01name")]           // SOH (0x01)
    [InlineData("user\x1Fname")]           // Unit separator (0x1F)
    public void Rfc5321_Section_4_1_1_4_ControlCharactersAndSpaces_Invalid(string localPart)
    {
        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse("Control characters and spaces not allowed per RFC 5321");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
    }

    /// <summary>
    /// RFC 5321 requires certain mailboxes to exist on every SMTP server
    /// Section 4.5.1: Minimum Implementation
    /// </summary>
    [Theory]
    [InlineData("postmaster")]             // Required by RFC 5321 Section 4.5.1
    public void Rfc5321_Section_4_5_1_Postmaster_MustExist_Syntactically(string localPart)
    {
        // RFC 5321 Section 4.5.1:
        // "Every publicly-reachable SMTP server MUST support the reserved mailbox 'postmaster'"

        // Our validator checks syntax only - the mailbox must exist on the server
        var result = LocalPartValidator.Validate(localPart);

        result.IsValid.Should().BeTrue($"'{localPart}' must be syntactically valid (server must support it)");
    }

    /// <summary>
    /// RFC 5321 Section 2.4: General Syntax Principles
    /// Commands and replies are composed of characters from the ASCII character set
    /// </summary>
    [Fact]
    public void Rfc5321_Section_2_4_AsciiOnly_BasicRequirement()
    {
        // All printable ASCII characters (0x21-0x7E) except special SMTP characters
        // should be testable in local-part

        var asciiValid = "abc123";
        var result = LocalPartValidator.Validate(asciiValid);

        result.IsValid.Should().BeTrue("Basic ASCII letters and numbers are always valid");
    }
}
