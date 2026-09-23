using EmailValidation.Core;
using EmailValidation.Core.Validators;
using FluentAssertions;

namespace EmailValidation.Tests;

/// <summary>
/// Regression tests derived from real, reported failures in comparable tools
/// (umuterturk/email-verifier, reacherhq/backend), and coverage for the feature
/// gaps identified against them (disposable-domain and role-based detection).
/// </summary>
public class CompetitorGapTests
{
    /// <summary>
    /// Regression for umuterturk/email-verifier issue #3: "J@musetheagency.com"
    /// was rejected with syntax=false/domain_exists=false/mx_records=false despite
    /// being a real, deliverable address. Neither RFC 5321 nor RFC 5322 impose a
    /// minimum length on the local-part.
    /// https://github.com/umuterturk/email-verifier/issues/3
    /// </summary>
    [Fact]
    public void LocalPartValidator_SingleCharacterLocalPart_IsValid()
    {
        var result = LocalPartValidator.Validate("j");

        result.IsValid.Should().BeTrue(
            "RFC 5321/5322 impose no minimum length on the local-part - a single character is valid");
    }

    /// <summary>
    /// End-to-end version of the above: a single-character local-part must survive
    /// the full pipeline, not just the local-part layer in isolation.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_SingleCharacterLocalPart_ReturnsValid()
    {
        var dns = new CountingDnsValidator();
        var validator = new EmailValidator(new EmailValidatorOptions(), dns);

        var result = await validator.ValidateAsync("j@example.com");

        result.IsValid.Should().BeTrue();
        result.LocalPart.Should().Be("j");
    }

    /// <summary>
    /// Regression for umuterturk/email-verifier issues #12 and #14: both report
    /// domain_exists=false while mx_records=true (and mailbox_exists=true) for
    /// real, mail-capable domains - an internally inconsistent result caused by
    /// treating "has an A/AAAA record" as a precondition for "domain exists".
    ///
    /// A domain can legitimately have MX records with no A/AAAA record at the
    /// apex (a mail-only domain, e.g. one that only serves a website at
    /// www.example.com but not example.com itself). RFC 5321 Section 5 makes MX
    /// the authoritative signal for mail acceptance - A/AAAA existence at the
    /// apex is not a precondition for it.
    ///
    /// https://github.com/umuterturk/email-verifier/issues/12
    /// https://github.com/umuterturk/email-verifier/issues/14
    /// </summary>
    [Fact]
    public async Task ValidateAsync_DomainHasMxButNoAAAARecord_IsStillValid()
    {
        var dns = new CountingDnsValidator(
            domainExists: _ => ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                "Domain 'mail-only.test' has no A or AAAA records"),
            mxRecords: _ => ValidationResult.Success("mail-only.test", string.Empty, "mail-only.test", ["mail.mail-only.test"]));

        var validator = new EmailValidator(new EmailValidatorOptions(), dns);

        var result = await validator.ValidateAsync("user@mail-only.test");

