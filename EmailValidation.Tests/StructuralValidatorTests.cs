using EmailValidation.Core;
using EmailValidation.Core.Validators;
using FluentAssertions;

namespace EmailValidation.Tests;

public class StructuralValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void Validate_EmptyOrWhitespace_ReturnsInvalidFormat(string email)
    {
        // Act
        var result = StructuralValidator.Validate(email, allowLocalDelivery: false, out _, out _);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
    }

    [Fact]
    public void Validate_ContainsWhitespace_ReturnsInvalidFormat()
    {
        // Act
        var result = StructuralValidator.Validate("user @example.com", allowLocalDelivery: false, out _, out _);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
        result.FailureMessage.Should().Contain("whitespace");
    }

    [Fact]
    public void Validate_ContainsControlCharacters_ReturnsInvalidFormat()
    {
        // Act - using ASCII character 0x01 (SOH - Start of Heading), a non-whitespace control char
        var result = StructuralValidator.Validate("user\x01@example.com", allowLocalDelivery: false, out _, out _);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
        result.FailureMessage.Should().Contain("control characters");
    }

    [Fact]
    public void Validate_NoAtSign_LocalDeliveryNotAllowed_ReturnsLocalDeliveryNotAllowed()
    {
        // Act
        var result = StructuralValidator.Validate("root", allowLocalDelivery: false, out _, out _);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.LocalDeliveryNotAllowed);
    }

    [Fact]
    public void Validate_NoAtSign_LocalDeliveryAllowed_ReturnsSuccess()
    {
        // Act
        var result = StructuralValidator.Validate("root", allowLocalDelivery: true, out var localPart, out var domain);

        // Assert
        result.IsValid.Should().BeTrue();
        localPart.Should().Be("root");
        domain.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Validate_MultipleAtSigns_ReturnsInvalidFormat()
    {
        // Act
        var result = StructuralValidator.Validate("user@@example.com", allowLocalDelivery: false, out _, out _);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
        result.FailureMessage.Should().Contain("exactly one @");
    }

    [Fact]
    public void Validate_EmptyLocalPart_ReturnsInvalidFormat()
    {
        // Act
        var result = StructuralValidator.Validate("@example.com", allowLocalDelivery: false, out _, out _);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
        result.FailureMessage.Should().Contain("Local part");
    }

    [Fact]
    public void Validate_EmptyDomain_ReturnsInvalidFormat()
    {
        // Act
        var result = StructuralValidator.Validate("user@", allowLocalDelivery: false, out _, out _);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
        result.FailureMessage.Should().Contain("Domain part");
    }

    [Fact]
    public void Validate_ValidEmail_ReturnsSuccess()
    {
        // Act
        var result = StructuralValidator.Validate("user@example.com", allowLocalDelivery: false, out var localPart, out var domain);

        // Assert
        result.IsValid.Should().BeTrue();
        localPart.Should().Be("user");
        domain.Should().Be("example.com");
    }

    [Fact]
    public void Validate_TrimsSurroundingWhitespace()
    {
        // Act
        var result = StructuralValidator.Validate("  user@example.com  ", allowLocalDelivery: false, out var localPart, out var domain);

        // Assert
        result.IsValid.Should().BeTrue();
        localPart.Should().Be("user");
        domain.Should().Be("example.com");
    }
}
