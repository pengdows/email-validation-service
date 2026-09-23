# Security Fixes - Implementation Checklist

Quick reference checklist for implementing security fixes. See `SECURITY-REVIEW.md` for detailed explanations and code examples.

## Critical Priority (Must Fix Before Production)

### 1. DNS SSRF Protection - Block Private IPs ⚠️
- [ ] Add `IsInternalOrPrivate()` extension method to `DnsValidator.cs`
- [ ] Update `ValidateDomainExistsAsync()` to validate DNS responses
- [ ] Block localhost (127.x.x.x)
- [ ] Block RFC1918 private ranges (10.x.x.x, 172.16-31.x.x, 192.168.x.x)
- [ ] Block link-local (169.254.x.x)
- [ ] Block multicast/reserved ranges
- [ ] Add test: `ValidateDomainExistsAsync_RejectsPrivateIPAddresses()`
- [ ] Test with: localhost, 192.168.1.1, 10.0.0.1

**Files:** `EmailValidation.Core/Validators/DnsValidator.cs:28`

---

### 2. Batch Size Limits ⚠️
- [ ] Add `MaxBatchSize = 100` constant to `ValidateBatchRequest.cs`
- [ ] Add batch size validation in `/validate/batch` endpoint
- [ ] Return 400 Bad Request if exceeded
- [ ] Add test: `ValidateBatch_RejectsOversizedBatch()`
- [ ] Test with 101+ emails

**Files:**
- `EmailValidation.Api/ValidateBatchRequest.cs`
- `EmailValidation.Api/Program.cs:121`

---

### 3. DNS Timeouts ⚠️
- [ ] Add 5-second timeout to `ValidateDomainExistsAsync()`
- [ ] Add 5-second timeout to `QueryMxRecordsAsync()`
- [ ] Use `CancellationTokenSource.CreateLinkedTokenSource()`
- [ ] Handle `OperationCanceledException`
- [ ] Test with slow/unresponsive domains

**Files:**
- `EmailValidation.Core/Validators/DnsValidator.cs:28`
- `EmailValidation.Core/Validators/DnsValidator.cs:121`

---

### 4. Input Size Limits ⚠️
- [ ] Add `MaxEmailLength = 320` validation to `/validate` endpoint
- [ ] Return 400 Bad Request if exceeded
- [ ] Add Kestrel `MaxRequestBodySize = 1MB` limit
- [ ] Add `FormOptions.ValueLengthLimit = 10KB` limit
- [ ] Add test: `Validate_RejectsOversizedEmail()`
- [ ] Test with 321+ character email

**Files:** `EmailValidation.Api/Program.cs:96`

---

## High Priority (Recommended)

### 5. Rate Limiting 🔴
- [ ] Install NuGet: `dotnet add package AspNetCoreRateLimit`
- [ ] Configure `IpRateLimitOptions` with rules
- [ ] Add `app.UseIpRateLimiting()` middleware
- [ ] Set `/validate` limit: 100/minute
- [ ] Set `/validate/batch` limit: 10/minute
- [ ] Configure `X-Forwarded-For` header (behind proxy)
- [ ] Test rate limit enforcement

**Files:** `EmailValidation.Api/Program.cs`

**Alternative:** Use built-in .NET 7+ rate limiting with `AddRateLimiter()`

---

### 6. DNS Security - Use DnsClient Library 🟠
- [ ] Install NuGet: `dotnet add package DnsClient`
- [ ] Replace `GetMxRecordsAsync()` with DnsClient implementation
- [ ] Remove custom DNS parsing code (`BuildMxQuery`, `ParseMxResponse`, etc.)
- [ ] Configure timeout = 5 seconds
- [ ] Disable DNS cache (`UseCache = false`)
- [ ] Update `GetNameServers()` to return `NameServer[]`
- [ ] Test MX lookups still work

**Files:** `EmailValidation.Core/Validators/DnsValidator.cs`

---

## Medium Priority (Hardening)

### 7. Configurable DNS Servers 🟡
- [ ] Add `PrimaryDnsServer` to `EmailValidatorOptions`
- [ ] Add `SecondaryDnsServer` to `EmailValidatorOptions`
- [ ] Add `AllowPublicDnsFallback` option (default: false)
- [ ] Update `GetNameServers()` to read from config
- [ ] Throw exception if no DNS servers configured
- [ ] Add to `appsettings.json`: set internal DNS servers
- [ ] Test with configured DNS servers

**Files:**
- `EmailValidation.Core/EmailValidator.cs`
- `EmailValidation.Core/Validators/DnsValidator.cs:311`
- `appsettings.json`

---

