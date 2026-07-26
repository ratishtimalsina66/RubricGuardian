using System.Net;
using System.Text;
using RubricGuardian.Web.Services;
using Xunit;

namespace RubricGuardian.Web.Tests;

/// <summary>Returns a canned response for every request, regardless of URL/method.</summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _content;

    public FakeHttpMessageHandler(HttpStatusCode statusCode, string content)
    {
        _statusCode = statusCode;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

public class AiServiceClientTests
{
    private static AiServiceClient MakeClient(HttpStatusCode statusCode, string content)
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(statusCode, content))
        {
            BaseAddress = new Uri("http://test")
        };
        return new AiServiceClient(httpClient);
    }

    [Fact]
    public async Task ExtractRequirementsAsync_Success_ReturnsRequirements()
    {
        var client = MakeClient(HttpStatusCode.OK,
            """{"requirements":[{"requirement_text":"Has a title","category":"Formatting","points":null,"is_required":true}]}""");

        var result = await client.ExtractRequirementsAsync("some text", "Instructions");

        Assert.Single(result);
        Assert.Equal("Has a title", result[0].RequirementText);
    }

    [Fact]
    public async Task ExtractRequirementsAsync_BadGateway_ThrowsAiServiceExceptionWithDetail()
    {
        var client = MakeClient(HttpStatusCode.BadGateway,
            """{"detail":"The AI provider rejected the request: the configured OPENAI_API_KEY is invalid."}""");

        var ex = await Assert.ThrowsAsync<AiServiceException>(
            () => client.ExtractRequirementsAsync("some text", "Instructions"));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Contains("OPENAI_API_KEY", ex.Detail);
    }

    [Fact]
    public async Task ExtractRequirementsAsync_TooManyRequests_ThrowsWithRateLimitStatus()
    {
        var client = MakeClient(HttpStatusCode.TooManyRequests, """{"detail":"rate limited"}""");

        var ex = await Assert.ThrowsAsync<AiServiceException>(
            () => client.ExtractRequirementsAsync("some text", "Instructions"));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
    }

    [Fact]
    public async Task ExtractRequirementsAsync_GatewayTimeout_ThrowsWithTimeoutStatus()
    {
        var client = MakeClient(HttpStatusCode.GatewayTimeout, """{"detail":"timed out"}""");

        var ex = await Assert.ThrowsAsync<AiServiceException>(
            () => client.ExtractRequirementsAsync("some text", "Instructions"));

        Assert.Equal(HttpStatusCode.GatewayTimeout, ex.StatusCode);
    }

    [Fact]
    public async Task ExtractTextAsync_BadRequest_PreservesDetailInsteadOfDiscardingIt()
    {
        var client = MakeClient(HttpStatusCode.BadRequest,
            """{"detail":"Unsupported file type '.zip'. Use PDF, DOCX, TXT, or MD."}""");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));
        var ex = await Assert.ThrowsAsync<AiServiceException>(
            () => client.ExtractTextAsync(stream, "submission.zip"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("Unsupported file type '.zip'. Use PDF, DOCX, TXT, or MD.", ex.Detail);
    }

    [Fact]
    public async Task EvaluateAsync_NonJsonErrorBody_ThrowsWithNullDetail()
    {
        var client = new AiServiceClient(new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "<html>not json</html>"))
        {
            BaseAddress = new Uri("http://test")
        });

        var ex = await Assert.ThrowsAsync<AiServiceException>(
            () => client.EvaluateAsync(new List<RequirementInputDto>(), "text"));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Null(ex.Detail);
    }
}
