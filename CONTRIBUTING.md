# Contributing to Email Validation Service

Thank you for your interest in contributing! This is a **reference implementation** that demonstrates why regex-based email validation is wrong and shows the correct approach.

We welcome contributions that improve correctness, add RFC compliance, or enhance documentation.

---

## Code of Conduct

Be respectful, constructive, and professional. This is a technical project focused on correctness and education.

---

## Philosophy & Non-Negotiables

### 1. **NO REGEX** - Ever
This is the core principle of the project. We demonstrate email validation **without regex**.

❌ **Will be rejected:**
```csharp
if (Regex.IsMatch(email, @"^[a-z]+@[a-z]+\.[a-z]+$"))
```

✅ **Correct approach:**
```csharp
foreach (char c in localPart) {
    if (!IsAllowedCharacter(c)) return Invalid;
}
```

**Why:** Email syntax is not a regular language. Regex cannot validate it correctly.

### 2. **RFC Compliance First**
All validation logic must be traceable to specific RFC sections.

❌ **Will be rejected:**
- "I think emails can have..."
- "My email provider allows..."

✅ **Correct approach:**
- "RFC 5322 Section 3.2.3 states that atext includes..."
- "RFC 5321 Section 4.5.3.1.1 limits local-part to 64 octets..."

### 3. **MX-Only, No Fallback**
No MX record = no mail delivery. We do NOT fall back to A records.

❌ **Will be rejected:**
```csharp
if (!mxRecords.Any() && aRecords.Any()) return Valid;
```

✅ **Correct approach:**
```csharp
if (!mxRecords.Any()) return DomainDoesNotAcceptMail;
```

**Why:** RFC 974 fallback is deprecated. Modern practice requires MX records.

### 4. **Educational Focus**
This project teaches WHY regex fails and HOW to do it correctly.

Changes should include:
- Clear comments explaining RFC decisions
- Tests that reference specific RFC sections
- Documentation that educates, not just instructs

---

## How to Contribute

### Reporting Issues

**Before opening an issue:**
1. Check existing issues (open and closed)
2. Read [RFC-COMPLIANCE.md](RFC-COMPLIANCE.md) to understand what's implemented vs intentionally excluded
3. Search the test suite for related tests

**Good issue:**
```
Title: RFC 5321 Section 4.5.3.1.1 - Local-part length not validated

Description:
RFC 5321 Section 4.5.3.1.1 states: "The maximum total length of a
user name or other local-part is 64 octets."

Currently, the validator accepts local-parts longer than 64 octets.

Test case:
var localPart = new string('a', 65); // Should be invalid

Expected: ValidationFailureReason.InvalidLocalPart
Actual: Valid

RFC reference: https://www.rfc-editor.org/rfc/rfc5321#section-4.5.3.1.1
```

**Bad issue:**
```
Title: Email validation is broken

Description:
My email doesn't work.
```

### Submitting Code Changes

#### 1. Fork & Branch

```bash
# Fork the repository on GitHub, then:
git clone https://github.com/YOUR_USERNAME/email-validation-service
cd email-validation-service
git checkout -b feature/rfc-5321-length-validation
```

#### 2. Make Your Changes

**All code changes MUST include:**
- ✅ Implementation code
- ✅ Comprehensive tests
- ✅ RFC section references in comments
- ✅ Updated documentation (if applicable)

**Example commit:**
```csharp
// In LocalPartValidator.cs

/// <summary>
/// RFC 5321 Section 4.5.3.1.1: Maximum local-part length
/// "The maximum total length of a user name or other local-part is 64 octets."
/// </summary>
private const int MaxLocalPartLength = 64;

public static ValidationResult Validate(string localPart)
{
    // ... existing validation ...

    // RFC 5321 Section 4.5.3.1.1: Length check
    if (localPart.Length > MaxLocalPartLength)
    {
        return ValidationResult.Failure(
            ValidationFailureReason.InvalidLocalPart,
            $"Local part exceeds maximum length of {MaxLocalPartLength} octets (RFC 5321 Section 4.5.3.1.1)");
    }

    // ... rest of validation ...
}
```

**Example test:**
```csharp
[Fact]
public void Rfc5321_Section_4_5_3_1_1_LocalPart_MaxLength_64Octets()
{
    // RFC 5321 Section 4.5.3.1.1:
    // "The maximum total length of a user name or other local-part is 64 octets."

    var maxValid = new string('a', 64);
    var tooLong = new string('a', 65);

    var maxValidResult = LocalPartValidator.Validate(maxValid);
    var tooLongResult = LocalPartValidator.Validate(tooLong);

    maxValidResult.IsValid.Should().BeTrue("64 octets is the maximum allowed");
    tooLongResult.IsValid.Should().BeFalse("65 octets exceeds maximum of 64");
    tooLongResult.FailureReason.Should().Be(ValidationFailureReason.InvalidLocalPart);
}
```

#### 3. Run Tests

```bash
dotnet test
# All 223+ tests must pass
```

#### 4. Update Documentation

If your change affects:
- **Validation behavior** → Update RFC-COMPLIANCE.md
- **API endpoints** → Update README.md API section
- **Configuration** → Update CLAUDE.md

#### 5. Commit with Good Messages

