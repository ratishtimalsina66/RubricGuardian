-- RubricGuardian - sample seed data (SQL version)
-- Note: the ASP.NET app already seeds this same demo data automatically on first run
-- (Data/DbSeeder.cs) when Database:SeedOnStartup is true. Use this script only if
-- you created the schema manually and want the demo data without running the app seeder.
-- Demo login when using the app seeder: demo@rubricguardian.dev / Demo123!
-- (The password hash below is a placeholder; register through the app for a usable login.)

USE RubricGuardian;
GO

INSERT INTO Users (FullName, Email, PasswordHash)
VALUES ('Demo Student', 'demo-sql@rubricguardian.dev', 'REGISTER_THROUGH_APP_FOR_REAL_HASH');

DECLARE @UserId INT = SCOPE_IDENTITY();

INSERT INTO Courses (UserId, CourseName, Semester)
VALUES (@UserId, 'CSET 3250 - Client-Side Scripting', 'Fall 2026');

DECLARE @CourseId INT = SCOPE_IDENTITY();

INSERT INTO Assignments (CourseId, Title, DueDate, Description)
VALUES (@CourseId, 'Project 2: Interactive Travel Journal', DATEADD(DAY, 14, SYSUTCDATETIME()),
        'Build a single-page travel journal app using vanilla JavaScript, localStorage, and responsive CSS.');

DECLARE @AssignmentId INT = SCOPE_IDENTITY();

INSERT INTO Documents (AssignmentId, DocumentType, FileName, FilePath, ExtractedText)
VALUES (@AssignmentId, 'Rubric', 'project2-rubric.txt', 'seed/project2-rubric.txt',
        'Rubric: 1) App must allow adding a journal entry with title, date, and description (20 pts). 2) Entries must persist using localStorage (20 pts). 3) Layout must be responsive on mobile (15 pts). 4) Code must include at least three reusable functions (15 pts). 5) Include a README with setup steps (10 pts). 6) Optional: photo upload support (bonus 5 pts).');

DECLARE @RubricDocId INT = SCOPE_IDENTITY();

INSERT INTO Requirements (AssignmentId, RequirementText, Category, Points, IsRequired, SourceDocumentId) VALUES
(@AssignmentId, 'App allows adding a journal entry with title, date, and description.', 'Functionality', 20, 1, @RubricDocId),
(@AssignmentId, 'Entries persist across page reloads using localStorage.',              'Functionality', 20, 1, @RubricDocId),
(@AssignmentId, 'Layout is responsive on mobile screen sizes.',                          'Design',        15, 1, @RubricDocId),
(@AssignmentId, 'Code includes at least three reusable functions.',                      'Code Quality',  15, 1, @RubricDocId),
(@AssignmentId, 'README file with setup steps is included.',                             'Documentation', 10, 1, @RubricDocId),
(@AssignmentId, 'Photo upload support (bonus).',                                         'Bonus',          5, 0, @RubricDocId);

INSERT INTO Submissions (AssignmentId, VersionNumber) VALUES (@AssignmentId, 1);
DECLARE @SubmissionId INT = SCOPE_IDENTITY();

INSERT INTO Documents (AssignmentId, SubmissionId, DocumentType, FileName, FilePath, ExtractedText)
VALUES (@AssignmentId, @SubmissionId, 'Submission', 'travel-journal-v1.txt', 'seed/travel-journal-v1.txt',
        'Submission summary: The app lets users add entries with a title and description via a form. Entries are stored in a JavaScript array. CSS uses flexbox with a media query at 600px. Functions: addEntry(), renderEntries(), formatDate().');

INSERT INTO Evaluations (SubmissionId, RequirementId, Status, EvidenceText, ConfidenceScore, RiskLevel, Feedback, FixSuggestion)
SELECT @SubmissionId, r.RequirementId, v.Status, v.EvidenceText, v.Confidence, v.Risk, v.Feedback, v.Fix
FROM Requirements r
JOIN (VALUES
    ('App allows adding a journal entry with title, date, and description.', 'Partial',  'The app lets users add entries with a title and description via a form.', 0.85, 'Medium', 'Title and description fields exist, but no date field was found.', 'Add a date input to the entry form and include it when saving and rendering entries.'),
    ('Entries persist across page reloads using localStorage.',              'Missing',  NULL, 0.90, 'High',   'Entries are only stored in an in-memory array; no localStorage usage found.', 'Save the entries array with localStorage.setItem on every change and load it with getItem on page load.'),
    ('Layout is responsive on mobile screen sizes.',                          'Complete', 'CSS uses flexbox with a media query at 600px.', 0.80, 'Low', 'A mobile breakpoint is present.', 'Verify the layout at 375px width to confirm nothing overflows.'),
    ('Code includes at least three reusable functions.',                      'Complete', 'Functions: addEntry(), renderEntries(), formatDate().', 0.90, 'Low', 'Three named reusable functions were found.', 'No change needed.'),
    ('README file with setup steps is included.',                             'Missing',  NULL, 0.85, 'High',   'No README or setup instructions were mentioned in the submission.', 'Add a README.md with a short description and the steps to open/run the app.'),
    ('Photo upload support (bonus).',                                         'Missing',  NULL, 0.90, 'Low',    'Optional bonus item; no photo upload found.', 'Optional: add a file input and store image data URLs with each entry.')
) AS v(ReqText, Status, EvidenceText, Confidence, Risk, Feedback, Fix)
    ON v.ReqText = r.RequirementText
WHERE r.AssignmentId = @AssignmentId;
GO
