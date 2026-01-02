# Email Validation Service

**Email validation done right: NO REGEX, MX-only, RFC-compliant**

> "When someone argues 'just add a regex for email validation,' send them here."

This is a reference implementation that demonstrates **why regex-based email validation is fundamentally wrong** and shows the **only correct approach**: a validation pipeline with DNS MX checks, zero regex, and comprehensive RFC test coverage.

[![Tests](https://img.shields.io/badge/tests-223%20passing-brightgreen)]()
[![RFC Compliant](https://img.shields.io/badge/RFC-5322%20%7C%205321%20%7C%201035-blue)]()
[![No Regex](https://img.shields.io/badge/regex-0-success)]()

---

## The Problem: Everyone Uses Regex (and It's Wrong)

You've seen this debate countless times:
- "We need to validate email addresses"
- "Just add a regex!"
- "Here's the regex: `/^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/`"

**This is categorically wrong.** Here's why:

### Why Regex (and Regex-Based Libraries) Fail

1. **Email syntax is not a regular language**
   - RFC 5321/5322 allow quoting, escaping, comments, and context-sensitive rules
   - Regex cannot express these rules without approximation
   - Approximation = incorrectness

2. **Regex validates appearance, not deliverability**
   - `user@example.com` may be syntactically fine but unroutable
   - Regex cannot tell if a domain exists, accepts mail, or drops messages

3. **False negatives break real users**
   - Valid but uncommon addresses get rejected
   - Quoted locals, plus-addressing, internationalized domains

4. **False positives create security and ops bugs**
   - "Validated" emails that can never receive mail
   - Broken password resets, account recovery failures, silent data loss

5. **Complex regexes are unmaintainable**
   - Large, unreadable, fragile
   - Still wrong

6. **Most "email validation libraries" are regex wrappers**
   - They inherit all the above failures
   - Many throw exceptions instead of returning clean invalid states
   - Many embed outdated or opinionated rules

**Bottom line:** If a system claims "email validation" and uses a regex anywhere in the decision path, it is lying about correctness.

---

## The Solution: Validation Pipeline (Not Pattern Matching)

Email validation is a **decision tree**, not a pattern match:

```
┌─────────────────────────────────────────────────────────┐
│ Layer 0: Policy Decision                                │
│ ➜ Allow local delivery (e.g., "root")? Usually NO      │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│ Layer 1: Structural Split                               │
│ ➜ Exactly one @, non-empty parts, no whitespace        │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│ Layer 2: Local-Part Validation (NO REGEX)              │
│ ➜ Character-by-character RFC rules                     │
│ ➜ Dot placement, allowed characters (ASCII only)       │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│ Layer 3: DNS A/AAAA Lookup                              │
│ ➜ Does the domain exist at all?                        │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│ Layer 4: DNS MX Lookup (MANDATORY)                      │
│ ➜ Does the domain accept mail?                         │
│ ➜ NO FALLBACK to A records (deprecated)                │
└─────────────────────────────────────────────────────────┘
                           ▼
              ✅ VALID (probabilistic)
                           ▼
┌─────────────────────────────────────────────────────────┐
│ Layer 6: Delivery Confirmation (outside this service)   │
│ ➜ Send mail → User receives → User confirms            │
│ ➜ ONLY ABSOLUTE VALIDATION                             │
└─────────────────────────────────────────────────────────┘
```

This service implements **Layers 0-4** (the correct stopping point for most systems).

---

## What Makes This Implementation Unique

Compared to existing email validation libraries ([see detailed comparison](COMPARISON.md)):

### ✅ Zero Regex, Principled Approach
- Character-by-character validation following RFC rules
- No regex anywhere in the codebase (check the code!)

### ✅ Strict MX-Only (Modern Best Practice)
```csharp
// Our approach: Correct ✅
if (!mxRecords.Any()) return DomainDoesNotAcceptMail;

// Most libraries: Incorrect ❌
if (!mxRecords.Any()) {
    // Fall back to A record (DEPRECATED RFC 974 behavior)
    if (aRecords.Any()) return Valid;
}
```

> **No MX record = no mail delivery. Period.**

### ✅ Comprehensive RFC Test Coverage
- **223 tests, all passing**
- Each test annotated with specific RFC section
- `Rfc5322_Section_3_2_3_Atext_Characters_AreValid`
- `Rfc5321_Section_5_MxRecords_Required_NoFallbackToA`

### ✅ Educational Focus
This isn't just code—it's **documentation that teaches WHY**:
- [RFC-COMPLIANCE.md](RFC-COMPLIANCE.md) - Complete RFC compliance documentation
- [COMPARISON.md](COMPARISON.md) - How this differs from existing tools
- In-code comments explain every RFC decision

### ✅ .NET Ecosystem
- Only open source .NET library with this level of rigor
- Modern .NET 8+ with async/await
- Production-ready ASP.NET Core minimal API

---

## Quick Start

### Run the API

```bash
dotnet run --project EmailValidation.Api
```

Visit `http://localhost:5000` for API documentation.

### Validate an Email

```bash
curl -X POST http://localhost:5000/validate \
  -H "Content-Type: application/json" \
  -d '{"email":"user@example.com"}'
```

**Response:**
```json
{
  "email": "user@example.com",
  "isValid": true,
  "normalizedEmail": "user@example.com",
  "localPart": "user",
  "domain": "example.com",
  "mxRecords": ["mail.example.com"],
  "failureReason": null,
  "failureMessage": null
}
```

**Invalid email response:**
```json
{
  "email": ".user@example.com",
  "isValid": false,
  "failureReason": "InvalidLocalPart",
  "failureMessage": "Local part cannot start with a dot (.)"
}
```

### Run Tests

```bash
dotnet test
# Output: Passed! - 223 tests passing
```

---

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | API information and quick reference |
| `GET` | `/docs` | Complete validation philosophy and pipeline documentation |
| `POST` | `/validate` | Validate single email address |
| `POST` | `/validate/batch` | Validate multiple email addresses concurrently |
| `GET` | `/health` | Health check endpoint |

---

## Validation Failure Reasons

Each failure includes a **specific reason** for debugging:

| Reason | Description | Example |
|--------|-------------|---------|
| `InvalidFormat` | Structural issues | No `@`, whitespace, control chars, multiple `@` |
| `InvalidLocalPart` | RFC violations | `.user@`, `user..name@`, invalid characters |
| `DomainDoesNotExist` | No DNS A/AAAA records | Non-existent domain |
| `DomainDoesNotAcceptMail` | No MX records | Domain exists but can't receive mail |
| `LocalDeliveryNotAllowed` | Policy violation | `root` without `@domain` |

---

## Configuration

```csharp
var options = new EmailValidatorOptions
{
    AllowLocalDelivery = false,  // Internet mail only (recommended)
    CheckDomainExists = true,     // Verify domain exists via DNS
    CheckMxRecords = true         // Verify MX records (MANDATORY)
};

var validator = new EmailValidator(options);
var result = await validator.ValidateAsync("user@example.com");
```

---

## Real-World Examples

### ✅ Valid Emails
```
user@example.com           ✅ Basic format
user.name@example.com      ✅ Dots in local-part (properly placed)
user+tag@example.com       ✅ Plus addressing (Gmail, FastMail, etc.)
user_test@example.com      ✅ Underscore
user-test@example.com      ✅ Hyphen
123@example.com            ✅ Numeric local-part
user@mail.example.com      ✅ Subdomain
```

### ❌ Invalid Emails (Correctly Rejected)
```
.user@example.com          ❌ Starts with dot
user.@example.com          ❌ Ends with dot
user..name@example.com     ❌ Consecutive dots
user @example.com          ❌ Contains whitespace
user@@example.com          ❌ Multiple @ symbols
user@example               ❌ No MX records (if check enabled)
user@192.168.1.1           ❌ IP literals not supported
user(comment)@example.com  ❌ Comments not supported
josé@example.com           ❌ Non-ASCII (SMTPUTF8 not supported)
```

---

## Technology Stack

- **.NET 8.0** - Modern, cross-platform
- **ASP.NET Core Minimal APIs** - Lightweight HTTP endpoints
- **xUnit + FluentAssertions** - Comprehensive test coverage
- **System.Net.Dns** - DNS lookups (A/AAAA records)
- **Platform tools (dig/nslookup)** - MX lookups (TODO: replace with DnsClient)

---

## Documentation

| Document | Description |
|----------|-------------|
| [README.md](README.md) | This file - quick start and overview |
| [CLAUDE.md](CLAUDE.md) | Detailed architecture and development guide |
| [RFC-COMPLIANCE.md](RFC-COMPLIANCE.md) | Complete RFC compliance documentation (9 RFCs) |
| [COMPARISON.md](COMPARISON.md) | How this differs from existing tools |

---

## Use Cases

### 1. User Registration
Validate email addresses during signup to prevent:
- Typos in email addresses
- Fake/undeliverable emails
- Support burden from bounced confirmation emails

### 2. Email List Hygiene
Clean email lists before sending campaigns:
- Remove addresses with no MX records
- Detect syntax errors
- Improve deliverability rates

### 3. API Integration
Use as a microservice in your architecture:
- Stateless, horizontally scalable
- RESTful API with clear error messages
- Batch validation for performance

### 4. Education & Reference
Show developers **why regex fails** and **how to do it correctly**:
- Point to specific RFC sections in tests
- Demonstrate proper validation pipeline
- Use in technical discussions/arguments 😄

---

## Settling the Argument

Next time someone says "just add a regex for email validation":

1. **Show them this repository** - Working code that proves why regex fails
2. **Point to the tests** - 223 tests covering RFC compliance
3. **Reference the docs** - RFC-COMPLIANCE.md explains every decision
4. **Show the comparison** - COMPARISON.md shows why other tools fall short

**The killer line:**
> "Email syntax is not a regular language. Regex cannot validate it correctly. Here's a service with 223 tests that proves it, including tests for RFC 5322 Section 3.2.3, RFC 5321 Section 5, and 7 other RFCs. Show me your regex that does the same."

---

## Contributing

Contributions welcome! This is a reference implementation, so we maintain high standards:

1. **No regex** - Ever. Non-negotiable.
2. **RFC compliance** - All changes must reference specific RFC sections
3. **Test coverage** - New features require comprehensive tests
4. **Documentation** - Explain WHY, not just what

See [CONTRIBUTING.md](CONTRIBUTING.md) for details.

---

## License

MIT License - see [LICENSE](LICENSE) for details.

Open sourced by [pengdows](https://github.com/pengdows) to settle arguments about email validation once and for all.

---

## Author

Built by [pengdows](https://github.com/pengdows) to demonstrate the **only correct approach** to email validation.

---

## Acknowledgments

- RFC authors for defining email standards
- Everyone who's ever argued with someone about regex email validation
- The developers who will use this to finally prove their point

**Star this repo** if it helped you win an argument about email validation! ⭐
