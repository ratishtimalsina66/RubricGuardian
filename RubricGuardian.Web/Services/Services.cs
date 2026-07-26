using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RubricGuardian.Web.Services;

// ---------------------------------------------------------------------------
// Password hashing (PBKDF2) - no external dependencies
// ---------------------------------------------------------------------------
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16, KeySize = 32, Iterations = 600_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('.');
        if (parts.Length != 3) return false;
        var iterations = int.Parse(parts[0]);
        var salt = Convert.FromBase64String(parts[1]);
        var key = Convert.FromBase64String(parts[2]);
        var attempt = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, key.Length);
        return CryptographicOperations.FixedTimeEquals(key, attempt);
    }
}

// ---------------------------------------------------------------------------
// File storage - local for MVP, interface allows Azure Blob later
// ---------------------------------------------------------------------------
public interface IFileStorageService
{
    /// <returns>A storage path/key that is persisted on the Document row.</returns>
    Task<string> SaveAsync(Stream content, string fileName, string containerHint, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default);
}

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;

    public LocalFileStorageService(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Storage:LocalRoot"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "uploads")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string fileName, string containerHint, CancellationToken ct = default)
    {
        var safeName = $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
        var relative = Path.Combine(containerHint, safeName);
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        await content.CopyToAsync(fs, ct);
        return relative.Replace('\\', '/');
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default)
        => Task.FromResult<Stream>(File.OpenRead(Path.Combine(_root, storagePath)));
}

// ---------------------------------------------------------------------------
// AI service client (talks to the Python FastAPI service)
// ---------------------------------------------------------------------------
public record ExtractedRequirementDto(
    [property: JsonPropertyName("requirement_text")] string RequirementText,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("points")] decimal? Points,
    [property: JsonPropertyName("is_required")] bool IsRequired);

public record RequirementInputDto(
    [property: JsonPropertyName("requirement_id")] int RequirementId,
    [property: JsonPropertyName("requirement_text")] string RequirementText,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("is_required")] bool IsRequired);

public record EvaluationResultDto(
    [property: JsonPropertyName("requirement_id")] int RequirementId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("evidence_text")] string? EvidenceText,
    [property: JsonPropertyName("confidence_score")] decimal ConfidenceScore,
    [property: JsonPropertyName("risk_level")] string RiskLevel,
    [property: JsonPropertyName("feedback")] string Feedback,
    [property: JsonPropertyName("fix_suggestion")] string FixSuggestion);

/// <summary>
/// Thrown when the AI service returned an HTTP response but with a non-2xx status
/// (e.g. a 502 because its OPENAI_API_KEY is invalid, or a 429 rate limit). Distinct
/// from <see cref="HttpRequestException"/>, which now only means the AI service could
/// not be reached at all (no response was ever received).
/// </summary>
public class AiServiceException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? Detail { get; }

    public AiServiceException(HttpStatusCode statusCode, string? detail)
        : base($"AI service returned {(int)statusCode}: {detail ?? "(no detail)"}")
    {
        StatusCode = statusCode;
        Detail = detail;
    }
}

public interface IAiServiceClient
{
    Task<string> ExtractTextAsync(Stream file, string fileName, CancellationToken ct = default);
    Task<List<ExtractedRequirementDto>> ExtractRequirementsAsync(string documentText, string documentType, CancellationToken ct = default);
    Task<List<EvaluationResultDto>> EvaluateAsync(List<RequirementInputDto> requirements, string submissionText, CancellationToken ct = default);
}

public class AiServiceClient : IAiServiceClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiServiceClient(HttpClient http) => _http = http;

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        string? detail = null;
        try
        {
            var payload = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            if (payload.TryGetProperty("detail", out var d)) detail = d.GetString();
        }
        catch { /* body wasn't JSON, or had no `detail` field */ }
        throw new AiServiceException(resp.StatusCode, detail);
    }

    public async Task<string> ExtractTextAsync(Stream file, string fileName, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(file);
        form.Add(fileContent, "file", fileName);
        var resp = await _http.PostAsync("/extract-text", form, ct);
        await EnsureSuccessOrThrowAsync(resp, ct);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        return payload.GetProperty("text").GetString() ?? "";
    }

    public async Task<List<ExtractedRequirementDto>> ExtractRequirementsAsync(string documentText, string documentType, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/extract-requirements",
            new { text = documentText, document_type = documentType }, Json, ct);
        await EnsureSuccessOrThrowAsync(resp, ct);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        return payload.GetProperty("requirements").Deserialize<List<ExtractedRequirementDto>>(Json) ?? new();
    }

    public async Task<List<EvaluationResultDto>> EvaluateAsync(List<RequirementInputDto> requirements, string submissionText, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/evaluate",
            new { requirements, submission_text = submissionText }, Json, ct);
        await EnsureSuccessOrThrowAsync(resp, ct);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        return payload.GetProperty("evaluations").Deserialize<List<EvaluationResultDto>>(Json) ?? new();
    }
}
