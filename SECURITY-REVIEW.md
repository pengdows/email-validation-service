# Security Review - Email Validation Service

**Review Date:** 2026-01-03
**Deployment Context:** Internal use behind firewall with ingress handling auth & SSL offloading
**Reviewer:** Security audit (automated + manual)

## Executive Summary

The email validation service has several **application-level security vulnerabilities** that need to be addressed before production deployment. The primary concerns are:

1. **DNS SSRF** - Service can be weaponized to scan internal network
2. **Resource exhaustion** - No limits on batch size, input size, or DNS timeouts
3. **Information disclosure** - Verbose error messages
4. **DNS security** - Weak query ID randomness, hardcoded public DNS servers

Since deployment will be behind a firewall with ingress handling authentication and SSL offloading, issues related to auth, HTTPS, and CORS are **not** critical for this deployment.

**Estimated remediation time:** 1-2 days for critical issues, 2-3 days for all recommended fixes.

---

## Critical Priority Issues

### 1. DNS SSRF / Internal Network Scanning ⚠️ CRITICAL

**Severity:** CRITICAL
**Affected Files:**
- `EmailValidation.Core/Validators/DnsValidator.cs:28` (ValidateDomainExistsAsync)
- `EmailValidation.Core/Validators/DnsValidator.cs:72` (ValidateMxRecordsAsync)

**Vulnerability:**
The service performs DNS lookups based on user-supplied domains without validating that responses don't point to internal/private IP addresses. An attacker can use this service to:
- Map internal network topology
- Enumerate internal hostnames
- Discover internal services
- Probe internal IP ranges

**Attack Scenario:**
```bash
# Enumerate internal infrastructure
POST /validate/batch
{
  "emails": [
    "test@jenkins.internal",
    "test@gitlab.internal",
    "test@database.internal",
    "test@admin-panel.internal",
    "test@vault.internal",
    "test@10.0.0.1",
    "test@192.168.1.1"
  ]
}

# Service responses reveal which hosts exist!
```

**Fix:**

Add to `DnsValidator.cs` after the `DnsValidator` class:

```csharp
internal static class IPAddressExtensions
{
    public static bool IsInternalOrPrivate(this IPAddress address)
    {
        if (address == null) return false;

        var bytes = address.GetAddressBytes();

        // IPv6 mapped IPv4
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
            bytes = address.GetAddressBytes();
        }

        return IPAddress.IsLoopback(address) ||
               // Private ranges (RFC 1918)
               (bytes[0] == 10) ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               // Link-local / APIPA (169.254.0.0/16)
               (bytes[0] == 169 && bytes[1] == 254) ||
               // Cloud metadata endpoints
               (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) || // 100.64.0.0/10 (Carrier-grade NAT)
               // Multicast
               (bytes[0] >= 224 && bytes[0] <= 239) ||
               // Reserved/experimental
               (bytes[0] >= 240);
    }
}
```

Update `ValidateDomainExistsAsync`:

```csharp
public static async Task<ValidationResult> ValidateDomainExistsAsync(
    string domain,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(domain))
    {
        return ValidationResult.Failure(
            ValidationFailureReason.DomainDoesNotExist,
            "Domain cannot be empty");
    }

    try
    {
        var addresses = await Dns.GetHostAddressesAsync(domain, cancellationToken);

        if (addresses.Length == 0)
        {
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                $"Domain '{domain}' has no A or AAAA records");
        }

        // SECURITY: Block private/internal IP addresses (SSRF protection)
        foreach (var addr in addresses)
        {
            if (addr.IsInternalOrPrivate())
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.DomainDoesNotExist,
                    $"Domain '{domain}' resolves to internal/private address");
            }
        }

        // Domain exists and is public
        return ValidationResult.Success(domain, string.Empty, domain);
    }
    catch (SocketException)
    {
        return ValidationResult.Failure(
            ValidationFailureReason.DomainDoesNotExist,
            $"Domain '{domain}' does not exist (DNS lookup failed)");
    }
    catch (Exception ex)
    {
        return ValidationResult.Failure(
            ValidationFailureReason.DomainDoesNotExist,
            $"DNS lookup failed for domain '{domain}': {ex.Message}");
    }
}
```