### 8. Reduce Error Verbosity 🟡
- [ ] Add `DetailedErrorMessages` to `EmailValidatorOptions` (default: false)
- [ ] Update exception handlers to check this flag
- [ ] Return generic errors in production
- [ ] Return detailed errors only when enabled
- [ ] Set `DetailedErrorMessages = true` in `appsettings.Development.json`
- [ ] Set `DetailedErrorMessages = false` in `appsettings.json`
- [ ] Test error messages in both modes

**Files:** `EmailValidation.Core/Validators/DnsValidator.cs:50, :87`

---

### 9. Add Logging 🟡
- [ ] Inject `ILogger<Program>` into endpoints
- [ ] Log validation requests with client IP
- [ ] Log batch requests with count and IP
- [ ] Log validation failures with reason
- [ ] Log oversized requests (batch/email)
- [ ] Log rate limit violations
- [ ] Configure log levels in `appsettings.json`
- [ ] Test logs appear correctly

**Files:** `EmailValidation.Api/Program.cs`

---

## Low Priority / Optional

### 10. Docker Health Check 🟢
- [ ] Add `HEALTHCHECK` to Dockerfile
- [ ] Configure interval=30s, timeout=3s
- [ ] Test health check works
- [ ] OR configure health check in Kubernetes manifests

**Files:** `Dockerfile`

---

### 11. Request Correlation IDs 🟢
- [ ] Add middleware to generate/extract correlation IDs
- [ ] Read from `X-Correlation-ID` header
- [ ] Generate new GUID if not present
- [ ] Add to response headers
- [ ] Add to log scope
- [ ] Test correlation IDs in logs

**Files:** `EmailValidation.Api/Program.cs`

---

### 12. Document Case Sensitivity 🟢
- [ ] Add comment explaining `ToLowerInvariant()` decision
- [ ] Note RFC 5321 vs. real-world usage
- [ ] Update README if needed

**Files:** `EmailValidation.Core/EmailValidator.cs:40`

---

## Configuration File Updates

### appsettings.json
```json
{
  "EmailValidation": {
    "MaxBatchSize": 100,
    "MaxEmailLength": 320,
    "DnsTimeout": "00:00:05",
    "PrimaryDnsServer": "10.0.0.10",
    "SecondaryDnsServer": "10.0.0.11",
    "AllowPublicDnsFallback": false,
    "AllowInternalDomains": false,
    "DetailedErrorMessages": false
  },
  "IpRateLimiting": {
    "EnableEndpointRateLimiting": true,
    "RealIpHeader": "X-Forwarded-For",
    "GeneralRules": [
      {
        "Endpoint": "POST:/validate",
        "Period": "1m",
        "Limit": 100
      },
      {
        "Endpoint": "POST:/validate/batch",
        "Period": "1m",
        "Limit": 10
      }
    ]
  }
}
```

---

## NuGet Packages to Install

```bash
# Rate limiting
dotnet add EmailValidation.Api/EmailValidation.Api.csproj package AspNetCoreRateLimit

# DNS client library (recommended)
dotnet add EmailValidation.Core/EmailValidation.Core.csproj package DnsClient
```

---

## Testing Checklist

After implementation, verify:

- [ ] **SSRF Protection**: Try validating `test@localhost`, `test@192.168.1.1`, `test@10.0.0.1` → should fail
- [ ] **Batch Limit**: Send 101 emails in batch → should return 400
- [ ] **Email Length**: Send 321+ character email → should return 400
- [ ] **DNS Timeout**: Test with slow domain → should timeout in ~5 seconds
- [ ] **Rate Limiting**: Send 101 requests in 1 minute → should return 429
- [ ] **Logging**: Check logs contain client IP, email count, failure reasons
- [ ] **Error Messages**: In production mode, errors should be generic (no stack traces)
- [ ] **Health Check**: `curl http://localhost:8080/health` → should return 200

---

## Estimated Implementation Time

- **Critical fixes (1-4):** 4-8 hours
- **High priority (5-6):** 3-4 hours
- **Medium priority (7-9):** 3-4 hours
- **Low priority (10-12):** 1-2 hours

**Total: 1-2 days of focused work**

---

## Quick Reference - Critical Code Locations

| Issue | File | Line | Fix |
|-------|------|------|-----|
| DNS SSRF | `DnsValidator.cs` | 28 | Add private IP validation |
| Batch Size | `Program.cs` | 121 | Add size limit check |
| DNS Timeout | `DnsValidator.cs` | 28, 121 | Add CancellationTokenSource |
| Input Size | `Program.cs` | 96 | Add length validation |
| Rate Limit | `Program.cs` | - | Add middleware |

---

**See SECURITY-REVIEW.md for detailed code examples and explanations.**
