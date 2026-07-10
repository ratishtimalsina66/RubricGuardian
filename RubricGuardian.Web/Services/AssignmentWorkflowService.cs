using Microsoft.EntityFrameworkCore;
using RubricGuardian.Web.Data;
using RubricGuardian.Web.Models;

namespace RubricGuardian.Web.Services;

/// <summary>
/// Orchestrates the AI workflow:
///  upload -> store file -> extract text -> extract requirements / evaluate -> persist.
/// </summary>
public class AssignmentWorkflowService
{
    private readonly AppDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IAiServiceClient _ai;
    private readonly ILogger<AssignmentWorkflowService> _log;

    public AssignmentWorkflowService(AppDbContext db, IFileStorageService storage,
        IAiServiceClient ai, ILogger<AssignmentWorkflowService> log)
    {
        _db = db; _storage = storage; _ai = ai; _log = log;
    }

    /// <summary>Steps 1-3: store instructions/rubric, extract text, convert to structured requirements.</summary>
    public async Task<int> IngestInstructionDocumentAsync(int assignmentId, IFormFile file, DocumentType type, CancellationToken ct = default)
    {
        if (type == DocumentType.Submission) throw new ArgumentException("Use IngestSubmissionAsync for submissions.");

        // 1. Store the file and extract text
        var doc = await StoreAndExtractAsync(assignmentId, null, file, type, ct);

        // 2. Convert to structured requirements (only from the uploaded text - never invented)
        var extracted = await _ai.ExtractRequirementsAsync(doc.ExtractedText ?? "", type.ToString(), ct);

        // 3. Replace requirements that came from a previous upload of this document type
        var stale = await _db.Requirements
            .Include(r => r.SourceDocument)
            .Where(r => r.AssignmentId == assignmentId && r.SourceDocument != null && r.SourceDocument.DocumentType == type)
            .ToListAsync(ct);

        var staleIds = stale.Select(r => r.RequirementId).ToList();
        var staleEvals = await _db.Evaluations.Where(e => staleIds.Contains(e.RequirementId)).ToListAsync(ct);
        _db.Evaluations.RemoveRange(staleEvals);
        _db.Requirements.RemoveRange(stale);

        foreach (var r in extracted)
        {
            _db.Requirements.Add(new Requirement
            {
                AssignmentId = assignmentId,
                RequirementText = r.RequirementText,
                Category = string.IsNullOrWhiteSpace(r.Category) ? "General" : r.Category,
                Points = r.Points,
                IsRequired = r.IsRequired,
                SourceDocumentId = doc.DocumentId
            });
        }
        await _db.SaveChangesAsync(ct);
        return extracted.Count;
    }

    /// <summary>Steps 4-8: store the submission as a new version, extract text, evaluate every requirement.</summary>
    public async Task<Submission> IngestSubmissionAsync(int assignmentId, IFormFile file, CancellationToken ct = default)
    {
        var nextVersion = (await _db.Submissions
            .Where(s => s.AssignmentId == assignmentId)
            .MaxAsync(s => (int?)s.VersionNumber, ct) ?? 0) + 1;

        var submission = new Submission { AssignmentId = assignmentId, VersionNumber = nextVersion };
        _db.Submissions.Add(submission);
        await _db.SaveChangesAsync(ct);

        var doc = await StoreAndExtractAsync(assignmentId, submission.SubmissionId, file, DocumentType.Submission, ct);

        await EvaluateSubmissionAsync(submission.SubmissionId, doc.ExtractedText ?? "", ct);
        return submission;
    }

    /// <summary>Re-runs evaluation for an existing submission (e.g. after the rubric changed).</summary>
    public async Task ReevaluateAsync(int submissionId, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .Where(d => d.SubmissionId == submissionId && d.DocumentType == DocumentType.Submission)
            .OrderByDescending(d => d.UploadedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No submission document found.");

        await EvaluateSubmissionAsync(submissionId, doc.ExtractedText ?? "", ct);
    }

    private async Task EvaluateSubmissionAsync(int submissionId, string submissionText, CancellationToken ct)
    {
        var submission = await _db.Submissions.FindAsync(new object[] { submissionId }, ct)
            ?? throw new InvalidOperationException("Submission not found.");

        var requirements = await _db.Requirements
            .Where(r => r.AssignmentId == submission.AssignmentId)
            .OrderBy(r => r.RequirementId)
            .ToListAsync(ct);

        if (requirements.Count == 0)
        {
            _log.LogWarning("No requirements exist for assignment {Id}; skipping evaluation.", submission.AssignmentId);
            return;
        }

        var inputs = requirements
            .Select(r => new RequirementInputDto(r.RequirementId, r.RequirementText, r.Category, r.IsRequired))
            .ToList();

        var results = await _ai.EvaluateAsync(inputs, submissionText, ct);

        // Replace any existing evaluations for this submission (re-evaluation support)
        var old = await _db.Evaluations.Where(e => e.SubmissionId == submissionId).ToListAsync(ct);
        _db.Evaluations.RemoveRange(old);

        var validIds = requirements.Select(r => r.RequirementId).ToHashSet();
        foreach (var r in results.Where(r => validIds.Contains(r.RequirementId)))
        {
            _db.Evaluations.Add(new Evaluation
            {
                SubmissionId = submissionId,
                RequirementId = r.RequirementId,
                Status = Enum.TryParse<EvaluationStatus>(r.Status, true, out var s) ? s : EvaluationStatus.Unclear,
                EvidenceText = r.EvidenceText,
                ConfidenceScore = Math.Clamp(r.ConfidenceScore, 0m, 1m),
                RiskLevel = Enum.TryParse<RiskLevel>(r.RiskLevel, true, out var rl) ? rl : RiskLevel.Medium,
                Feedback = r.Feedback,
                FixSuggestion = r.FixSuggestion
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Document> StoreAndExtractAsync(int assignmentId, int? submissionId, IFormFile file, DocumentType type, CancellationToken ct)
    {
        string storagePath;
        await using (var upload = file.OpenReadStream())
        {
            storagePath = await _storage.SaveAsync(upload, file.FileName, $"assignment-{assignmentId}", ct);
        }

        string text;
        await using (var stored = await _storage.OpenReadAsync(storagePath, ct))
        {
            text = await _ai.ExtractTextAsync(stored, file.FileName, ct);
        }

        var doc = new Document
        {
            AssignmentId = assignmentId,
            SubmissionId = submissionId,
            DocumentType = type,
            FileName = file.FileName,
            FilePath = storagePath,
            ExtractedText = text
        };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync(ct);
        return doc;
    }
}