**Alternative Approach - Allowlist:**

If you need to validate internal domains, use an allowlist:

```csharp
// Add to EmailValidatorOptions.cs
public class EmailValidatorOptions
{
    public bool AllowInternalDomains { get; set; } = false;
    public HashSet<string> AllowedInternalDomains { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

// Resolve options from your DI/config path for the validator:
// var options = /* EmailValidatorOptions instance */;

// Then check in validation (pass options into the validator path):
if (addr.IsInternalOrPrivate() && !options.AllowInternalDomains)
{
    return ValidationResult.Failure(/* ... */);
}
```

**Testing:**
```csharp
// Add test in DnsValidatorTests.cs
[Fact]
public async Task ValidateDomainExistsAsync_RejectsPrivateIPAddresses()
{
    var result = await DnsValidator.ValidateDomainExistsAsync("localhost");
    result.IsValid.Should().BeFalse();
    result.FailureReason.Should().Be(ValidationFailureReason.DomainDoesNotExist);
    result.FailureMessage.Should().Contain("internal/private");
}
```

---

### 2. Unbounded Batch Size → Resource Exhaustion ⚠️ CRITICAL

**Severity:** CRITICAL
**Affected Files:**
- `EmailValidation.Api/Program.cs:121-146` (validate/batch endpoint)
- `EmailValidation.Api/ValidateBatchRequest.cs`

**Vulnerability:**
No maximum limit on batch size. An attacker can send 100,000+ emails in a single request, causing:
- Memory exhaustion (results dictionary)
- DNS query flooding (2 queries per unique domain)
- Thread pool exhaustion
- Service crash

**Attack Scenario:**
```bash
# Send 100,000 emails = 200,000 DNS queries
POST /validate/batch
{
  "emails": ["test1@domain1.com", "test2@domain2.com", ... x100000]
}
```

**Fix:**

Update `ValidateBatchRequest.cs`:

```csharp
namespace EmailValidation.Api;

public record ValidateBatchRequest(string[] Emails)
{
    /// <summary>
    /// Maximum number of emails that can be validated in a single batch request.
    /// Prevents memory exhaustion and DNS flooding attacks.
    /// </summary>
    public const int MaxBatchSize = 100;  // Adjust based on your needs (100-1000)
}
```

Update `Program.cs` batch endpoint:

```csharp
// Validate batch of emails
app.MapPost("/validate/batch", async (ValidateBatchRequest request, EmailValidator validator) =>
{
    if (request.Emails == null || !request.Emails.Any())
    {
        return Results.BadRequest(new { error = "Emails array is required and must not be empty" });
    }

    // SECURITY: Enforce batch size limit
    if (request.Emails.Length > ValidateBatchRequest.MaxBatchSize)
    {
        return Results.BadRequest(new {
            error = $"Batch size {request.Emails.Length} exceeds maximum of {ValidateBatchRequest.MaxBatchSize}",
            maxBatchSize = ValidateBatchRequest.MaxBatchSize,
            providedSize = request.Emails.Length
        });
    }

    var results = await validator.ValidateBatchAsync(request.Emails);

    var response = new ValidateBatchResponse
    {
        Results = results.Select(kvp => new ValidateResponse
        {
            Email = kvp.Key,
            IsValid = kvp.Value.IsValid,
            FailureReason = kvp.Value.FailureReason?.ToString(),
            FailureMessage = kvp.Value.FailureMessage,
            NormalizedEmail = kvp.Value.NormalizedEmail,
            LocalPart = kvp.Value.LocalPart,
            Domain = kvp.Value.Domain,
            MxRecords = kvp.Value.MxRecords
        }).ToArray()
    };

    return Results.Ok(response);
});
```

