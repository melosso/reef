using System.Net;
using FluentAssertions;
using Reef.Core.Destinations;
using Reef.Tests.Templates;
using Xunit;

namespace Reef.Tests.Destinations;

public class EmailApiClientTests
{
    [Fact]
    public async Task UnavailableProviderIsRetriedThreeTimes()
    {
        using var api = new MockHttpServer();
        api.SetResponse(503, "busy");

        using var response = await EmailApiClient.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, api.BaseUrl + "send"), 10, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        api.RequestCount.Should().Be(4);
    }

    [Fact]
    public async Task RejectedRequestIsNotRetried()
    {
        using var api = new MockHttpServer();
        api.SetResponse(400, "bad request");

        using var response = await EmailApiClient.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, api.BaseUrl + "send"), 10, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        api.RequestCount.Should().Be(1);
    }
}
