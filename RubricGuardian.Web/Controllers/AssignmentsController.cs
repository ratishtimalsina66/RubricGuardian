using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RubricGuardian.Web.Data;
using RubricGuardian.Web.Models;
using RubricGuardian.Web.Models.ViewModels;
using RubricGuardian.Web.Services;

namespace RubricGuardian.Web.Controllers;

[Authorize]
public class AssignmentsController : AppControllerBase
{
    private static readonly string[] AllowedExtensions = { ".pdf", ".docx", ".txt", ".md" };
    private const long MaxFileBytes = 20 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly AssignmentWorkflowService _workflow;

    public AssignmentsController(AppDbContext db, AssignmentWorkflowService workflow)
    {
        _db = db; _workflow = workflow;
    }

    // ----------------------------------------------------------------- create
    [HttpGet("/assignments/create")]
    public async Task<IActionResult> Create(int? courseId)
        => View(new AssignmentCreateViewModel
        {
            CourseId = courseId ?? 0,
            Courses = await MyCourses()
        });

    [HttpPost("/assignments/create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AssignmentCreateViewModel vm)
    {
        vm.Courses = await MyCourses();
        if (!vm.Courses.Any(c => c.CourseId == vm.CourseId))
            ModelState.AddModelError(nameof(vm.CourseId), "Choose one of your courses.");
        if (!ModelState.IsValid) return View(vm);

        var assignment = new Assignment
        {
            CourseId = vm.CourseId,
            Title = vm.Title.Trim(),
            DueDate = vm.DueDate,
            Description = vm.Description?.Trim()
        };
        _db.Assignments.Add(assignment);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Assignment created. Next, upload the instructions or rubric.";
        return Redirect($"/assignments/{assignment.AssignmentId}");
    }

    // ---------------------------------------------------------------- details
    [HttpGet("/assignments/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var assignment = await OwnedAssignment(id)
            .Include(a => a.Course)
            .Include(a => a.Documents)
            .Include(a => a.Requirements)
            .Include(a => a.Submissions).ThenInclude(s => s.Evaluations)
            .FirstOrDefaultAsync();
        if (assignment is null) return NotFound();

        double? readiness = null;
        var latest = assignment.Submissions.OrderByDescending(s => s.VersionNumber).FirstOrDefault();
        var required = assignment.Requirements.Where(r => r.IsRequired).ToList();
        if (latest is not null && required.Count > 0)
        {
            var byReq = latest.Evaluations.ToDictionary(e => e.RequirementId);
            double score = required.Sum(r => byReq.TryGetValue(r.RequirementId, out var e)
                ? e.Status switch { EvaluationStatus.Complete => 1.0, EvaluationStatus.Partial => 0.5, _ => 0.0 }
                : 0.0);
            readiness = Math.Round(score / required.Count * 100, 1);
        }

        return View(new AssignmentDetailViewModel
        {
            Assignment = assignment,
            Documents = assignment.Documents.OrderByDescending(d => d.UploadedAt).ToList(),
            Requirements = assignment.Requirements.OrderBy(r => r.RequirementId).ToList(),
            Submissions = assignment.Submissions.OrderByDescending(s => s.VersionNumber).ToList(),
            LatestReadiness = readiness
        });
    }

    // ------------------------------------------------- upload instructions/rubric
    [HttpGet("/assignments/{id:int}/upload-instructions")]
    public async Task<IActionResult> UploadInstructions(int id)
    {
        var assignment = await OwnedAssignment(id).Include(a => a.Course).FirstOrDefaultAsync();
        return assignment is null ? NotFound() : View(assignment);
    }

    [HttpPost("/assignments/{id:int}/upload-instructions"), ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxFileBytes)]
    public async Task<IActionResult> UploadInstructions(int id, IFormFile? file, string documentType)
    {
        var assignment = await OwnedAssignment(id).Include(a => a.Course).FirstOrDefaultAsync();
        if (assignment is null) return NotFound();

        var type = documentType == "Rubric" ? DocumentType.Rubric : DocumentType.Instructions;
        if (!ValidateUpload(file, out var error))
        {
            ModelState.AddModelError("", error!);
            return View(assignment);
        }

        try
        {
            var count = await _workflow.IngestInstructionDocumentAsync(id, file!, type);
            TempData["Success"] = $"{type} uploaded. {count} requirement(s) extracted.";
        }
        catch (HttpRequestException)
        {
            TempData["Error"] = "The AI service is unreachable. The file was not processed - check that the FastAPI service is running.";
        }
        return Redirect($"/assignments/{id}");
    }

    // ------------------------------------------------------- upload submission
    [HttpGet("/assignments/{id:int}/upload-submission")]
    public async Task<IActionResult> UploadSubmission(int id)
    {
        var assignment = await OwnedAssignment(id)
            .Include(a => a.Course)
            .Include(a => a.Requirements)
            .Include(a => a.Submissions)
            .FirstOrDefaultAsync();
        return assignment is null ? NotFound() : View(assignment);
    }

    [HttpPost("/assignments/{id:int}/upload-submission"), ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxFileBytes)]
    public async Task<IActionResult> UploadSubmission(int id, IFormFile? file)
    {
        var assignment = await OwnedAssignment(id)
            .Include(a => a.Course).Include(a => a.Requirements).Include(a => a.Submissions)
            .FirstOrDefaultAsync();
        if (assignment is null) return NotFound();

        if (!assignment.Requirements.Any())
        {
            TempData["Error"] = "Upload instructions or a rubric first so requirements exist to check against.";
            return Redirect($"/assignments/{id}/upload-instructions");
        }
        if (!ValidateUpload(file, out var error))
        {
            ModelState.AddModelError("", error!);
            return View(assignment);
        }

        try
        {
            var submission = await _workflow.IngestSubmissionAsync(id, file!);
            TempData["Success"] = $"Version {submission.VersionNumber} uploaded and evaluated.";
            return Redirect($"/assignments/{id}/evaluation?version={submission.VersionNumber}");
        }
        catch (HttpRequestException)
        {
            TempData["Error"] = "The AI service is unreachable. Check that the FastAPI service is running, then try again.";
            return Redirect($"/assignments/{id}");
        }
    }

    // --------------------------------------------------------------- evaluation
    [HttpGet("/assignments/{id:int}/evaluation")]
    public async Task<IActionResult> Evaluation(int id, int? version)
    {
        var assignment = await OwnedAssignment(id)
            .Include(a => a.Course)
            .Include(a => a.Requirements)
            .Include(a => a.Submissions).ThenInclude(s => s.Evaluations)
            .FirstOrDefaultAsync();
        if (assignment is null) return NotFound();

        var versions = assignment.Submissions.OrderByDescending(s => s.VersionNumber).ToList();
        var submission = version.HasValue
            ? versions.FirstOrDefault(s => s.VersionNumber == version.Value)
            : versions.FirstOrDefault();

        var evalByReq = submission?.Evaluations.ToDictionary(e => e.RequirementId) ?? new();

        // Prioritize: required high-risk missing first, then by severity
        var rows = assignment.Requirements
            .Select(r => new EvaluationRow { Requirement = r, Evaluation = evalByReq.GetValueOrDefault(r.RequirementId) })
            .OrderByDescending(r => r.Requirement.IsRequired)
            .ThenByDescending(r => r.Evaluation?.RiskLevel ?? RiskLevel.Medium)
            .ThenByDescending(r => r.Evaluation?.Status switch
            {
                EvaluationStatus.Missing => 3,
                EvaluationStatus.Unclear => 2,
                EvaluationStatus.Partial => 1,
                _ => 0
            })
            .ToList();

        return View(new EvaluationDashboardViewModel
        {
            Assignment = assignment,
            Submission = submission,
            AllVersions = versions,
            Rows = rows
        });
    }

    [HttpPost("/assignments/{id:int}/evaluation/rerun"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Rerun(int id, int submissionId)
    {
        var owned = await OwnedAssignment(id).AnyAsync();
        var submission = await _db.Submissions.FirstOrDefaultAsync(s => s.SubmissionId == submissionId && s.AssignmentId == id);
        if (!owned || submission is null) return NotFound();

        try
        {
            await _workflow.ReevaluateAsync(submissionId);
            TempData["Success"] = $"Version {submission.VersionNumber} re-evaluated.";
        }
        catch (HttpRequestException)
        {
            TempData["Error"] = "The AI service is unreachable. Check that the FastAPI service is running.";
        }
        return Redirect($"/assignments/{id}/evaluation?version={submission.VersionNumber}");
    }

    // ------------------------------------------------------------------ helpers
    private IQueryable<Assignment> OwnedAssignment(int id)
        => _db.Assignments.Where(a => a.AssignmentId == id && a.Course!.UserId == CurrentUserId);

    private Task<List<Course>> MyCourses()
        => _db.Courses.Where(c => c.UserId == CurrentUserId).OrderBy(c => c.CourseName).ToListAsync();

    private static bool ValidateUpload(IFormFile? file, out string? error)
    {
        error = null;
        if (file is null || file.Length == 0) { error = "Choose a file to upload."; return false; }
        if (file.Length > MaxFileBytes) { error = "File is larger than 20 MB."; return false; }
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
        {
            error = $"Unsupported file type '{ext}'. Use PDF, DOCX, TXT, or MD.";
            return false;
        }
        return true;
    }
}
