using EmailValidation.Core;
using FluentAssertions;

namespace EmailValidation.Tests;

public class DefaultDnsValidatorTests
{
    [Fact]
    public async Task ValidateDomainExistsAsync_EmptyDomain_ReturnsFailure()
    {
        // Arrange
        var validator = new DefaultDnsValidator();

        // Act
        var result = await validator.ValidateDomainExistsAsync(string.Empty);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotExist);
    }

    [Fact]
    public async Task ValidateMxRecordsAsync_EmptyDomain_ReturnsFailure()
    {
        // Arrange
        var validator = new DefaultDnsValidator();

        // Act
        var result = await validator.ValidateMxRecordsAsync(string.Empty);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotAcceptMail);
    }
}
