using System.Net;
using Xunit;

namespace JiraLite.Api.IntegrationTests;

public class HealthCheckTests : IClassFixture<JiraLiteApiFactory>
{
    private readonly JiraLiteApiFactory _factory;

    public HealthCheckTests(JiraLiteApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_endpoint_returns_200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