```bash
git add .
git commit -m "Add RFC 5321 Section 4.5.3.1.1 local-part length validation

Implements maximum local-part length of 64 octets per RFC 5321 Section 4.5.3.1.1.

- Added MaxLocalPartLength constant
- Added length check in LocalPartValidator.Validate()
- Added Rfc5321_Section_4_5_3_1_1_LocalPart_MaxLength_64Octets test
- Updated RFC-COMPLIANCE.md with implementation status"
```

#### 6. Push & Create Pull Request

```bash
git push origin feature/rfc-5321-length-validation
```

On GitHub, create a pull request with:
- **Title:** Clear, descriptive (e.g., "Add RFC 5321 local-part length validation")
- **Description:**
  - What RFC section does this implement?
  - Why is this change needed?
  - How was it tested?
  - Link to RFC section

---

## Types of Contributions We Welcome

### ✅ High Priority

1. **RFC Compliance Improvements**
   - Implement missing RFC requirements (see RFC-COMPLIANCE.md TODOs)
   - Fix RFC violations
   - Add RFC-specific tests

2. **Documentation Improvements**
   - Clarify WHY regex fails
   - Add more RFC explanations
   - Improve educational content

3. **Test Coverage**
   - Add edge cases
   - Add more RFC-specific tests
   - Improve test documentation

4. **Bug Fixes**
   - Fix incorrect validation
   - Fix RFC compliance issues

### ⚠️ Medium Priority

5. **Performance Improvements**
   - Optimize DNS lookups (caching?)
   - Improve batch validation performance
   - Add benchmarks

6. **Production Readiness**
   - Replace dig/nslookup with DnsClient library
   - Add Docker support
   - Add Kubernetes deployment examples

### ❌ Will Be Rejected

- **Adding regex** for any reason
- **Relaxing RFC compliance** without justification
- **Adding A record fallback**
- **Removing RFC test annotations**
- **Generic "email validation" without RFC basis**

---

## Development Setup

### Prerequisites

- .NET 8.0 SDK or later
- Git
- (Optional) dig/nslookup for MX record lookups

### Clone & Build

```bash
git clone https://github.com/pengdows/email-validation-service
cd email-validation-service
dotnet build
dotnet test
```

### Project Structure

```
email-validation-service/
├── EmailValidation.Core/         # Core validation logic
│   ├── EmailValidator.cs         # Main orchestrator
│   ├── ValidationResult.cs       # Result types
│   └── Validators/
│       ├── StructuralValidator.cs
│       ├── LocalPartValidator.cs
│       └── DnsValidator.cs
├── EmailValidation.Api/          # HTTP REST API
│   └── Program.cs
├── EmailValidation.Tests/        # Test suite
│   ├── Rfc5322LocalPartTests.cs
│   ├── Rfc5321SmtpTests.cs
│   └── ...
└── Documentation/
    ├── README.md
    ├── CLAUDE.md
    ├── RFC-COMPLIANCE.md
    └── COMPARISON.md
```

---

## Testing Guidelines

### Test Naming Convention

```csharp
// Pattern: RfcXXXX_Section_X_X_X_FeatureName
[Fact]
public void Rfc5322_Section_3_2_3_Atext_Characters_AreValid()

// For edge cases
[Fact]
public void EdgeCase_EmptyLocalPart_Invalid()

// For real-world patterns
[Fact]
public void RealWorld_PlusAddressing_Valid()
```

### Test Structure

```csharp
[Fact]
public void Rfc5322_Section_X_Y_Z_TestName()
{
    // RFC comment explaining what we're testing
    // RFC 5322 Section X.Y.Z states: "..."

    // Arrange
    var input = "...";

    // Act
    var result = Validator.Validate(input);

    // Assert
    result.IsValid.Should().BeTrue("because RFC 5322 Section X.Y.Z allows...");
}
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test file
dotnet test --filter "FullyQualifiedName~Rfc5322LocalPartTests"

# Run with detailed output
dotnet test --verbosity normal
```

---

## Documentation Guidelines

### Comments

**Good:**
```csharp
// RFC 5322 Section 3.2.3: atext characters
// Allowed: A-Z, a-z, 0-9, and special characters
private static readonly HashSet<char> AllowedSpecialChars = new() { ... };
```

**Bad:**
```csharp
// Allowed characters
private static readonly HashSet<char> AllowedSpecialChars = new() { ... };
```

### Commit Messages

Format:
```
<type>: <short summary>

<detailed description>

- Bullet points for changes
- RFC references
- Test coverage notes
```

Types: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`

---

## Pull Request Process

1. **Fork and create a branch**
2. **Make changes with tests**
3. **Update documentation**
4. **Run all tests** (must pass)
5. **Push and create PR**
6. **Address review feedback**

**We will review for:**
- ✅ RFC compliance
- ✅ No regex usage
- ✅ Comprehensive tests
- ✅ Clear documentation
- ✅ Educational value

---

## Questions?

- **General questions:** Open a GitHub discussion
- **Bug reports:** Open an issue
- **RFC interpretation:** Reference specific RFC section in issue
- **Regex alternative:** Please read README.md first 😄

---

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

---

## Attribution

Contributors will be listed in a CONTRIBUTORS.md file (to be created).

---

**Thank you for helping build the reference implementation for correct email validation!**
