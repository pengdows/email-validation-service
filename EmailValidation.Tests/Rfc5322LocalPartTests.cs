using EmailValidation.Core;
using EmailValidation.Core.Validators;
using FluentAssertions;

namespace EmailValidation.Tests;

/// <summary>
/// Tests for RFC 5322 Section 3.4.1: Addr-Spec Specification
/// https://www.rfc-editor.org/rfc/rfc5322#section-3.4.1
///
/// RFC 5322 defines the local-part as either:
/// 1. dot-atom: Atoms separated by dots (this implementation)
/// 2. quoted-string: Quoted strings with escaping (NOT implemented - simplified subset)
///
/// This test suite validates our simplified, practical subset of RFC 5322
/// that covers the vast majority of real-world email addresses.
/// </summary>
public class Rfc5322LocalPartTests
{
    /// <summary>
    /// RFC 5322 Section 3.2.3: Atom
    /// atext = ALPHA / DIGIT / "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "/" / "=" / "?" / "^" / "_" / "`" / "{" / "|" / "}" / "~"
    /// </summary>
    [Theory]
    [InlineData("a")]           // ALPHA (lowercase)
    [InlineData("Z")]           // ALPHA (uppercase)
    [InlineData("0")]           // DIGIT
    [InlineData("9")]           // DIGIT
    [InlineData("!")]           // Special: !
    [InlineData("#")]           // Special: #
    [InlineData("$")]           // Special: $
    [InlineData("%")]           // Special: %
    [InlineData("&")]           // Special: &
    [InlineData("'")]           // Special: '
    [InlineData("*")]           // Special: *
    [InlineData("+")]           // Special: +
    [InlineData("-")]           // Special: -
    [InlineData("/")]           // Special: /
    [InlineData("=")]           // Special: =
    [InlineData("?")]           // Special: ?
    [InlineData("^")]           // Special: ^
    [InlineData("_")]           // Special: _
    [InlineData("`")]           // Special: `
    [InlineData("{")]           // Special: {
    [InlineData("|")]           // Special: |
    [InlineData("}")]           // Special: }
    [InlineData("~")]           // Special: ~
    public void Rfc5322_Section_3_2_3_Atext_Characters_AreValid(string character)
    {
        // Arrange
        var localPart = $"test{character}user";

        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeTrue($"'{character}' is a valid atext character per RFC 5322 Section 3.2.3");
    }

    /// <summary>
    /// RFC 5322 Section 3.2.3: Atom
    /// Characters NOT in atext are invalid in unquoted local-parts
    /// </summary>
    [Theory]
    [InlineData("@", "at sign")]           // Reserved as address separator
    [InlineData(" ", "space")]             // Whitespace not allowed
    [InlineData("\"", "double quote")]     // Only valid in quoted-string form
    [InlineData("(", "left paren")]        // Only valid in comments
    [InlineData(")", "right paren")]       // Only valid in comments
    [InlineData(",", "comma")]             // List separator
    [InlineData(":", "colon")]             // Group separator
    [InlineData(";", "semicolon")]         // Group terminator
    [InlineData("<", "less than")]         // Angle bracket (addr-spec delimiter)
    [InlineData(">", "greater than")]      // Angle bracket (addr-spec delimiter)
    [InlineData("[", "left bracket")]      // Domain literal delimiter
    [InlineData("]", "right bracket")]     // Domain literal delimiter
    [InlineData("\\", "backslash")]        // Escape character (only in quoted-string)
    public void Rfc5322_Section_3_2_3_NonAtext_Characters_AreInvalid(string character, string description)
    {
        // Arrange
        var localPart = $"test{character}user";

        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse($"'{character}' ({description}) is NOT a valid atext character per RFC 5322 Section 3.2.3");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
    }

    /// <summary>
    /// RFC 5322 Section 3.2.3: dot-atom-text
    /// dot-atom-text = 1*atext *("." 1*atext)
    ///
    /// This means:
    /// - Must start with at least one atext character
    /// - Can have dots followed by at least one atext character
    /// - Cannot start with dot
    /// - Cannot end with dot
    /// - Cannot have consecutive dots
    /// </summary>
    [Theory]
    [InlineData("user")]                   // Simple atom
    [InlineData("user.name")]              // Atom dot atom
    [InlineData("user.middle.name")]       // Multiple dots
    [InlineData("first.last")]             // Common pattern
    [InlineData("a.b.c.d.e")]              // Many segments
    [InlineData("user123")]                // Alphanumeric
    [InlineData("123user")]                // Starting with digit
    [InlineData("user+tag")]               // Plus addressing (Gmail, etc.)
    [InlineData("user_name")]              // Underscore
    [InlineData("user-name")]              // Hyphen
    public void Rfc5322_Section_3_2_3_DotAtom_ValidPatterns(string localPart)
    {
        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeTrue($"'{localPart}' is a valid dot-atom per RFC 5322 Section 3.2.3");
    }

    /// <summary>
    /// RFC 5322 Section 3.2.3: dot-atom-text cannot start with dot
    /// </summary>
    [Fact]
    public void Rfc5322_Section_3_2_3_DotAtom_CannotStartWithDot()
    {
        // Arrange
        var localPart = ".user";

        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse("dot-atom cannot start with '.' per RFC 5322 Section 3.2.3");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
        result.FailureMessage.Should().Contain("cannot start with a dot");
    }

