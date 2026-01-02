namespace EmailValidation.Api;

public record ValidateBatchResponse
{
    public required ValidateResponse[] Results { get; init; }
}