**Configuration Option (Optional):**

Make it configurable via appsettings:

```json
// appsettings.json
{
  "EmailValidation": {
    "MaxBatchSize": 100,
    "MaxEmailLength": 320
  }
}
```

```csharp
// Program.cs
builder.Services.Configure<ValidationLimits>(
    builder.Configuration.GetSection("EmailValidation"));
```

**Testing:**
```csharp
[Fact]
public async Task ValidateBatch_RejectsOversizedBatch()
{
    var emails = Enumerable.Range(1, 101).Select(i => $"test{i}@example.com").ToArray();
    var request = new ValidateBatchRequest(emails);

    var response = await client.PostAsJsonAsync("/validate/batch", request);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

---

### 3. No DNS Timeout → Thread Pool Exhaustion ⚠️ CRITICAL

**Severity:** CRITICAL
**Affected Files:**
- `EmailValidation.Core/Validators/DnsValidator.cs:28` (GetHostAddressesAsync)
- `EmailValidation.Core/Validators/DnsValidator.cs:121` (QueryMxRecordsAsync)

**Vulnerability:**
DNS queries have no explicit timeout. Slow or unresponsive DNS servers can cause:
- Requests hanging indefinitely
- Thread pool exhaustion
- Service becoming unresponsive

**Fix:**

Update `ValidateDomainExistsAsync`:

```csharp
public static async Task<ValidationResult> ValidateDomainExistsAsync(
    string domain,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(domain))
    {
        return ValidationResult.Failure(
            ValidationFailureReason.DomainDoesNotExist,
            "Domain cannot be empty");
    }

    // SECURITY: Add timeout to prevent hanging requests
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        timeoutCts.Token);

    try
    {
        var addresses = await Dns.GetHostAddressesAsync(domain, linkedCts.Token);

        if (addresses.Length == 0)
        {
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                $"Domain '{domain}' has no A or AAAA records");
        }

        // SECURITY: Block private/internal IP addresses (SSRF protection)
        foreach (var addr in addresses)
        {
            if (addr.IsInternalOrPrivate())
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.DomainDoesNotExist,
                    $"Domain '{domain}' resolves to internal/private address");
            }
        }

        return ValidationResult.Success(domain, string.Empty, domain);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        return ValidationResult.Failure(
            ValidationFailureReason.DomainDoesNotExist,
            $"DNS lookup timeout for domain '{domain}'");
    }
    catch (SocketException)
    {
        return ValidationResult.Failure(
            ValidationFailureReason.DomainDoesNotExist,
            $"Domain '{domain}' does not exist (DNS lookup failed)");
    }
    catch (Exception ex)
    {
        return ValidationResult.Failure(
            ValidationFailureReason.DomainDoesNotExist,
            $"DNS lookup failed for domain '{domain}': {ex.Message}");
    }
}
```

Update `QueryMxRecordsAsync`:

```csharp
private static async Task<string[]> QueryMxRecordsAsync(
    string domain,
    IPEndPoint nameServer,
    CancellationToken cancellationToken)
{
    // SECURITY: Add timeout to prevent hanging UDP requests
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        timeoutCts.Token);

    try
    {
        using var client = new UdpClient();
        client.Connect(nameServer);

        var query = BuildMxQuery(domain, out var queryId);

        await client.SendAsync(query, query.Length);
        var response = await client.ReceiveAsync(linkedCts.Token);

        return ParseMxResponse(response.Buffer, queryId);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        // Timeout - return empty array, will try next nameserver
        return Array.Empty<string>();
    }
}
```

**Configuration (Optional):**

Make timeout configurable:

```csharp
public class EmailValidatorOptions
{
    public TimeSpan DnsTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
```

---

### 4. No Input Size Limits → Memory DoS ⚠️ CRITICAL

**Severity:** CRITICAL
**Affected Files:**
- `EmailValidation.Api/Program.cs:96-118` (validate endpoint)

**Vulnerability:**
No maximum length validation on email input. Attacker can send arbitrarily large strings.

**Attack Scenario:**
```bash
POST /validate
{
  "email": "aaaaaaa...(10MB)...@example.com"
}
```

**Fix:**

Update `/validate` endpoint in `Program.cs`:

```csharp
// Validate single email
app.MapPost("/validate", async (ValidateRequest request, EmailValidator validator) =>
{
    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return Results.BadRequest(new { error = "Email is required" });
    }

    // SECURITY: Enforce maximum email length (RFC 5321: 64 + @ + 255 = 320)
    const int MaxEmailLength = 320;
    if (request.Email.Length > MaxEmailLength)
    {
        return Results.BadRequest(new {
            error = $"Email exceeds maximum length of {MaxEmailLength} characters",
            maxLength = MaxEmailLength,
            providedLength = request.Email.Length
        });
    }

    var result = await validator.ValidateAsync(request.Email);

    var response = new ValidateResponse
    {
        Email = request.Email,
        IsValid = result.IsValid,
        FailureReason = result.FailureReason?.ToString(),
        FailureMessage = result.FailureMessage,
        NormalizedEmail = result.NormalizedEmail,
        LocalPart = result.LocalPart,
        Domain = result.Domain,
        MxRecords = result.MxRecords
    };

    return Results.Ok(response);
});
```

Also add ASP.NET Core request body size limits in `Program.cs`:

```csharp
// After builder.Services.ConfigureHttpJsonOptions...

