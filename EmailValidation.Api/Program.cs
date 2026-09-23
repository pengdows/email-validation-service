using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using EmailValidation.Api;
using EmailValidation.Caching;
using EmailValidation.Core;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services
// DNS results are uncached by default. Set DnsCache:SqlitePath (or the
// DNSCACHE__SQLITEPATH env var) to enable a persistent, cross-request cache
// backed by pengdows.crud - see EmailValidation.Caching/SqliteDnsCache.cs.
var dnsCacheSqlitePath = builder.Configuration["DnsCache:SqlitePath"];
if (string.IsNullOrWhiteSpace(dnsCacheSqlitePath))
{
    builder.Services.AddSingleton<IDnsValidator, DefaultDnsValidator>();
}
else
{
    var cachingValidator = await SqliteDnsCache.CreateAsync(new DefaultDnsValidator(), dnsCacheSqlitePath);
    builder.Services.AddSingleton<IDnsValidator>(cachingValidator);
}

builder.Services.AddSingleton(sp => new EmailValidator(
    new EmailValidatorOptions
    {
        AllowLocalDelivery = false,      // Internet mail only
        CheckDomainExists = true,         // Check DNS A/AAAA
        CheckMxRecords = true             // Check MX records (MANDATORY)
    },
    sp.GetRequiredService<IDnsValidator>()));

// Configure Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", policy =>
    {
        policy.PermitLimit = 100;
        policy.Window = TimeSpan.FromSeconds(10);
        policy.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        policy.QueueLimit = 10;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Configure JSON options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.UseRateLimiter();

// API Documentation endpoint
app.MapGet("/", () => new
{
    service = "Email Validation Service",
    version = "1.0",
    documentation = "/docs",
    endpoints = new
    {
        validate = "POST /validate",
        validateBatch = "POST /validate/batch",
        health = "GET /health"
    }
});

// Documentation endpoint
app.MapGet("/docs", () => new
{
    service = "Email Validation Service",
    description = "Email validation service that implements proper validation pipeline (NO REGEX)",
    philosophy = "Email validation is a pipeline, not a pattern match",

    pipeline = new[]
    {
        "0. Policy decision (local delivery allowed?)",
        "1. Structural split (exactly one @, non-empty parts)",
        "2. Local-part validation (RFC rules, no regex)",
        "3. DNS A/AAAA check (domain exists)",
        "4. MX record check (domain accepts mail) - MANDATORY",
        "5. SMTP verification (optional, unreliable, not implemented)",
        "6. Delivery confirmation (only authoritative method - not in this service)"
    },

    whyNotRegex = new[]
    {
        "Email syntax is not a regular language",
        "Regex validates appearance, not deliverability",
        "False negatives break real users",
        "False positives create security bugs",
        "Complex regexes are unmaintainable and still wrong"
    },

    mxRecordPolicy = "No MX record = no mail delivery. Period. No fallback to A records.",

    endpoints = new
    {
        validate = new
        {
            method = "POST",
            path = "/validate",
            body = new { email = "user@example.com" },
            description = "Validate single email address"
        },
        validateBatch = new
        {
            method = "POST",
            path = "/validate/batch",
            body = new { emails = new[] { "user1@example.com", "user2@example.com" } },
            description = "Validate multiple email addresses concurrently"
        },
        health = new
        {
            method = "GET",
            path = "/health",
            description = "Health check endpoint"
        }
    }
});

// Validate single email
app.MapPost("/validate", async (ValidateRequest request, EmailValidator validator) =>
{
    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return Results.BadRequest(new { error = "Email is required" });
    }

    if (request.Email.Length > 320)
    {
        return Results.BadRequest(new { error = "Email exceeds maximum length of 320 characters" });
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
        MxRecords = result.MxRecords,
        IsDisposable = result.IsDisposable,
        IsRoleBased = result.IsRoleBased
    };

    return Results.Ok(response);
}).RequireRateLimiting("fixed");

// Validate batch of emails
app.MapPost("/validate/batch", async (ValidateBatchRequest request, EmailValidator validator) =>
{
    if (request.Emails == null || !request.Emails.Any())
    {
        return Results.BadRequest(new { error = "Emails array is required and must not be empty" });
    }

    if (request.Emails.Length > ValidateBatchRequest.MaxBatchSize)
    {
        return Results.BadRequest(new { error = $"Batch size {request.Emails.Length} exceeds maximum of {ValidateBatchRequest.MaxBatchSize}" });
    }

    if (request.Emails.Any(string.IsNullOrWhiteSpace))
    {
        return Results.BadRequest(new { error = "Emails array cannot contain null, empty, or whitespace-only values" });
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
            MxRecords = kvp.Value.MxRecords,
            IsDisposable = kvp.Value.IsDisposable,
            IsRoleBased = kvp.Value.IsRoleBased
        }).ToArray()
    };

    return Results.Ok(response);
}).RequireRateLimiting("fixed");

// Health check
app.MapGet("/health", () => new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "email-validation-service"
});

app.Run();

public partial class Program;