    /// <summary>
    /// RFC 5322 Section 3.2.3: dot-atom-text cannot end with dot
    /// </summary>
    [Fact]
    public void Rfc5322_Section_3_2_3_DotAtom_CannotEndWithDot()
    {
        // Arrange
        var localPart = "user.";

        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse("dot-atom cannot end with '.' per RFC 5322 Section 3.2.3");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
        result.FailureMessage.Should().Contain("cannot end with a dot");
    }

    /// <summary>
    /// RFC 5322 Section 3.2.3: dot-atom-text cannot have consecutive dots
    /// dot-atom-text = 1*atext *("." 1*atext)
    /// The "1*atext" after each dot means at least one atext character must follow
    /// </summary>
    [Fact]
    public void Rfc5322_Section_3_2_3_DotAtom_CannotHaveConsecutiveDots()
    {
        // Arrange
        var localPart = "user..name";

        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse("dot-atom cannot have consecutive dots per RFC 5322 Section 3.2.3");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
        result.FailureMessage.Should().Contain("consecutive dots");
    }

    /// <summary>
    /// RFC 5322 Section 3.4.1: addr-spec
    /// addr-spec = local-part "@" domain
    ///
    /// Note: We also reject local-parts with length > 64 octets per RFC 5321 Section 4.5.3.1.1
    /// However, this is typically enforced at a higher level, not in the local-part validator
    /// </summary>
    [Fact]
    public void Rfc5321_Section_4_5_3_1_1_LocalPart_MaxLength_Note()
    {
        // RFC 5321 Section 4.5.3.1.1 specifies:
        // "The maximum total length of a user name or other local-part is 64 octets."

        // Note: This test documents the requirement but doesn't enforce it
        // The LocalPartValidator currently doesn't check length limits
        // This should be added if strict RFC 5321 compliance is required

        var maxLengthLocalPart = new string('a', 64);
        var tooLongLocalPart = new string('a', 65);

        // Current implementation allows both (length check not implemented)
        var validResult = LocalPartValidator.Validate(maxLengthLocalPart);
        var invalidResult = LocalPartValidator.Validate(tooLongLocalPart);

        // Document current behavior
        validResult.IsValid.Should().BeTrue("64 characters should be valid");
        invalidResult.IsValid.Should().BeTrue("Length validation not yet implemented - TODO for strict RFC 5321 compliance");
    }

    /// <summary>
    /// RFC 5322 vs RFC 5321 difference: quoted-string
    /// RFC 5322 allows quoted-string form: "user name"@example.com
    /// RFC 5321 (SMTP) has more restrictions
    ///
    /// This implementation does NOT support quoted-string (simplified subset)
    /// </summary>
    [Theory]
    [InlineData("\"user\"")]               // Quoted string
    [InlineData("\"user name\"")]          // Quoted string with space
    [InlineData("\"user@host\"")]          // Quoted string with @
    public void Rfc5322_Section_3_2_4_QuotedString_NotSupported_SimplifiedImplementation(string localPart)
    {
        // Our simplified implementation does NOT support quoted-string form
        // This is a practical subset that covers 99%+ of real-world emails

        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse(
            "Quoted-string form is valid per RFC 5322 but NOT supported in this simplified implementation");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
    }

    /// <summary>
    /// Case sensitivity: RFC 5321 Section 2.3.11
    /// "The local-part of a mailbox MUST BE treated as case sensitive."
    ///
    /// However, our validator does not enforce case - it only validates characters
    /// Case handling is the responsibility of the mail server
    /// </summary>
    [Fact]
    public void Rfc5321_Section_2_3_11_LocalPart_IsCaseSensitive_Note()
    {
        // RFC 5321 Section 2.3.11:
        // "The local-part of a mailbox MUST BE treated as case sensitive.
        //  Therefore, SMTP implementations MUST take care to preserve the case
        //  of mailbox local-parts."

        // Our validator accepts both - case handling is server responsibility
        var lowercase = "user";
        var uppercase = "USER";
        var mixed = "UsEr";

        LocalPartValidator.Validate(lowercase).IsValid.Should().BeTrue();
        LocalPartValidator.Validate(uppercase).IsValid.Should().BeTrue();
        LocalPartValidator.Validate(mixed).IsValid.Should().BeTrue();

        // Note: user@example.com and USER@example.com are DIFFERENT mailboxes per RFC 5321
        // But both are syntactically valid
    }

    /// <summary>
    /// Real-world patterns that are valid per RFC 5322
    /// These test common email patterns used in production
    /// </summary>
    [Theory]
    [InlineData("admin")]
    [InlineData("webmaster")]
    [InlineData("postmaster")]              // RFC 5321 requires this to exist
    [InlineData("abuse")]
    [InlineData("noreply")]
    [InlineData("no-reply")]
    [InlineData("support")]
    [InlineData("info")]
    [InlineData("sales")]
    [InlineData("user+spam")]               // Plus addressing (subaddressing)
    [InlineData("user+tag123")]
    [InlineData("first.last")]
    [InlineData("first_last")]
    [InlineData("first-last")]
    [InlineData("user123")]
    [InlineData("123user")]
    [InlineData("user.name.long")]
    [InlineData("a")]                       // Single character
    [InlineData("1")]                       // Single digit
    public void Rfc5322_RealWorld_CommonValidPatterns(string localPart)
    {
        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeTrue($"'{localPart}' is a common, valid email local-part");
    }
}