// SECURITY: Limit request body size
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 1_048_576; // 1 MB max
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.ValueLengthLimit = 10_240; // 10 KB per field
    options.MultipartBodyLengthLimit = 1_048_576; // 1 MB total
});
```

**Testing:**
```csharp
[Fact]
public async Task Validate_RejectsOversizedEmail()
{
    var email = new string('a', 321) + "@example.com";
    var request = new ValidateRequest(email);

    var response = await client.PostAsJsonAsync("/validate", request);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

---

## High Priority Issues

### 5. Rate Limiting (Defense in Depth) 🔴 HIGH

**Severity:** HIGH
**Affected Files:**
- `EmailValidation.Api/Program.cs`

**Issue:**
Even with ingress authentication, application-level rate limiting provides:
- Defense in depth if ingress misconfigured
- Protection against compromised internal accounts
- Protection against buggy internal services
- Different limits per endpoint

**Fix:**

Install package:
```bash
dotnet add EmailValidation.Api/EmailValidation.Api.csproj package AspNetCoreRateLimit
```

Update `Program.cs`:

```csharp
using AspNetCoreRateLimit;

// Add before var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddMemoryCache();

builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.EnableEndpointRateLimiting = true;
    options.StackBlockedRequests = false;
    options.RealIpHeader = "X-Forwarded-For"; // If behind proxy/ingress
    options.ClientIdHeader = "X-ClientId";

    options.GeneralRules = new List<RateLimitRule>
    {
        new RateLimitRule
        {
            Endpoint = "POST:/validate",
            Period = "1m",
            Limit = 100  // 100 single validations per minute per IP
        },
        new RateLimitRule
        {
            Endpoint = "POST:/validate/batch",
            Period = "1m",
            Limit = 10  // 10 batch requests per minute per IP
        },
        new RateLimitRule
        {
            Endpoint = "*",
            Period = "1m",
            Limit = 200  // 200 total requests per minute per IP
        }
    };
});

builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// ... rest of builder configuration

var app = builder.Build();

// Add BEFORE any MapGet/MapPost
app.UseIpRateLimiting();

// ... rest of app configuration
```

Configuration via `appsettings.json`:

```json
{
  "IpRateLimiting": {
    "EnableEndpointRateLimiting": true,
    "StackBlockedRequests": false,
    "RealIpHeader": "X-Forwarded-For",
    "HttpStatusCode": 429,
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

**Alternative (Built-in .NET 7+):**

If using .NET 7+, use built-in rate limiting:

```csharp
using System.Threading.RateLimiting;

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("validate", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 100;
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("batch", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 10;
        opt.QueueLimit = 0;
    });
});

