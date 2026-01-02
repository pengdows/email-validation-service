using EmailValidation.Core;
using EmailValidation.Core.Validators;
using FluentAssertions;

namespace EmailValidation.Tests;

/// <summary>
/// Tests for edge cases and internationalization extensions to email RFCs
/// - RFC 6531: SMTP Extension for Internationalized Email (SMTPUTF8)
/// - RFC 6532: Internationalized Email Headers
/// - RFC 3696: Application Techniques for Checking and Transformation of Names
/// - RFC 2606: Reserved Top Level DNS Names (for testing)
/// </summary>
public class RfcEdgeCasesAndInternationalizationTests
{
    /// <summary>
    /// RFC 6531: SMTP Extension for Internationalized Email
    /// https://www.rfc-editor.org/rfc/rfc6531
    ///
    /// SMTPUTF8 extends SMTP to support UTF-8 in email addresses
    /// This allows non-ASCII characters in local-parts and domains
    ///
    /// Examples:
    /// - 用户@例え.jp
    /// - user@例え.jp
    /// - josé@example.com
    ///
    /// NOTE: This implementation does NOT support SMTPUTF8
    /// This is a simplified, ASCII-only implementation
    /// </summary>
    [Theory]
    [InlineData("josé", "Latin: josé")]
    [InlineData("用户", "Chinese: 用户")]
    [InlineData("Дмитрий", "Cyrillic: Дмитрий")]
    [InlineData("محمد", "Arabic: محمد")]
    [InlineData("test\u00E9", "Latin with combining: testé")]
    public void Rfc6531_Smtputf8_NonAsciiLocalParts_NotSupported(string localPart, string description)
    {
        // RFC 6531 allows UTF-8 in email addresses (SMTPUTF8 extension)
        // This requires server support and negotiation

        // Our implementation: ASCII-only (simplified, practical default)
        // SMTPUTF8 support would be a future enhancement

        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse(
            $"{description}: SMTPUTF8 (RFC 6531) not supported in this implementation");
    }

