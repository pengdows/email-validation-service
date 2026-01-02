# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Email Validation Service** - A microservice that implements proper email validation as a **pipeline**, not a pattern match.

This service rejects the regex-based approach to email validation and instead implements a multi-layer validation pipeline that correctly validates email addresses based on:
- Structural correctness
- RFC compliance (simplified, practical subset)
- DNS existence
- Mail routing capability (MX records)

## Core Philosophy: Why NO REGEX

**Regex validation is structurally incapable of validating email addresses.**

### Why regex (and regex-based libraries) fail:

1. **Email syntax is not a regular language** - RFC 5321/5322 allow quoting, escaping, comments, and context-sensitive rules that regex cannot express without approximation
2. **Regex validates appearance, not meaning** - Cannot tell if domain exists, accepts mail, or drops messages
3. **False negatives break real users** - Valid but uncommon addresses get rejected
4. **False positives create security bugs** - "Validated" emails that can never receive mail
5. **Complex regexes are unmaintainable** - Large, unreadable, fragile, and still wrong
6. **Most email validation libraries are regex wrappers** - They inherit all the above failures

**Bottom line:** If a system uses regex anywhere in the email validation decision path, it is lying about correctness.

## Validation Pipeline

Email validation is a **decision tree**, not a pattern match.

### Layer 0: Policy Decision
- Allow local-only mailboxes (e.g., `root`, `postmaster`)? **Default: NO**
- Most modern systems require Internet email only

### Layer 1: Structural Split
- Exactly one `@`
- Non-empty local part
- Non-empty domain part
- No whitespace
- No control characters

This is **input sanitation**, not RFC validation.

### Layer 2: Local-Part Validation (Server-Agnostic)
**No regex.** Character-by-character validation following RFC rules:

Reject if:
- Starts with `.`
- Ends with `.`
- Contains `..` (consecutive dots)
- Contains invalid characters

Allow:
- Letters, digits
- `!#$%&'*+-/=?^_`{|}~`
- Dots (with placement constraints above)

**Critical note about dots:**
- Gmail ignores dots in routing (`user@gmail.com` = `u.ser@gmail.com`)
- BUT `.user@gmail.com` is still **INVALID**
- Dot placement rules still apply regardless of provider behavior

### Layer 3: DNS A/AAAA Lookup
Check if domain exists at all.
- No A/AAAA record = domain does not exist

### Layer 4: DNS MX Lookup (MANDATORY)
**This is the correct stopping point for most systems.**

> **If there is no MX record, you cannot send mail. Period.**

- No fallback to A records (historical, operationally unsafe)
- Modern, correct stance: **No MX = no mail delivery**

### Layer 5: SMTP Verification (Optional, NOT Implemented)
**Risky and unreliable:**
- Most servers disable or lie on `VRFY`
- Accept-all domains common
- Greylisting, tarpits, rate limits
- Can get your IP blocked

### Layer 6: Delivery Confirmation
**The only absolute validation.**
Everything before this is probabilistic.
- Send mail → User receives it → User acts on it (confirmation link)

## Common Commands

### Build
```bash
# Build entire solution
dotnet build

# Build specific project
dotnet build EmailValidation.Core/EmailValidation.Core.csproj
```

### Testing
```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --verbosity normal

# Run specific test class
dotnet test --filter "FullyQualifiedName~StructuralValidator"
```

### Running the API
```bash
# Run the API
dotnet run --project EmailValidation.Api/EmailValidation.Api.csproj

# The API will start on http://localhost:5000 (or configured port)
# Visit http://localhost:5000 for API documentation
# Visit http://localhost:5000/docs for detailed documentation
```

### API Endpoints
```bash
# Validate single email
curl -X POST http://localhost:5000/validate \
  -H "Content-Type: application/json" \
  -d '{"email":"user@example.com"}'

# Validate batch
curl -X POST http://localhost:5000/validate/batch \
  -H "Content-Type: application/json" \
  -d '{"emails":["user1@example.com","user2@example.com"]}'

# Health check
curl http://localhost:5000/health
```

## Project Structure

```
email-validation-service/
├── EmailValidation.sln          # Solution file
├── EmailValidation.Core/         # Core validation logic (NO REGEX)
│   ├── EmailValidator.cs        # Main orchestrator
│   ├── ValidationResult.cs      # Result types
│   ├── ValidationFailureReason.cs
│   └── Validators/
│       ├── StructuralValidator.cs   # Layer 1: Structural split
│       ├── LocalPartValidator.cs    # Layer 2: Local-part rules
│       └── DnsValidator.cs          # Layer 3 & 4: DNS checks
├── EmailValidation.Api/          # HTTP REST API
│   └── Program.cs               # Minimal API endpoints
└── EmailValidation.Tests/        # Comprehensive unit tests
    ├── StructuralValidatorTests.cs
    ├── LocalPartValidatorTests.cs
    └── EmailValidatorTests.cs