var app = builder.Build();
app.UseRateLimiter();

app.MapPost("/validate", async (ValidateRequest request, EmailValidator validator) =>
{
    // ... handler code
}).RequireRateLimiting("validate");

app.MapPost("/validate/batch", async (ValidateBatchRequest request, EmailValidator validator) =>
{
    // ... handler code
}).RequireRateLimiting("batch");
```

---

### 6. DNS Query ID Collision 🟠 MEDIUM-HIGH

**Severity:** MEDIUM-HIGH
**Affected Files:**
- `EmailValidation.Core/Validators/DnsValidator.cs:134`

**Issue:**
Weak query ID randomness using only 16-bit random number. An on-path attacker on internal network can spoof DNS responses. No source port randomization.

**Fix (Recommended):**

Replace custom DNS implementation with `DnsClient` library:

```bash
dotnet add EmailValidation.Core/EmailValidation.Core.csproj package DnsClient
```

Replace `GetMxRecordsAsync` implementation:

```csharp
using DnsClient;

private static async Task<string[]> GetMxRecordsAsync(string domain, CancellationToken cancellationToken)
{
    var lookup = new LookupClient(GetLookupClientOptions());

    try
    {
        var result = await lookup.QueryAsync(domain, QueryType.MX, cancellationToken: cancellationToken);

        if (result.HasError)
        {
            return Array.Empty<string>();
        }

        return result.Answers
            .MxRecords()
            .OrderBy(mx => mx.Preference)
            .Select(mx => mx.Exchange.Value.TrimEnd('.'))
            .ToArray();
    }
    catch
    {
        return Array.Empty<string>();
    }
}

private static LookupClientOptions GetLookupClientOptions()
{
    var nameServers = GetNameServers();
    var options = new LookupClientOptions(nameServers)
    {
        UseCache = false,  // For security and freshness
        Timeout = TimeSpan.FromSeconds(5),
        Retries = 2,
        ThrowDnsErrors = false
    };
    return options;
}

// Update GetNameServers to return NameServer[] instead of IPEndPoint[]
private static NameServer[] GetNameServers()
{
    var servers = new List<NameServer>();

    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
    {
        try
        {
            foreach (var line in File.ReadLines("/etc/resolv.conf"))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("nameserver", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                if (IPAddress.TryParse(parts[1], out var ip))
                {
                    servers.Add(new NameServer(new IPEndPoint(ip, 53)));
                }
            }
        }
        catch
        {
            // Ignore and fall back
        }
    }

    if (servers.Count == 0)
    {
        // Note: Consider making these configurable instead of hardcoded
        servers.Add(new NameServer(IPAddress.Parse("8.8.8.8")));
        servers.Add(new NameServer(IPAddress.Parse("1.1.1.1")));
    }

    return servers.ToArray();
}
```

Then remove all custom DNS parsing code:
- `BuildMxQuery`
- `ParseMxResponse`
- `QueryMxRecordsAsync` (replace with above)
- Helper methods for DNS packet building/parsing

**Benefits:**
- Proper query ID randomization
- Source port randomization
- DNSSEC support (optional)
- Better error handling
- Maintained by DNS experts

---

## Medium Priority Issues

### 7. Information Disclosure in Error Messages 🟡 MEDIUM

**Severity:** MEDIUM
**Affected Files:**
- `EmailValidation.Core/Validators/DnsValidator.cs:50, :87`

**Issue:**
Detailed exception messages expose internal infrastructure details.

**Fix:**

Add environment-aware error messages:

```csharp
// Add to EmailValidatorOptions or inject IHostEnvironment
public class EmailValidatorOptions
{
    public bool DetailedErrorMessages { get; set; } = false;
}

// Resolve options from your DI/config path for the validator:
// var options = /* EmailValidatorOptions instance */;

