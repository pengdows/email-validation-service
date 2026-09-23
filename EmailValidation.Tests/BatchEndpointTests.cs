using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EmailValidation.Tests;

public class BatchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public BatchEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ValidateBatch_WithNullEmailEntry_ReturnsBadRequest()
    {
        // Arrange
        using var content = new StringContent(
            """{"emails":["valid@example.com",null]}""",
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/validate/batch", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("error").GetString()
            .Should().Be("Emails array cannot contain null, empty, or whitespace-only values");
    }

    [Fact]
    public async Task ValidateBatch_WithWhitespaceEmailEntry_ReturnsBadRequest()
    {
        // Arrange
        using var content = new StringContent(
            """{"emails":["valid@example.com","   "]}""",
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/validate/batch", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("error").GetString()
            .Should().Be("Emails array cannot contain null, empty, or whitespace-only values");
    }

    [Fact]
    public async Task ValidateBatch_ExceedsMaxBatchSize_ReturnsBadRequest()
    {
        // Arrange
        var emails = Enumerable.Range(0, 101).Select(i => $"user{i}@example.com").ToArray();
        var request = new { emails };

        // Act
        var response = await _client.PostAsJsonAsync("/validate/batch", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("error").GetString()
            .Should().Contain("exceeds maximum of 100");
    }
}
