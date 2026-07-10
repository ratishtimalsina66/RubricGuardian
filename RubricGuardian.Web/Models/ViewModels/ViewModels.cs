using System.ComponentModel.DataAnnotations;

namespace RubricGuardian.Web.Models.ViewModels;

public class LoginViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required, MaxLength(150)] public string FullName { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, MinLength(8), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

public class CourseCreateViewModel
{
    [Required, MaxLength(200)] public string CourseName { get; set; } = "";
    [Required, MaxLength(50)] public string Semester { get; set; } = "";
}

public class AssignmentCreateViewModel
{
    [Required] public int CourseId { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = "";
    [DataType(DataType.Date)] public DateTime? DueDate { get; set; }
    public string? Description { get; set; }
    public List<Course> Courses { get; set; } = new();
}

public class DashboardViewModel
{
    public string FullName { get; set; } = "";
    public int CourseCount { get; set; }
    public int AssignmentCount { get; set; }
    public int SubmissionCount { get; set; }
    public List<AssignmentSummary> RecentAssignments { get; set; } = new();
}

public class AssignmentSummary
{
    public int AssignmentId { get; set; }
    public string Title { get; set; } = "";
    public string CourseName { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public int RequirementCount { get; set; }
    public int? LatestVersion { get; set; }
    public double? ReadinessPercent { get; set; }
}

public class AssignmentDetailViewModel
{
    public Assignment Assignment { get; set; } = null!;
    public List<Document> Documents { get; set; } = new();
    public List<Requirement> Requirements { get; set; } = new();
    public List<Submission> Submissions { get; set; } = new();
    public double? LatestReadiness { get; set; }
}

public class EvaluationRow
{
    public Requirement Requirement { get; set; } = null!;
    public Evaluation? Evaluation { get; set; }
}

public class EvaluationDashboardViewModel
{
    public Assignment Assignment { get; set; } = null!;
    public Submission? Submission { get; set; }
    public List<Submission> AllVersions { get; set; } = new();
    public List<EvaluationRow> Rows { get; set; } = new();

    public int CompleteCount => Rows.Count(r => r.Evaluation?.Status == EvaluationStatus.Complete);
    public int PartialCount => Rows.Count(r => r.Evaluation?.Status == EvaluationStatus.Partial);
    public int MissingCount => Rows.Count(r => r.Evaluation?.Status == EvaluationStatus.Missing);
    public int UnclearCount => Rows.Count(r => r.Evaluation?.Status == EvaluationStatus.Unclear);

    /// <summary>Complete = 1.0, Partial = 0.5, Missing/Unclear/not evaluated = 0, over required items.</summary>
    public double ReadinessPercent
    {
        get
        {
            var required = Rows.Where(r => r.Requirement.IsRequired).ToList();
            if (required.Count == 0) return 0;
            double score = required.Sum(r => r.Evaluation?.Status switch
            {
                EvaluationStatus.Complete => 1.0,
                EvaluationStatus.Partial => 0.5,
                _ => 0.0
            });
            return Math.Round(score / required.Count * 100, 1);
        }
    }
}