// Update error handling in ValidateDomainExistsAsync:
catch (Exception ex)
{
    var message = $"Domain '{domain}' could not be validated";

    // Only include details in development/when configured
    if (options.DetailedErrorMessages)
    {
        message += $" (Detail: {ex.Message})";
    }

    return ValidationResult.Failure(
        ValidationFailureReason.DomainDoesNotExist,
        message);
}
```

Configure via `appsettings.json`:

```json
{
  "EmailValidation": {
    "DetailedErrorMessages": false
  }
}

// appsettings.Development.json
{
  "EmailValidation": {
    "DetailedErrorMessages": true
  }
}
```

---

### 8. Hardcoded Public DNS Servers 🟡 MEDIUM

**Severity:** MEDIUM (Data Leakage)
**Affected Files:**
- `EmailValidation.Core/Validators/DnsValidator.cs:347-348`

**Issue:**
Hardcoded fallback to Google (8.8.8.8) and Cloudflare (1.1.1.1):
- All DNS queries sent to external parties
- Privacy violation (Google/Cloudflare see all validated domains)
- Bypasses internal DNS security controls
- May violate corporate policies

**Fix:**

Make DNS servers configurable:

```csharp
public class EmailValidatorOptions
{
    public string? PrimaryDnsServer { get; set; }
    public string? SecondaryDnsServer { get; set; }
    public bool AllowPublicDnsFallback { get; set; } = false;
}
```

Update `GetNameServers()`:

```csharp
private static IPEndPoint[] GetNameServers(EmailValidatorOptions options)
{
    var servers = new List<IPEndPoint>();

    // Try configured servers first
    if (!string.IsNullOrWhiteSpace(options.PrimaryDnsServer) &&
        IPAddress.TryParse(options.PrimaryDnsServer, out var primary))
    {
        servers.Add(new IPEndPoint(primary, 53));
    }

    if (!string.IsNullOrWhiteSpace(options.SecondaryDnsServer) &&
        IPAddress.TryParse(options.SecondaryDnsServer, out var secondary))
    {
        servers.Add(new IPEndPoint(secondary, 53));
    }

    // Try system resolv.conf
    if (servers.Count == 0 && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
    {
        try
        {
            foreach (var line in File.ReadLines("/etc/resolv.conf"))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("nameserver", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && IPAddress.TryParse(parts[1], out var ip))
                {
                    servers.Add(new IPEndPoint(ip, 53));
                }
            }
        }
        catch
        {
            // Ignore
        }
    }

    // Fallback to public DNS only if explicitly allowed
    if (servers.Count == 0 && options.AllowPublicDnsFallback)
    {
        servers.Add(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
        servers.Add(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53));
    }

    if (servers.Count == 0)
    {
        throw new InvalidOperationException(
            "No DNS servers configured. Set PrimaryDnsServer in configuration or enable AllowPublicDnsFallback.");
    }

    return servers.ToArray();
}
```

Configuration:

```json
// appsettings.json
{
  "EmailValidation": {
    "PrimaryDnsServer": "10.0.0.10",    // Your internal DNS
    "SecondaryDnsServer": "10.0.0.11",  // Backup internal DNS
    "AllowPublicDnsFallback": false     // Never use public DNS
  }
}
```

---

### 9. No Logging/Monitoring 🟡 MEDIUM

**Severity:** MEDIUM
**Affected Files:**
- `EmailValidation.Api/Program.cs`

**Issue:**
No logging of validation attempts, failures, or abuse patterns makes it impossible to detect and investigate security incidents.

**Fix:**

Add logging to endpoints:

```csharp
// Inject ILogger into endpoints
app.MapPost("/validate", async (
    ValidateRequest request,
    EmailValidator validator,
    ILogger<Program> logger,
    HttpContext httpContext) =>
{
    var clientIp = httpContext.Connection.RemoteIpAddress;

    if (string.IsNullOrWhiteSpace(request.Email))
    {
        logger.LogWarning(
            "Validation request with empty email from {IP}",
            clientIp);
        return Results.BadRequest(new { error = "Email is required" });
    }

    const int MaxEmailLength = 320;
    if (request.Email.Length > MaxEmailLength)
    {
        logger.LogWarning(
            "Validation request exceeds max length ({Length} > {Max}) from {IP}",
            request.Email.Length,
            MaxEmailLength,
            clientIp);
        return Results.BadRequest(new {
            error = $"Email exceeds maximum length of {MaxEmailLength} characters"
        });
    }

    logger.LogInformation(
        "Validating email for domain {Domain} from {IP}",
        request.Email.Split('@').LastOrDefault() ?? "unknown",
        clientIp);

    var result = await validator.ValidateAsync(request.Email);

    if (!result.IsValid)
    {
        logger.LogInformation(
            "Validation failed for {Email}: {Reason} from {IP}",
            request.Email,
            result.FailureReason,
            clientIp);
    }

    var response = new ValidateResponse
    {
        Email = request.Email,
        IsValid = result.IsValid,
        FailureReason = result.FailureReason?.ToString(),
        FailureMessage = result.FailureMessage,
        NormalizedEmail = result.NormalizedEmail,
        LocalPart = result.LocalPart,
        Domain = result.Domain,
        MxRecords = result.MxRecords
    };

    return Results.Ok(response);
});

