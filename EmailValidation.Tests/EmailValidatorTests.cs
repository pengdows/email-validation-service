using EmailValidation.Core;
using FluentAssertions;

namespace EmailValidation.Tests;

public class EmailValidatorTests
{
    [Fact]
    public async Task ValidateAsync_InvalidFormat_ReturnsInvalidFormat()
    {
        // Arrange
        var validator = new EmailValidator();

        // Act
        var result = await validator.ValidateAsync("invalid@@email.com");

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
    }

    [Fact]
    public async Task ValidateAsync_InvalidLocalPart_ReturnsInvalidLocalPart()
    {
        // Arrange
        var validator = new EmailValidator();

        // Act
        var result = await validator.ValidateAsync(".user@example.com");

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
    }

    [Fact]
    public async Task ValidateAsync_LocalDeliveryNotAllowed_ReturnsLocalDeliveryNotAllowed()
    {
        // Arrange
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            AllowLocalDelivery = false
        });

        // Act
        var result = await validator.ValidateAsync("root");

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.LocalDeliveryNotAllowed);
    }

    [Fact]
    public async Task ValidateAsync_LocalDeliveryAllowed_ReturnsSuccess()
    {
        // Arrange
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            AllowLocalDelivery = true,
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        // Act
        var result = await validator.ValidateAsync("root");

        // Assert
        result.IsValid.Should().BeTrue();
        result.LocalPart.Should().Be("root");
        result.Domain.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_DomainDoesNotExist_ReturnsDomainDoesNotExist()
    {
        // Arrange
        var dnsValidator = new CountingDnsValidator(
            domainExists: _ => ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                "Domain does not exist"));

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = false
        }, dnsValidator);

        // Act
        var result = await validator.ValidateAsync("user@nonexistent-domain-12345.com");

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotExist);
    }

    [Fact]
    public async Task ValidateAsync_DomainExistsButNoMx_ReturnsDomainDoesNotAcceptMail()
    {
        // Arrange
        var dnsValidator = new CountingDnsValidator(
            domainExists: _ => ValidationResult.Success("domain", string.Empty, "domain"),
            mxRecords: _ => ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotAcceptMail,
                "No MX records"));

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = true
        }, dnsValidator);

        // Act
        var result = await validator.ValidateAsync("user@localhost");

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotAcceptMail);
    }

    [Fact]
    public async Task ValidateBatchAsync_MultipleEmails_ReturnsResultsForAll()
    {
        // Arrange
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        var emails = new[]
        {
            "valid@example.com",
            "invalid@@example.com",
            ".invalid@example.com"
        };

        // Act
        var results = await validator.ValidateBatchAsync(emails);

        // Assert
        results.Should().HaveCount(3);
        results["valid@example.com"].IsValid.Should().BeTrue();
        results["invalid@@example.com"].IsValid.Should().BeFalse();
        results[".invalid@example.com"].IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NormalizesToLowercase()
    {
        // Arrange
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        // Act
        var result = await validator.ValidateAsync("UsEr@ExAmple.COM");

        // Assert
        result.IsValid.Should().BeTrue();
        result.NormalizedEmail.Should().Be("user@example.com");
        result.LocalPart.Should().Be("user");
        result.Domain.Should().Be("example.com");
    }

    [Fact]
    public async Task ValidateBatchAsync_DedupesCaseInsensitiveEmails()
    {
        // Arrange
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        var emails = new[]
        {
            "User@Example.com",
            "user@example.com"
        };

        // Act
        var results = await validator.ValidateBatchAsync(emails);

        // Assert
        results.Should().HaveCount(1);
        results.ContainsKey("user@example.com").Should().BeTrue();
        results["user@example.com"].IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBatchAsync_DomainCheckRunsBeforeLocalPartValidation()
    {
        // Arrange
        var dnsValidator = new CountingDnsValidator(
            domainExists: _ => ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                "Domain does not exist"));

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = false
        }, dnsValidator);

        var emails = new[]
        {
            ".bad@nonexistent-domain-12345.com",
            "good@nonexistent-domain-12345.com"
        };

        // Act
        var results = await validator.ValidateBatchAsync(emails);

        // Assert
        results.Should().HaveCount(2);
        results[".bad@nonexistent-domain-12345.com"].FailureReason.Should()
            .Be(ValidationFailureReason.DomainDoesNotExist);
        results["good@nonexistent-domain-12345.com"].FailureReason.Should()
            .Be(ValidationFailureReason.DomainDoesNotExist);
    }

    [Fact]
    public async Task ValidateBatchAsync_DedupesInvalidEmails()
    {
        // Arrange
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        var emails = new[]
        {
            "invalid@@example.com",
            "invalid@@example.com"
        };

        // Act
        var results = await validator.ValidateBatchAsync(emails);

        // Assert
        results.Should().HaveCount(1);
        results.ContainsKey("invalid@@example.com").Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBatchAsync_MxFailureSetsResultsForDomain()
    {
        // Arrange
        var dnsValidator = new CountingDnsValidator(
            mxRecords: _ => ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotAcceptMail,
                "No MX records"));

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = true
        }, dnsValidator);

        var emails = new[]
        {
            "user1@example.com",
            "user2@example.com"
        };

        // Act
        var results = await validator.ValidateBatchAsync(emails);

        // Assert
        results.Should().HaveCount(2);
        results["user1@example.com"].FailureReason.Should()
            .Be(ValidationFailureReason.DomainDoesNotAcceptMail);
        results["user2@example.com"].FailureReason.Should()
            .Be(ValidationFailureReason.DomainDoesNotAcceptMail);
    }

    [Fact]
    public async Task ValidateBatchAsync_AllowsLocalDeliveryWhenEnabled()
    {
        // Arrange
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            AllowLocalDelivery = true,
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        var emails = new[]
        {
            "  RoOt  "
        };

        // Act
        var results = await validator.ValidateBatchAsync(emails);

        // Assert
        results.Should().HaveCount(1);
        results.ContainsKey("root").Should().BeTrue();
        results["root"].IsValid.Should().BeTrue();
        results["root"].NormalizedEmail.Should().Be("root");
        results["root"].LocalPart.Should().Be("root");
        results["root"].Domain.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateBatchAsync_TrimsAndLowercasesEmails()
    {
        // Arrange
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        var emails = new[]
        {
            "  UsEr@Example.COM  "
        };

        // Act
        var results = await validator.ValidateBatchAsync(emails);

        // Assert
        results.Should().HaveCount(1);
        results.ContainsKey("user@example.com").Should().BeTrue();
        results["user@example.com"].IsValid.Should().BeTrue();
        results["user@example.com"].NormalizedEmail.Should().Be("user@example.com");
        results["user@example.com"].LocalPart.Should().Be("user");
        results["user@example.com"].Domain.Should().Be("example.com");
    }

    [Fact]
    public async Task ValidateBatchAsync_MxEnabledDomain_ReturnsValid()
    {
        // Arrange
        var dnsValidator = new CountingDnsValidator(
            mxRecords: _ => ValidationResult.Success("domain", string.Empty, "domain", ["mx.example.com"]));

        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = true
        }, dnsValidator);

        var emails = new[]
        {
            "User@Gmail.com",
            "Other@Gmail.com"
        };

        // Act
        var results = await validator.ValidateBatchAsync(emails);

        // Assert
        results.Should().HaveCount(2);
        results["user@gmail.com"].IsValid.Should().BeTrue();
        results["user@gmail.com"].MxRecords.Should().NotBeNullOrEmpty();
        results["other@gmail.com"].IsValid.Should().BeTrue();
        results["other@gmail.com"].MxRecords.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateBatchAsync_CallsDnsOncePerDomain()
    {
        // Arrange
        var dnsValidator = new CountingDnsValidator();
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = true,
            CheckMxRecords = true
        }, dnsValidator);

        var emails = new[]
        {
            "user1@example.com",
            "user2@example.com",
            "user3@example.com"
        };

        // Act
        var results = await validator.ValidateBatchAsync(emails);

        // Assert
        results.Should().HaveCount(3);
        dnsValidator.DomainExistsCalls["example.com"].Should().Be(1);
        dnsValidator.MxCalls["example.com"].Should().Be(1);
    }

    [Fact]
    public async Task ValidateBatchAsync_LocalOnlySkipsDnsChecks()
    {
        // Arrange
        var dnsValidator = new CountingDnsValidator();
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            AllowLocalDelivery = true,
            CheckDomainExists = true,
            CheckMxRecords = true
        }, dnsValidator);

        var emails = new[]
        {
            "root",
            "postmaster"
        };

        // Act
        var results = await validator.ValidateBatchAsync(emails);

        // Assert
        results.Should().HaveCount(2);
        dnsValidator.DomainExistsCalls.Should().BeEmpty();
        dnsValidator.MxCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("user.name@example.com")]
    [InlineData("user+tag@example.com")]
    [InlineData("user_test@example.com")]
    [InlineData("user-test@example.com")]
    [InlineData("123@example.com")]
    [InlineData("user123@example123.com")]
    public async Task ValidateAsync_ValidFormat_WithoutDnsChecks_ReturnsSuccess(string email)
    {
        // Arrange
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        // Act
        var result = await validator.ValidateAsync(email);

        // Assert
        result.IsValid.Should().BeTrue($"'{email}' should be valid");
        result.NormalizedEmail.Should().Be(email.ToLowerInvariant());
    }

    [Fact]
    public async Task ValidateAsync_SkipsDnsChecksWhenDisabled()
    {
        // Arrange
        var validator = new EmailValidator(new EmailValidatorOptions
        {
            CheckDomainExists = false,
            CheckMxRecords = false
        });

        // Act - nonexistent domain should still pass without DNS checks
        var result = await validator.ValidateAsync("user@totally-fake-domain-99999.com");

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
