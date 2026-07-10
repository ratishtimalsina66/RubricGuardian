using Microsoft.EntityFrameworkCore;
using RubricGuardian.Web.Models;
using RubricGuardian.Web.Services;

namespace RubricGuardian.Web.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher)
    {
        await db.Database.EnsureCreatedAsync();
        if (await db.Users.AnyAsync()) return;

        var user = new User
        {
            FullName = "Demo Student",
            Email = "demo@rubricguardian.dev",
            PasswordHash = hasher.Hash("Demo123!")
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var course = new Course { UserId = user.UserId, CourseName = "CSET 3250 - Client-Side Scripting", Semester = "Fall 2026" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        var assignment = new Assignment
        {
            CourseId = course.CourseId,
            Title = "Project 2: Interactive Travel Journal",
            DueDate = DateTime.UtcNow.AddDays(14),
            Description = "Build a single-page travel journal app using vanilla JavaScript, localStorage, and responsive CSS."
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var rubricDoc = new Document
        {
            AssignmentId = assignment.AssignmentId,
            DocumentType = DocumentType.Rubric,
            FileName = "project2-rubric.txt",
            FilePath = "seed/project2-rubric.txt",
            ExtractedText = "Rubric: 1) App must allow adding a journal entry with title, date, and description (20 pts). " +
                            "2) Entries must persist using localStorage (20 pts). 3) Layout must be responsive on mobile (15 pts). " +
                            "4) Code must include at least three reusable functions (15 pts). 5) Include a README with setup steps (10 pts). " +
                            "6) Optional: photo upload support (bonus 5 pts)."
        };
        db.Documents.Add(rubricDoc);
        await db.SaveChangesAsync();

        var reqs = new List<Requirement>
        {
            new() { AssignmentId = assignment.AssignmentId, RequirementText = "App allows adding a journal entry with title, date, and description.", Category = "Functionality", Points = 20, IsRequired = true, SourceDocumentId = rubricDoc.DocumentId },
            new() { AssignmentId = assignment.AssignmentId, RequirementText = "Entries persist across page reloads using localStorage.", Category = "Functionality", Points = 20, IsRequired = true, SourceDocumentId = rubricDoc.DocumentId },
            new() { AssignmentId = assignment.AssignmentId, RequirementText = "Layout is responsive on mobile screen sizes.", Category = "Design", Points = 15, IsRequired = true, SourceDocumentId = rubricDoc.DocumentId },
            new() { AssignmentId = assignment.AssignmentId, RequirementText = "Code includes at least three reusable functions.", Category = "Code Quality", Points = 15, IsRequired = true, SourceDocumentId = rubricDoc.DocumentId },
            new() { AssignmentId = assignment.AssignmentId, RequirementText = "README file with setup steps is included.", Category = "Documentation", Points = 10, IsRequired = true, SourceDocumentId = rubricDoc.DocumentId },
            new() { AssignmentId = assignment.AssignmentId, RequirementText = "Photo upload support (bonus).", Category = "Bonus", Points = 5, IsRequired = false, SourceDocumentId = rubricDoc.DocumentId },
        };
        db.Requirements.AddRange(reqs);

        var submission = new Submission { AssignmentId = assignment.AssignmentId, VersionNumber = 1 };
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();

        db.Documents.Add(new Document
        {
            AssignmentId = assignment.AssignmentId,
            SubmissionId = submission.SubmissionId,
            DocumentType = DocumentType.Submission,
            FileName = "travel-journal-v1.txt",
            FilePath = "seed/travel-journal-v1.txt",
            ExtractedText = "Submission summary: The app lets users add entries with a title and description via a form. " +
                            "Entries are stored in a JavaScript array. CSS uses flexbox with a media query at 600px. " +
                            "Functions: addEntry(), renderEntries(), formatDate()."
        });

        var evals = new List<Evaluation>
        {
            new() { SubmissionId = submission.SubmissionId, RequirementId = reqs[0].RequirementId, Status = EvaluationStatus.Partial, EvidenceText = "The app lets users add entries with a title and description via a form.", ConfidenceScore = 0.85m, RiskLevel = RiskLevel.Medium, Feedback = "Title and description fields exist, but no date field was found.", FixSuggestion = "Add a date input to the entry form and include it when saving and rendering entries." },
            new() { SubmissionId = submission.SubmissionId, RequirementId = reqs[1].RequirementId, Status = EvaluationStatus.Missing, EvidenceText = null, ConfidenceScore = 0.9m, RiskLevel = RiskLevel.High, Feedback = "Entries are only stored in an in-memory array; no localStorage usage found.", FixSuggestion = "Save the entries array with localStorage.setItem on every change and load it with getItem on page load." },
            new() { SubmissionId = submission.SubmissionId, RequirementId = reqs[2].RequirementId, Status = EvaluationStatus.Complete, EvidenceText = "CSS uses flexbox with a media query at 600px.", ConfidenceScore = 0.8m, RiskLevel = RiskLevel.Low, Feedback = "A mobile breakpoint is present.", FixSuggestion = "Verify the layout at 375px width to confirm nothing overflows." },
            new() { SubmissionId = submission.SubmissionId, RequirementId = reqs[3].RequirementId, Status = EvaluationStatus.Complete, EvidenceText = "Functions: addEntry(), renderEntries(), formatDate().", ConfidenceScore = 0.9m, RiskLevel = RiskLevel.Low, Feedback = "Three named reusable functions were found.", FixSuggestion = "No change needed." },
            new() { SubmissionId = submission.SubmissionId, RequirementId = reqs[4].RequirementId, Status = EvaluationStatus.Missing, EvidenceText = null, ConfidenceScore = 0.85m, RiskLevel = RiskLevel.High, Feedback = "No README or setup instructions were mentioned in the submission.", FixSuggestion = "Add a README.md with a short description and the steps to open/run the app." },
            new() { SubmissionId = submission.SubmissionId, RequirementId = reqs[5].RequirementId, Status = EvaluationStatus.Missing, EvidenceText = null, ConfidenceScore = 0.9m, RiskLevel = RiskLevel.Low, Feedback = "Optional bonus item; no photo upload found.", FixSuggestion = "Optional: add an <input type=\"file\"> and store image data URLs with each entry." },
        };
        db.Evaluations.AddRange(evals);
        await db.SaveChangesAsync();
    }
}