app.MapPost("/validate/batch", async (
    ValidateBatchRequest request,
    EmailValidator validator,
    ILogger<Program> logger,
    HttpContext httpContext) =>
{
    var clientIp = httpContext.Connection.RemoteIpAddress;

    if (request.Emails == null || !request.Emails.Any())
    {
        return Results.BadRequest(new { error = "Emails array is required and must not be empty" });
    }

    if (request.Emails.Length > ValidateBatchRequest.MaxBatchSize)
    {
        logger.LogWarning(
            "Batch validation request exceeds limit ({Size} > {Max}) from {IP}",
            request.Emails.Length,
            ValidateBatchRequest.MaxBatchSize,
            clientIp);

        return Results.BadRequest(new {
            error = $"Batch size {request.Emails.Length} exceeds maximum of {ValidateBatchRequest.MaxBatchSize}"
        });
    }

    logger.LogInformation(
        "Batch validation request for {Count} emails from {IP}",
        request.Emails.Length,
        clientIp);

    var results = await validator.ValidateBatchAsync(request.Emails);

    var failedCount = results.Values.Count(r => !r.IsValid);
    logger.LogInformation(
        "Batch validation completed: {Total} emails, {Failed} failed from {IP}",
        results.Count,
        failedCount,
        clientIp);

    var response = new ValidateBatchResponse
    {
        Results = results.Select(kvp => new ValidateResponse
        {
            Email = kvp.Key,
            IsValid = kvp.Value.IsValid,
            FailureReason = kvp.Value.FailureReason?.ToString(),
            FailureMessage = kvp.Value.FailureMessage,
            NormalizedEmail = kvp.Value.NormalizedEmail,
            LocalPart = kvp.Value.LocalPart,
            Domain = kvp.Value.Domain,
            MxRecords = kvp.Value.MxRecords
        }).ToArray()
    };

    return Results.Ok(response);
});
```

Add structured logging configuration:

```json
// appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "EmailValidation": "Information"
    }
  }
}
```

Consider adding Application Insights, Seq, or ELK for centralized logging.

---

## Low Priority / Informational

### 10. Missing Health Check in Dockerfile 🟢 LOW

**Affected Files:**
- `Dockerfile`

**Fix:**

Add health check to Dockerfile:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EmailValidation.sln ./
COPY EmailValidation.Api/EmailValidation.Api.csproj EmailValidation.Api/
COPY EmailValidation.Core/EmailValidation.Core.csproj EmailValidation.Core/
COPY EmailValidation.Tests/EmailValidation.Tests.csproj EmailValidation.Tests/

RUN dotnet restore

COPY . ./
RUN dotnet publish EmailValidation.Api/EmailValidation.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.1-azurelinux3.0-distroless AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

COPY --from=build --chown=10001:0 /app/publish ./

USER 10001
EXPOSE 8080

# Add health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "EmailValidation.Api.dll"]
```

