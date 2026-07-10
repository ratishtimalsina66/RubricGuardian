using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RubricGuardian.Web.Models;

public class User
{
    public int UserId { get; set; }
    [MaxLength(150)] public string FullName { get; set; } = "";
    [MaxLength(255)] public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Course> Courses { get; set; } = new();
}

public class Course
{
    public int CourseId { get; set; }
    public int UserId { get; set; }
    [MaxLength(200)] public string CourseName { get; set; } = "";
    [MaxLength(50)] public string Semester { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public List<Assignment> Assignments { get; set; } = new();
}

public class Assignment
{
    public int AssignmentId { get; set; }
    public int CourseId { get; set; }
    [MaxLength(250)] public string Title { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Course? Course { get; set; }
    public List<Document> Documents { get; set; } = new();
    public List<Requirement> Requirements { get; set; } = new();
    public List<Submission> Submissions { get; set; } = new();
}

public enum DocumentType { Instructions, Rubric, Submission }

public class Document
{
    public int DocumentId { get; set; }
    public int AssignmentId { get; set; }
    public int? SubmissionId { get; set; }
    public DocumentType DocumentType { get; set; }
    [MaxLength(260)] public string FileName { get; set; } = "";
    [MaxLength(500)] public string FilePath { get; set; } = "";
    public string? ExtractedText { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Assignment? Assignment { get; set; }
    public Submission? Submission { get; set; }
}

public class Requirement
{
    public int RequirementId { get; set; }
    public int AssignmentId { get; set; }
    public string RequirementText { get; set; } = "";
    [MaxLength(100)] public string Category { get; set; } = "General";
    [Column(TypeName = "decimal(8,2)")] public decimal? Points { get; set; }
    public bool IsRequired { get; set; } = true;
    public int? SourceDocumentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Assignment? Assignment { get; set; }
    public Document? SourceDocument { get; set; }
    public List<Evaluation> Evaluations { get; set; } = new();
}

public class Submission
{
    public int SubmissionId { get; set; }
    public int AssignmentId { get; set; }
    public int VersionNumber { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Assignment? Assignment { get; set; }
    public List<Document> Documents { get; set; } = new();
    public List<Evaluation> Evaluations { get; set; } = new();
}

public enum EvaluationStatus { Complete, Partial, Missing, Unclear }
public enum RiskLevel { Low, Medium, High }

public class Evaluation
{
    public int EvaluationId { get; set; }
    public int SubmissionId { get; set; }
    public int RequirementId { get; set; }
    public EvaluationStatus Status { get; set; }
    public string? EvidenceText { get; set; }
    [Column(TypeName = "decimal(4,3)")] public decimal ConfidenceScore { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public string? Feedback { get; set; }
    public string? FixSuggestion { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Submission? Submission { get; set; }
    public Requirement? Requirement { get; set; }
}
