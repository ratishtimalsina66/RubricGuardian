using System.Net;
using RubricGuardian.Web.Services;
using Xunit;

namespace RubricGuardian.Web.Tests;

public class AiServiceErrorMessagesTests
{
    [Fact]
    public void Unauthorized_MentionsCredentials()
    {
        var msg = AiServiceErrorMessages.For(new AiServiceException(HttpStatusCode.Unauthorized, "bad key"));
        Assert.Contains("credentials", msg);
    }

    [Fact]
    public void TooManyRequests_WithNullDetail_FallsBackToGenericRateLimitMessage()
    {
        var msg = AiServiceErrorMessages.For(new AiServiceException(HttpStatusCode.TooManyRequests, null));
        Assert.Contains("rate-limiting", msg);
    }

    [Fact]
    public void TooManyRequests_WithDetail_UsesTheMoreSpecificDetail()
    {
        // e.g. ai-service distinguishes "insufficient_quota" from a transient rate limit,
        // even though both map to HTTP 429 - the specific detail must not be discarded.
        var msg = AiServiceErrorMessages.For(new AiServiceException(
            HttpStatusCode.TooManyRequests,
            "The AI provider account has run out of quota/credits. Check the OpenAI plan and billing details for this API key."));
        Assert.Contains("quota/credits", msg);
    }

    [Fact]
    public void GatewayTimeout_MentionsTooLong()
    {
        var msg = AiServiceErrorMessages.For(new AiServiceException(HttpStatusCode.GatewayTimeout, null));
        Assert.Contains("took too long", msg);
    }

    [Fact]
    public void BadGateway_IncludesUpstreamDetail()
    {
        var msg = AiServiceErrorMessages.For(new AiServiceException(HttpStatusCode.BadGateway, "OPENAI_API_KEY is invalid"));
        Assert.Contains("OPENAI_API_KEY is invalid", msg);
    }

    [Fact]
    public void BadGateway_WithNullDetail_FallsBackToGenericPhrase()
    {
        var msg = AiServiceErrorMessages.For(new AiServiceException(HttpStatusCode.BadGateway, null));
        Assert.Contains("upstream error occurred", msg);
    }

    [Fact]
    public void BadRequest_UsesDetailVerbatim()
    {
        var msg = AiServiceErrorMessages.For(new AiServiceException(HttpStatusCode.BadRequest, "Unsupported file type '.zip'."));
        Assert.Equal("Unsupported file type '.zip'.", msg);
    }

    [Fact]
    public void UnknownStatus_FallsBackToGenericMessage()
    {
        var msg = AiServiceErrorMessages.For(new AiServiceException(HttpStatusCode.InternalServerError, "boom"));
        Assert.Contains("unexpected error", msg);
    }
}