Note: `curl` won't be available in distroless image. Either:
1. Use a health check at orchestration level (Kubernetes liveness probe)
2. Switch to non-distroless image
3. Add a lightweight health check binary

---

### 11. Case Sensitivity Handling 🟢 INFORMATIONAL

**Affected Files:**
- `EmailValidation.Core/EmailValidator.cs:40`

**Issue:**
Uses `ToLowerInvariant()` on entire email, but RFC 5321 specifies local-part is case-sensitive.

**Current Behavior:**
```csharp
var normalizedEmail = email.Trim().ToLowerInvariant();
```

**Note:**
This is technically incorrect per RFC 5321, but:
- Most modern email systems treat local-part as case-insensitive
- This is a pragmatic, common approach
- Aligns with real-world usage

**Recommendation:**
Document this decision in code comments:

```csharp
// Note: RFC 5321 specifies local-part is case-sensitive, but most modern
// email systems treat it as case-insensitive. We normalize to lowercase
// for consistency and practical usability.
var normalizedEmail = email.Trim().ToLowerInvariant();
```

---

### 12. Missing Request Correlation IDs 🟢 LOW

**Issue:**
No correlation ID for tracking requests across logs.

**Fix:**

Add middleware:

```csharp
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? Guid.NewGuid().ToString();

    context.Response.Headers.Add("X-Correlation-ID", correlationId);

    using (logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId
    }))
    {
        await next();
    }
});
```

---

## Items NOT Required for Internal Deployment

These are handled by your ingress layer and are **not required** for this deployment:

- ✅ Authentication/Authorization (handled by ingress)
- ✅ HTTPS/TLS enforcement (SSL offloading at ingress)
- ✅ CORS configuration (ingress policy)
- ✅ Security headers (can be added at ingress)

---

## Testing Checklist

After implementing fixes, verify:

- [ ] Batch size limit enforced (test with 101+ emails)
- [ ] Email length limit enforced (test with 321+ char email)
- [ ] DNS timeout works (test with slow/unresponsive domain)
- [ ] Private IP blocking works (test with localhost, 192.168.x.x, 10.x.x.x)
- [ ] Rate limiting works (send 101+ requests in 1 minute)
- [ ] Logging captures validation attempts and failures
- [ ] Health check returns 200 OK
- [ ] Error messages don't leak internal details (in production mode)

---

## Configuration Summary

After all fixes, your `appsettings.json` should include:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "EmailValidation": "Information"
    }
  },
  "AllowedHosts": "*",
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
    "StackBlockedRequests": false,
    "RealIpHeader": "X-Forwarded-For",
    "HttpStatusCode": 429,
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

## Implementation Priority

1. **Week 1 - Critical Fixes** (1-2 days):
   - DNS SSRF protection (private IP blocking)
   - Batch size limits
   - DNS timeouts
   - Input size limits

2. **Week 2 - Security Hardening** (1-2 days):
   - Rate limiting
   - Switch to DnsClient library
   - Configurable DNS servers
   - Logging

3. **Week 3 - Polish** (1 day):
   - Error message cleanup
   - Health checks
   - Correlation IDs
   - Documentation updates

**Total estimated effort: 3-5 days**

---

## References

- [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
- [RFC 1918 - Private IP Addresses](https://datatracker.ietf.org/doc/html/rfc1918)
- [RFC 5321 - SMTP](https://datatracker.ietf.org/doc/html/rfc5321)
- [ASP.NET Core Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
- [DnsClient.NET Documentation](https://dnsclient.michaco.net/)

---

**End of Security Review**
