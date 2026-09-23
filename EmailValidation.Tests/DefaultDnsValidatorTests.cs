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
        var result = await validator.ValidateDomainExistsAsync(string.Empty, new EmailValidatorOptions());

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
        var result = await validator.ValidateMxRecordsAsync(string.Empty, new EmailValidatorOptions());

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotAcceptMail);
    }

    [Fact]
    public async Task ValidateDomainExistsAsync_CanceledToken_DoesNotLeakExceptionDetails()
    {
        // Arrange
        var validator = new DefaultDnsValidator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await validator.ValidateDomainExistsAsync("example.com", new EmailValidatorOptions(), cts.Token);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotExist);
        result.FailureMessage.Should().Be("DNS lookup canceled for domain 'example.com'");
    }

    [Fact]
    public async Task ValidateMxRecordsAsync_CanceledToken_DoesNotLeakExceptionDetails()
    {
        // Arrange
        var validator = new DefaultDnsValidator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await validator.ValidateMxRecordsAsync("example.com", new EmailValidatorOptions(), cts.Token);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotAcceptMail);
        result.FailureMessage.Should().Be("MX lookup canceled for domain 'example.com'");
    }
}
