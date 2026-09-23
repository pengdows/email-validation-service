using EmailValidation.Core;
using EmailValidation.Core.Validators;
using FluentAssertions;

namespace EmailValidation.Tests;

public class DomainSyntaxValidatorTests
{
    [Theory]
    [InlineData("example.com")]
    [InlineData("mail.example.com")]
    [InlineData("sub.domain.example.com")]
    [InlineData("example-domain.com")]
    [InlineData("example123.com")]
    [InlineData("123example.com")]
    [InlineData("localhost")]          // single label, no dot - a legitimate hostname
    public void Validate_WellFormedDomains_AreValid(string domain)
    {
        DomainSyntaxValidator.Validate(domain).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("-bad.com")]
    [InlineData("bad-.com")]
    [InlineData("exa..mple.com")]
    [InlineData(".example.com")]
    [InlineData("example.com.")]
    [InlineData("exa$mple.com")]
    [InlineData("exa mple.com")]
    public void Validate_MalformedDomains_AreInvalid(string domain)
    {
        DomainSyntaxValidator.Validate(domain).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_LabelOver63Characters_IsInvalid()
    {
        var label = new string('a', 64);

        DomainSyntaxValidator.Validate($"{label}.com").IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_LabelOf63Characters_IsValid()
    {
        var label = new string('a', 63);

        DomainSyntaxValidator.Validate($"{label}.com").IsValid.Should().BeTrue();
    }

    /// <summary>
    /// A malformed domain must be rejected at the syntax layer, before any DNS
    /// lookup is attempted - not just eventually fail once DNS is checked.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_MalformedDomain_FailsWithoutAnyDnsLookup()
    {
        var dns = new CountingDnsValidator();
        var validator = new EmailValidator(new EmailValidatorOptions(), dns);

        var result = await validator.ValidateAsync("user@-bad-.com");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
        dns.DomainExistsCalls.Should().BeEmpty();
        dns.MxCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateBatchAsync_MalformedDomain_FailsWithoutAnyDnsLookup()
    {
        var dns = new CountingDnsValidator();
        var validator = new EmailValidator(new EmailValidatorOptions(), dns);

        var results = await validator.ValidateBatchAsync(new[] { "user@-bad-.com" });

        results["user@-bad-.com"].IsValid.Should().BeFalse();
        results["user@-bad-.com"].FailureReason.Should().Be(ValidationFailureReason.InvalidFormat);
        dns.DomainExistsCalls.Should().BeEmpty();
        dns.MxCalls.Should().BeEmpty();
    }
}
