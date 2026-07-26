using System.Net;

namespace RubricGuardian.Web.Services;

/// <summary>
/// Turns an <see cref="AiServiceException"/> into an accurate, user-facing message.
/// Kept separate from AssignmentsController so the mapping is unit-testable without
/// any MVC scaffolding.
/// </summary>
public static class AiServiceErrorMessages
{
    public static string For(AiServiceException ex) => ex.StatusCode switch
    {
        HttpStatusCode.Unauthorized => "The AI service rejected the request's credentials. Contact your administrator (AI_SERVICE_API_KEY misconfigured).",
        HttpStatusCode.TooManyRequests => ex.Detail ?? "The AI provider is rate-limiting requests right now. Wait a moment and try again.",
        HttpStatusCode.GatewayTimeout => "The AI provider took too long to respond. Try again in a moment.",
        HttpStatusCode.BadGateway => $"The AI service could not complete the request: {ex.Detail ?? "an upstream error occurred"}.",
        HttpStatusCode.BadRequest => ex.Detail ?? "The document could not be processed.",
        _ => "The AI service returned an unexpected error. Check the logs for details."
    };
}
