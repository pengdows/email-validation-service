namespace EmailValidation.Api;

public record ValidateResponse
{
    public required string Email { get; init; }
    public required bool IsValid { get; init; }
    public string? FailureReason { get; init; }
    public string? FailureMessage { get; init; }
    public string? NormalizedEmail { get; init; }
    public string? LocalPart { get; init; }
    public string? Domain { get; init; }
    public string[]? MxRecords { get; init; }
}
