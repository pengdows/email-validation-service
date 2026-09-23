using EmailValidation.Core;
using EmailValidation.Core.Validators;
using FluentAssertions;

namespace EmailValidation.Tests;

public class DnsValidatorTests
{
    [Fact]
    public async Task ValidateDomainExistsAsync_RejectsPrivateIPAddresses()
    {
        // Act
        var result = await DnsValidator.ValidateDomainExistsAsync("localhost", new EmailValidatorOptions { AllowInternalDomains = false });

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InternalAddressBlocked);
        result.FailureMessage.Should().ContainAny("internal", "private", "loopback");
    }

    [Fact]
    public async Task ValidateDomainExistsAsync_TimedOut_ReturnsFailure()
    {
        // Act
        // Use a pre-canceled token to guarantee immediate "timeout" / cancellation
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        
        var result = await DnsValidator.ValidateDomainExistsAsync("example.com", new EmailValidatorOptions(), cts.Token);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotExist);
        result.FailureMessage.Should().ContainAny("timeout", "canceled");
    }

    [Fact]
    public async Task ValidateMxRecordsAsync_DetailedErrorMessages_True_IncludesDetails()
    {
        // Act - use an invalid domain that causes lookup failure
        var result = await DnsValidator.ValidateMxRecordsAsync("invalid.domain.that.does.not.exist.test", new EmailValidatorOptions { DetailedErrorMessages = true });

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotAcceptMail);
        // We aren't guaranteed an exact exception message, but it should contain "Detail: "
        result.FailureMessage.Should().Contain("Detail:");
    }

    [Fact]
    public async Task ValidateMxRecordsAsync_DetailedErrorMessages_False_HidesDetails()
    {
        // Act
        var result = await DnsValidator.ValidateMxRecordsAsync("invalid.domain.that.does.not.exist.test", new EmailValidatorOptions { DetailedErrorMessages = false });

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotAcceptMail);
        result.FailureMessage.Should().NotContain("Detail:");
    }
}