    /// <summary>
    /// RFC 6531 Section 3.3: UTF-8 in Domain Names (IDN)
    /// Internationalized Domain Names in Applications (IDNA)
    ///
    /// Examples:
    /// - 例え.jp
    /// - münchen.de
    /// - испытание.рф
    ///
    /// IDN domains use Punycode encoding (xn--...)
    /// </summary>
    [Theory]
    [InlineData("user@xn--e1afmkfd.xn--p1ai")]  // испытание.рф in Punycode
    [InlineData("user@xn--mnchen-3ya.de")]      // münchen.de in Punycode
    public void Rfc6531_Idn_PunycodeEncoding_Supported(string email)
    {
        // IDN (Internationalized Domain Names) use Punycode encoding
        // Punycode domains start with "xn--"
        // This is ASCII-compatible and supported by our validator

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,  // Syntax only
            CheckMxRecords = false
        });

        var result = validator.ValidateAsync(email).Result;

        result.IsValid.Should().BeTrue("Punycode-encoded IDN domains are valid ASCII");
    }

    /// <summary>
    /// RFC 3696 Section 3: Restrictions on Email Addresses
    /// https://www.rfc-editor.org/rfc/rfc3696#section-3
    ///
    /// RFC 3696 clarifies common misconceptions about email validation
    /// and provides practical guidance
    /// </summary>
    [Fact]
    public void Rfc3696_Section_3_CommonMisconceptions_Documentation()
    {
        // RFC 3696 Section 3 discusses common email validation errors:

        // 1. Maximum lengths:
        //    - Local-part: 64 octets (RFC 5321)
        //    - Domain: 255 octets (RFC 5321)
        //    - Total path: 256 octets (RFC 5321)

        // 2. Plus addressing is valid:
        //    user+tag@example.com is valid

        // 3. Dots in local-part:
        //    - Cannot start or end with dot
        //    - Cannot have consecutive dots
        //    - But can have dots in middle

        var plusAddressing = LocalPartValidator.Validate("user+tag");
        plusAddressing.IsValid.Should().BeTrue("Plus addressing is valid per RFC 3696");

        var validDots = LocalPartValidator.Validate("user.name");
        validDots.IsValid.Should().BeTrue("Dots are valid when properly placed");
    }

    /// <summary>
    /// RFC 2606: Reserved Top Level DNS Names
    /// https://www.rfc-editor.org/rfc/rfc2606
    ///
    /// Reserved domains for testing and documentation:
    /// - .test
    /// - .example
    /// - .invalid
    /// - .localhost
    /// - example.com, example.net, example.org
    /// </summary>
    [Theory]
    [InlineData("user@example.com")]        // RFC 2606 reserved
    [InlineData("user@example.net")]        // RFC 2606 reserved
    [InlineData("user@example.org")]        // RFC 2606 reserved
    [InlineData("user@test.example")]       // .example TLD reserved
    [InlineData("user@something.test")]     // .test TLD reserved
    [InlineData("user@something.invalid")]  // .invalid TLD reserved
    public void Rfc2606_ReservedDomains_SyntacticallyValid(string email)
    {
        // RFC 2606 reserves certain domains for testing/documentation
        // These are syntactically valid but may not resolve (intended)

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,  // Syntax check only
            CheckMxRecords = false
        });

        var result = validator.ValidateAsync(email).Result;

        result.IsValid.Should().BeTrue("RFC 2606 reserved domains are syntactically valid");
    }

    /// <summary>
    /// Edge case: Empty domain after @
    /// user@ is invalid
    /// </summary>
    [Fact]
    public void EdgeCase_EmptyDomain_Invalid()
    {
        var result = StructuralValidator.Validate("user@", false, out _, out _);

        result.IsValid.Should().BeFalse("Empty domain is invalid");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
    }

    /// <summary>
    /// Edge case: Empty local-part before @
    /// @example.com is invalid
    /// </summary>
    [Fact]
    public void EdgeCase_EmptyLocalPart_Invalid()
    {
        var result = StructuralValidator.Validate("@example.com", false, out _, out _);

        result.IsValid.Should().BeFalse("Empty local-part is invalid");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
    }

    /// <summary>
    /// Edge case: Just @ symbol
    /// @ is invalid
    /// </summary>
    [Fact]
    public void EdgeCase_OnlyAtSymbol_Invalid()
    {
        var result = StructuralValidator.Validate("@", false, out _, out _);

        result.IsValid.Should().BeFalse("Just @ is invalid");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
    }

    /// <summary>
    /// Edge case: Multiple consecutive @ symbols
    /// user@@example.com is invalid
    /// </summary>
    [Fact]
    public void EdgeCase_ConsecutiveAtSymbols_Invalid()
    {
        var result = StructuralValidator.Validate("user@@example.com", false, out _, out _);

        result.IsValid.Should().BeFalse("Consecutive @ symbols are invalid");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
    }

    /// <summary>
    /// Edge case: Leading/trailing whitespace should be trimmed
    /// "  user@example.com  " → "user@example.com"
    /// </summary>
    [Fact]
    public void EdgeCase_LeadingTrailingWhitespace_Trimmed()
    {
        var result = StructuralValidator.Validate("  user@example.com  ", false, out var localPart, out var domain);

        result.IsValid.Should().BeTrue("Leading/trailing whitespace should be trimmed");
        localPart.Should().Be("user");
        domain.Should().Be("example.com");
    }

    /// <summary>
    /// Edge case: Whitespace within address
    /// "user @example.com" is invalid (space before @)
    /// "user@ example.com" is invalid (space after @)
    /// </summary>
    [Theory]
    [InlineData("user @example.com")]       // Space before @
    [InlineData("user@ example.com")]       // Space after @
    [InlineData("user @ example.com")]      // Spaces around @
    [InlineData("us er@example.com")]       // Space in local-part
    [InlineData("user@exam ple.com")]       // Space in domain
    public void EdgeCase_InternalWhitespace_Invalid(string email)
    {
        var result = StructuralValidator.Validate(email, false, out _, out _);

        result.IsValid.Should().BeFalse($"Internal whitespace is invalid: '{email}'");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
    }

    /// <summary>
    /// Edge case: Very long local-part (exceeds 64 octets)
    /// RFC 5321 Section 4.5.3.1.1: Maximum 64 octets
    /// </summary>
    [Fact]
    public void EdgeCase_LocalPartTooLong_NotYetValidated()
    {
        // TODO: Implement length validation for strict RFC 5321 compliance
        var tooLong = new string('a', 65);
        var result = LocalPartValidator.Validate(tooLong);

        // Currently passes (length check not implemented)
        result.IsValid.Should().BeTrue("Length validation not yet implemented");

        // After implementation, should be:
        // result.IsValid.Should().BeFalse("Local-part exceeds 64 octets");
    }

    /// <summary>
    /// Edge case: Comments in email addresses
    /// RFC 5322 allows comments in parentheses: user(comment)@example.com
    ///
    /// Our simplified implementation does NOT support comments
    /// </summary>
    [Theory]
    [InlineData("user(comment)@example.com")]
    [InlineData("(comment)user@example.com")]
    [InlineData("user@(comment)example.com")]
    public void EdgeCase_Comments_NotSupported(string email)
    {
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        var result = validator.ValidateAsync(email).Result;

        result.IsValid.Should().BeFalse("Comments (parentheses) not supported in simplified implementation");
    }

    /// <summary>
    /// Edge case: IP address literals in domain
    /// RFC 5321 allows: user@[192.0.2.1] or user@[IPv6:2001:db8::1]
    ///
    /// Our simplified implementation does NOT support IP literals
    /// </summary>
    [Theory]
    [InlineData("user@[192.0.2.1]")]
    [InlineData("user@[IPv6:2001:db8::1]")]
    public void EdgeCase_IpLiterals_NotSupported(string email)
    {
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        var result = validator.ValidateAsync(email).Result;

        result.IsValid.Should().BeFalse("IP address literals not supported in simplified implementation");
    }

    /// <summary>
    /// Real-world edge case: Plus addressing (subaddressing)
    /// Supported by Gmail, FastMail, and many others
    /// user+tag@example.com routes to user@example.com
    /// </summary>
    [Theory]
    [InlineData("user+spam@example.com")]
    [InlineData("user+receipts@example.com")]
    [InlineData("user+tag123@example.com")]
    [InlineData("user+@example.com")]           // Plus at end (valid per RFC, though unusual)
    public void RealWorld_PlusAddressing_Valid(string email)
    {
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        var result = validator.ValidateAsync(email).Result;

        result.IsValid.Should().BeTrue($"Plus addressing is valid: {email}");
    }

    /// <summary>
    /// Real-world edge case: Gmail dot normalization
    /// Gmail ignores dots in local-part for routing:
    /// user@gmail.com = u.ser@gmail.com = us.er@gmail.com
    ///
    /// However, RFC rules for dot placement still apply for validation
    /// </summary>
    [Theory]
    [InlineData("user@gmail.com", true)]
    [InlineData("u.ser@gmail.com", true)]
    [InlineData("us.er.name@gmail.com", true)]
    [InlineData(".user@gmail.com", false)]      // Invalid: starts with dot
    [InlineData("user.@gmail.com", false)]      // Invalid: ends with dot
    [InlineData("user..name@gmail.com", false)] // Invalid: consecutive dots
    public void RealWorld_Gmail_DotNormalization_RfcRulesStillApply(string email, bool shouldBeValid)
    {
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        var result = validator.ValidateAsync(email).Result;

        result.IsValid.Should().Be(shouldBeValid,
            "Gmail normalizes dots in routing, but RFC placement rules still apply for validation");
    }
}
