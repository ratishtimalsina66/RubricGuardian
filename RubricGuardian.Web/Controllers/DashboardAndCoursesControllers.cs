using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RubricGuardian.Web.Data;
using RubricGuardian.Web.Models;
using RubricGuardian.Web.Models.ViewModels;

namespace RubricGuardian.Web.Controllers;

public abstract class AppControllerBase : Controller
{
    protected int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

[Authorize]
public class DashboardController : AppControllerBase
{
    private readonly AppDbContext _db;
    public DashboardController(AppDbContext db) => _db = db;

    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [HttpGet("/")]
    public IActionResult Root() => Redirect(User.Identity?.IsAuthenticated == true ? "/dashboard" : "/login");

    [HttpGet("/dashboard")]
    public async Task<IActionResult> Index()
    {
        var userId = CurrentUserId;

        var assignments = await _db.Assignments
            .Where(a => a.Course!.UserId == userId)
            .Include(a => a.Course)
            .Include(a => a.Requirements)
            .Include(a => a.Submissions).ThenInclude(s => s.Evaluations)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var vm = new DashboardViewModel
        {
            FullName = User.Identity?.Name ?? "",
            CourseCount = await _db.Courses.CountAsync(c => c.UserId == userId),
            AssignmentCount = assignments.Count,
            SubmissionCount = assignments.Sum(a => a.Submissions.Count),
            RecentAssignments = assignments.Take(8).Select(a =>
            {
                var latest = a.Submissions.OrderByDescending(s => s.VersionNumber).FirstOrDefault();
                double? readiness = null;
                if (latest is not null)
                {
                    var required = a.Requirements.Where(r => r.IsRequired).ToList();
                    if (required.Count > 0)
                    {
                        var byReq = latest.Evaluations.ToDictionary(e => e.RequirementId);
                        double score = required.Sum(r => byReq.TryGetValue(r.RequirementId, out var e)
                            ? e.Status switch { EvaluationStatus.Complete => 1.0, EvaluationStatus.Partial => 0.5, _ => 0.0 }
                            : 0.0);
                        readiness = Math.Round(score / required.Count * 100, 1);
                    }
                }
                return new AssignmentSummary
                {
                    AssignmentId = a.AssignmentId,
                    Title = a.Title,
                    CourseName = a.Course!.CourseName,
                    DueDate = a.DueDate,
                    RequirementCount = a.Requirements.Count,
                    LatestVersion = latest?.VersionNumber,
                    ReadinessPercent = readiness
                };
            }).ToList()
        };
        return View(vm);
    }
}

[Authorize]
public class CoursesController : AppControllerBase
{
    private readonly AppDbContext _db;
    public CoursesController(AppDbContext db) => _db = db;

    [HttpGet("/courses")]
    public async Task<IActionResult> Index()
    {
        var courses = await _db.Courses
            .Where(c => c.UserId == CurrentUserId)
            .Include(c => c.Assignments)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return View(courses);
    }

    [HttpGet("/courses/create")]
    public IActionResult Create() => View(new CourseCreateViewModel());

    [HttpPost("/courses/create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CourseCreateViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        _db.Courses.Add(new Course { UserId = CurrentUserId, CourseName = vm.CourseName.Trim(), Semester = vm.Semester.Trim() });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Course created.";
        return Redirect("/courses");
    }
}
