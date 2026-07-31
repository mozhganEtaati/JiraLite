using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace JiraLite.Api.IntegrationTests;

public class ErrorHandlingTests : IClassFixture<JiraLiteApiFactory>
{
    private readonly JiraLiteApiFactory _factory;

    public ErrorHandlingTests(JiraLiteApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Malformed_JSON_body_returns_400_not_500()
    {
        var client = _factory.CreateClient();
        var content = new StringContent("{\"email\":\"a@b.com\", \"password\":", Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync("/api/auth/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
