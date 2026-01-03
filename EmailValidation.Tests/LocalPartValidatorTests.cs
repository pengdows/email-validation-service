using EmailValidation.Core;
using EmailValidation.Core.Validators;
using FluentAssertions;

namespace EmailValidation.Tests;

public class LocalPartValidatorTests
{
    [Fact]
    public void Validate_StartsWithDot_ReturnsInvalidLocalPart()
    {
        // Arrange
        var localPart = ".user";

        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
        result.FailureMessage.Should().Contain("cannot start with a dot");
    }

    [Fact]
    public void Validate_EndsWithDot_ReturnsInvalidLocalPart()
    {
        // Arrange
        var localPart = "user.";

        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
        result.FailureMessage.Should().Contain("cannot end with a dot");
    }

    [Fact]
    public void Validate_ConsecutiveDots_ReturnsInvalidLocalPart()
    {
        // Arrange
        var localPart = "user..name";

        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
        result.FailureMessage.Should().Contain("consecutive dots");
    }

    [Theory]
    [InlineData("user")]
    [InlineData("user.name")]
    [InlineData("user+test")]
    [InlineData("user_test")]
    [InlineData("user-test")]
    [InlineData("user123")]
    [InlineData("u.s.e.r")]  // Multiple dots but valid placement
    [InlineData("user!test")]
    [InlineData("user#test")]
    [InlineData("user$test")]
    [InlineData("user%test")]
    [InlineData("user&test")]
    [InlineData("user'test")]
    [InlineData("user*test")]
    [InlineData("user/test")]
    [InlineData("user=test")]
    [InlineData("user?test")]
    [InlineData("user^test")]
    [InlineData("user`test")]
    [InlineData("user{test")]
    [InlineData("user|test")]
    [InlineData("user}test")]
    [InlineData("user~test")]
    public void Validate_ValidLocalPart_ReturnsSuccess(string localPart)
    {
        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeTrue($"'{localPart}' should be valid");
    }

    [Theory]
    [InlineData("user@test")]    // @ not allowed in local part
    [InlineData("user test")]    // space not allowed
    [InlineData("user,test")]    // comma not allowed
    [InlineData("user;test")]    // semicolon not allowed
    [InlineData("user:test")]    // colon not allowed
    [InlineData("user<test")]    // angle bracket not allowed
    [InlineData("user>test")]    // angle bracket not allowed
    [InlineData("user[test")]    // square bracket not allowed
    [InlineData("user]test")]    // square bracket not allowed
    [InlineData("user(test")]    // parenthesis not allowed
    [InlineData("user)test")]    // parenthesis not allowed
    [InlineData("user\\test")]   // backslash not allowed (in simplified RFC)
    [InlineData("user\"test")]   // quote not allowed (in simplified RFC)
    public void Validate_InvalidCharacter_ReturnsInvalidLocalPart(string localPart)
    {
        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse($"'{localPart}' should be invalid");
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
        result.FailureMessage.Should().Contain("invalid character");
    }

    [Fact]
    public void Validate_EmptyLocalPart_ReturnsInvalidLocalPart()
    {
        // Act
        var result = LocalPartValidator.Validate("");

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
    }

    // Test for Gmail-style dot normalization note
    // The validator should still reject invalid dot placement
    // even though Gmail normalizes dots in routing
    [Theory]
    [InlineData(".user")]      // Invalid - starts with dot
    [InlineData("user.")]      // Invalid - ends with dot
    [InlineData("user..name")] // Invalid - consecutive dots
    public void Validate_InvalidDotPlacement_StillInvalid_EvenThoughGmailNormalizesDots(string localPart)
    {
        // These are invalid per RFC even though Gmail might normalize dots
        // Act
        var result = LocalPartValidator.Validate(localPart);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
        result.FailureMessage.Should().Contain("dot");
    }

    [Fact]
    public void GetNormalizationNote_ReturnsNote()
    {
        // Act
        var note = LocalPartValidator.GetNormalizationNote();

        // Assert
        note.Should().Contain("Gmail");
        note.Should().Contain("RFC");
    }
}
