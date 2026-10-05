using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.FunctionalTests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_endpoint_is_available()
    {
        using var response = await _client.GetAsync("/api/v1/health");

        Assert.True(response.IsSuccessStatusCode);
    }
}