```

## Architecture

### Technology Stack
- **.NET 8.0** with C# nullable reference types enabled
- **ASP.NET Core Minimal APIs** for HTTP endpoints
- **xUnit** + **FluentAssertions** for testing
- **System.Net.Dns** for DNS A/AAAA lookups
- **Platform tools (dig/nslookup)** for MX lookups (temporary - see production notes)

### Validation Configuration

The validator accepts configuration via `EmailValidatorOptions`:

```csharp
var options = new EmailValidatorOptions
{
    AllowLocalDelivery = false,  // Internet mail only (recommended)
    CheckDomainExists = true,     // DNS A/AAAA check
    CheckMxRecords = true         // MX check (MANDATORY for production)
};
```

**Default configuration (production-ready):**
- Local delivery: **disabled**
- Domain existence check: **enabled**
- MX record check: **enabled**

### API Response Format

```json
{
  "email": "user@example.com",
  "isValid": true,
  "failureReason": null,
  "failureMessage": null,
  "normalizedEmail": "user@example.com",
  "localPart": "user",
  "domain": "example.com",
  "mxRecords": ["mail.example.com", "mail2.example.com"]
}
```

### Failure Reasons

| Reason | Description |
|--------|-------------|
| `InvalidFormat` | Failed structural split (no @, empty parts, whitespace, control chars) |
| `InvalidLocalPart` | Local part violates RFC rules (dot placement, invalid characters) |
| `DomainDoesNotExist` | No DNS A/AAAA records |
| `DomainDoesNotAcceptMail` | No MX records |
| `LocalDeliveryNotAllowed` | Policy violation (e.g., "root" without @domain) |

## Development Conventions

### Code Patterns
- **NO REGEX anywhere** - This is non-negotiable
- **Async/await** for all I/O operations (DNS lookups)
- **Nullable reference types** - Explicit null handling required
- **Character-by-character validation** for local-part checking

### Testing
- **Comprehensive coverage** - Each layer has dedicated tests
- **xUnit** framework with **FluentAssertions** for readable assertions
- Follow **Arrange-Act-Assert** pattern
- Test both valid and invalid cases
- Test edge cases (empty strings, null, whitespace, control chars)

### Critical Implementation Notes

**DNS MX Lookup - Production TODO:**
The current implementation uses platform tools (`dig` on Linux/macOS, `nslookup` on Windows) for MX record lookups. This is a temporary solution.

For production, replace with **DnsClient** NuGet package:
```bash
dotnet add package DnsClient
```

Then update `DnsValidator.GetMxRecordsAsync()` to use DnsClient for reliable, cross-platform MX lookups.

**Why no fallback to A records:**
- RFC historically allowed fallback to A record if no MX exists
- Modern practice: this is operationally unsafe and rarely supported
- Our stance: **No MX = no mail**, period

### Where Regexes and Libraries Fail (Explicit Failure Modes)

**Regex failures:**
- Reject valid addresses (quoted locals, plus-addressing, internationalized domains)
- Accept invalid ones (unroutable domains)
- Cannot detect routability or deliverability
- Encourage "validated == correct" thinking (false confidence)

**Library failures:**
- Regex under the hood
- Opinionated restrictions (e.g., "no plus signs")
- Poor error handling (throwing instead of returning invalid result)
- Outdated TLD assumptions
- Incorrect dot handling
- No DNS or MX checks
- Silent acceptance of undeliverable domains

Libraries often fail **harder** than raw regexes because they hide these flaws behind an API named "validate".

## Canonical Validation Model

Email validation is a **decision tree**:

1. ✅ Policy: local delivery allowed? (usually no)
2. ✅ Structural split (`@`)
3. ✅ Local-part rule validation (no regex)
4. ✅ DNS existence check
5. ✅ MX existence check (**MANDATORY**)
6. ⚠️ Optional SMTP probing (unreliable, not implemented)
7. 🎯 Confirmation email (authoritative, outside this service)

Anything less than layers 1-5 is filtering, not validation.

## Final Position (Unambiguous)

- **Regexes are categorically the wrong tool**
- **Regex-based libraries inherit the same flaws**
- **MX records MUST exist for Internet email**
- **Dot handling is provider-specific but bounded by RFC rules**
- **Delivery confirmation is the only truth**

If a system claims "email validation" without MX checks and confirmation flow, it is overstating its guarantees.

## Production Deployment

### Environment Variables
The API uses standard ASP.NET Core configuration:
- `ASPNETCORE_URLS` - Listening URLs (default: http://localhost:5000)
- `ASPNETCORE_ENVIRONMENT` - Environment (Development, Staging, Production)

### Docker (TODO)
Create Dockerfile for containerized deployment.

### Kubernetes (TODO)
Create Kubernetes deployment manifests for production deployment.

## About This Project

This is an open source project by [pengdows](https://github.com/pengdows), created to demonstrate the **only correct approach** to email validation and to settle the "just use a regex" argument once and for all.

The service is designed to be used standalone as a microservice, or integrated into any application that needs proper email validation.