        result.IsValid.Should().BeTrue(
            "a domain with MX records can accept mail even without an A/AAAA record at the apex - " +
            "MX is the authoritative signal, not A/AAAA");
        result.MxRecords.Should().Contain("mail.mail-only.test");
    }

    /// <summary>
    /// Deferring a domain-does-not-exist failure to MX (above) must never apply
    /// to the SSRF-protection case. A domain resolving to an internal/private
    /// address is a hard block regardless of what the MX check says.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_DomainResolvesInternally_IsBlockedEvenIfMxSucceeds()
    {
        var dns = new CountingDnsValidator(
            domainExists: _ => ValidationResult.Failure(
                ValidationFailureReason.InternalAddressBlocked,
                "Domain 'evil.test' resolves to internal/private address"),
            mxRecords: _ => ValidationResult.Success("evil.test", string.Empty, "evil.test", ["mail.evil.test"]));

        var validator = new EmailValidator(new EmailValidatorOptions(), dns);

        var result = await validator.ValidateAsync("user@evil.test");

        result.IsValid.Should().BeFalse(
            "SSRF protection must never be overridden by a successful MX check");
        result.FailureReason.Should().Be(ValidationFailureReason.InternalAddressBlocked);
    }

    /// <summary>
    /// A domain with neither A/AAAA nor MX records genuinely doesn't accept mail -
    /// deferring to MX doesn't mean deferring forever, it must still fail overall.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_NoAAAARecordAndNoMxRecords_IsInvalid()
    {
        var dns = new CountingDnsValidator(
            domainExists: _ => ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                "Domain 'nowhere.test' has no A or AAAA records"),
            mxRecords: _ => ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotAcceptMail,
                "Domain 'nowhere.test' has no MX records - cannot accept mail"));

        var validator = new EmailValidator(new EmailValidatorOptions(), dns);

        var result = await validator.ValidateAsync("user@nowhere.test");

        result.IsValid.Should().BeFalse("a domain with neither A/AAAA nor MX records cannot accept mail");
        result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotAcceptMail);
    }

    [Theory]
    [InlineData("mailinator.com")]
    [InlineData("guerrillamail.com")]
    [InlineData("yopmail.com")]
    [InlineData("10minutemail.com")]
    public void DisposableDomainChecker_KnownProviders_AreFlagged(string domain)
    {
        DisposableDomainChecker.IsDisposable(domain).Should().BeTrue();
    }

    [Fact]
    public void DisposableDomainChecker_UnknownDomain_IsNotFlagged()
    {
        DisposableDomainChecker.IsDisposable("gmail.com").Should().BeFalse();
    }

    [Fact]
    public void DisposableDomainChecker_AdditionalDomains_AreFlagged()
    {
        var additional = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "example-disposable.test" };

        DisposableDomainChecker.IsDisposable("example-disposable.test", additional).Should().BeTrue();
        DisposableDomainChecker.IsDisposable("gmail.com", additional).Should().BeFalse();
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("support")]
    [InlineData("noreply")]
    [InlineData("postmaster")]
    public void RoleBasedLocalPartChecker_KnownRoleAddresses_AreFlagged(string localPart)
    {
        RoleBasedLocalPartChecker.IsRoleBased(localPart).Should().BeTrue();
    }

    [Fact]
    public void RoleBasedLocalPartChecker_PersonalName_IsNotFlagged()
    {
        RoleBasedLocalPartChecker.IsRoleBased("jane.doe").Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_DisposableDomain_FlagsResultButRemainsValid()
    {
        var dns = new CountingDnsValidator();
        var validator = new EmailValidator(new EmailValidatorOptions(), dns);

        var result = await validator.ValidateAsync("user@mailinator.com");

        result.IsValid.Should().BeTrue("a disposable address is still a real, deliverable mailbox");
        result.IsDisposable.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_RoleBasedLocalPart_FlagsResultButRemainsValid()
    {
        var dns = new CountingDnsValidator();
        var validator = new EmailValidator(new EmailValidatorOptions(), dns);

        var result = await validator.ValidateAsync("support@example.com");

        result.IsValid.Should().BeTrue("a role-based address is still a real, deliverable mailbox");
        result.IsRoleBased.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBatchAsync_DisposableAndRoleBasedFlags_PropagatePerEmail()
    {
        var dns = new CountingDnsValidator();
        var validator = new EmailValidator(new EmailValidatorOptions(), dns);

        var results = await validator.ValidateBatchAsync(new[]
        {
            "jane.doe@example.com",
            "support@example.com",
            "user@mailinator.com"
        });

        results["jane.doe@example.com"].IsDisposable.Should().BeFalse();
        results["jane.doe@example.com"].IsRoleBased.Should().BeFalse();

        results["support@example.com"].IsRoleBased.Should().BeTrue();
        results["support@example.com"].IsDisposable.Should().BeFalse();

        results["user@mailinator.com"].IsDisposable.Should().BeTrue();
        results["user@mailinator.com"].IsRoleBased.Should().BeFalse();
    }
}
