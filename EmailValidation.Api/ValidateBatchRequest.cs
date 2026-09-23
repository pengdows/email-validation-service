namespace EmailValidation.Api;

public record ValidateBatchRequest(string[] Emails)
{
    public const int MaxBatchSize = 100;
}
