-- RubricGuardian - SQL Server schema
-- Optional: the ASP.NET app also creates this schema automatically on first run
-- via EnsureCreated(). Use this script if you prefer to create the database manually.

IF DB_ID('RubricGuardian') IS NULL
    CREATE DATABASE RubricGuardian;
GO
USE RubricGuardian;
GO

CREATE TABLE Users (
    UserId       INT IDENTITY(1,1) PRIMARY KEY,
    FullName     NVARCHAR(150) NOT NULL,
    Email        NVARCHAR(255) NOT NULL,
    PasswordHash NVARCHAR(MAX) NOT NULL,
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Users_Email UNIQUE (Email)
);

CREATE TABLE Courses (
    CourseId   INT IDENTITY(1,1) PRIMARY KEY,
    UserId     INT NOT NULL,
    CourseName NVARCHAR(200) NOT NULL,
    Semester   NVARCHAR(50) NOT NULL,
    CreatedAt  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Courses_Users FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE
);

CREATE TABLE Assignments (
    AssignmentId INT IDENTITY(1,1) PRIMARY KEY,
    CourseId     INT NOT NULL,
    Title        NVARCHAR(250) NOT NULL,
    DueDate      DATETIME2 NULL,
    Description  NVARCHAR(MAX) NULL,
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Assignments_Courses FOREIGN KEY (CourseId) REFERENCES Courses(CourseId) ON DELETE CASCADE
);

CREATE TABLE Submissions (
    SubmissionId  INT IDENTITY(1,1) PRIMARY KEY,
    AssignmentId  INT NOT NULL,
    VersionNumber INT NOT NULL,
    UploadedAt    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Submissions_Assignments FOREIGN KEY (AssignmentId) REFERENCES Assignments(AssignmentId),
    CONSTRAINT UQ_Submissions_Version UNIQUE (AssignmentId, VersionNumber)
);

CREATE TABLE Documents (
    DocumentId    INT IDENTITY(1,1) PRIMARY KEY,
    AssignmentId  INT NOT NULL,
    SubmissionId  INT NULL,
    DocumentType  NVARCHAR(20) NOT NULL,   -- Instructions | Rubric | Submission
    FileName      NVARCHAR(260) NOT NULL,
    FilePath      NVARCHAR(500) NOT NULL,
    ExtractedText NVARCHAR(MAX) NULL,
    UploadedAt    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Documents_Assignments FOREIGN KEY (AssignmentId) REFERENCES Assignments(AssignmentId) ON DELETE CASCADE,
    CONSTRAINT FK_Documents_Submissions FOREIGN KEY (SubmissionId) REFERENCES Submissions(SubmissionId),
    CONSTRAINT CK_Documents_Type CHECK (DocumentType IN ('Instructions','Rubric','Submission'))
);

CREATE TABLE Requirements (
    RequirementId    INT IDENTITY(1,1) PRIMARY KEY,
    AssignmentId     INT NOT NULL,
    RequirementText  NVARCHAR(MAX) NOT NULL,
    Category         NVARCHAR(100) NOT NULL DEFAULT 'General',
    Points           DECIMAL(8,2) NULL,
    IsRequired       BIT NOT NULL DEFAULT 1,
    SourceDocumentId INT NULL,
    CreatedAt        DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Requirements_Assignments FOREIGN KEY (AssignmentId) REFERENCES Assignments(AssignmentId) ON DELETE CASCADE,
    CONSTRAINT FK_Requirements_Documents FOREIGN KEY (SourceDocumentId) REFERENCES Documents(DocumentId)
);

CREATE TABLE Evaluations (
    EvaluationId    INT IDENTITY(1,1) PRIMARY KEY,
    SubmissionId    INT NOT NULL,
    RequirementId   INT NOT NULL,
    Status          NVARCHAR(20) NOT NULL,   -- Complete | Partial | Missing | Unclear
    EvidenceText    NVARCHAR(MAX) NULL,
    ConfidenceScore DECIMAL(4,3) NOT NULL DEFAULT 0,
    RiskLevel       NVARCHAR(20) NOT NULL,   -- Low | Medium | High
    Feedback        NVARCHAR(MAX) NULL,
    FixSuggestion   NVARCHAR(MAX) NULL,
    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Evaluations_Submissions FOREIGN KEY (SubmissionId) REFERENCES Submissions(SubmissionId) ON DELETE CASCADE,
    CONSTRAINT FK_Evaluations_Requirements FOREIGN KEY (RequirementId) REFERENCES Requirements(RequirementId),
    CONSTRAINT CK_Evaluations_Status CHECK (Status IN ('Complete','Partial','Missing','Unclear')),
    CONSTRAINT CK_Evaluations_Risk CHECK (RiskLevel IN ('Low','Medium','High'))
);

CREATE INDEX IX_Courses_UserId ON Courses(UserId);
CREATE INDEX IX_Assignments_CourseId ON Assignments(CourseId);
CREATE INDEX IX_Documents_AssignmentId ON Documents(AssignmentId);
CREATE INDEX IX_Requirements_AssignmentId ON Requirements(AssignmentId);
CREATE INDEX IX_Submissions_AssignmentId ON Submissions(AssignmentId);
CREATE INDEX IX_Evaluations_SubmissionId ON Evaluations(SubmissionId);
GO
