using Microsoft.EntityFrameworkCore;
using RubricGuardian.Web.Models;

namespace RubricGuardian.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();

        b.Entity<Course>()
            .HasOne(c => c.User).WithMany(u => u.Courses)
            .HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Assignment>()
            .HasOne(a => a.Course).WithMany(c => c.Assignments)
            .HasForeignKey(a => a.CourseId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Document>()
            .HasOne(d => d.Assignment).WithMany(a => a.Documents)
            .HasForeignKey(d => d.AssignmentId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Document>()
            .HasOne(d => d.Submission).WithMany(s => s.Documents)
            .HasForeignKey(d => d.SubmissionId).OnDelete(DeleteBehavior.NoAction);

        b.Entity<Document>().Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(20);

        b.Entity<Requirement>()
            .HasOne(r => r.Assignment).WithMany(a => a.Requirements)
            .HasForeignKey(r => r.AssignmentId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Requirement>()
            .HasOne(r => r.SourceDocument).WithMany()
            .HasForeignKey(r => r.SourceDocumentId).OnDelete(DeleteBehavior.NoAction);

        b.Entity<Submission>()
            .HasOne(s => s.Assignment).WithMany(a => a.Submissions)
            .HasForeignKey(s => s.AssignmentId).OnDelete(DeleteBehavior.NoAction);

        b.Entity<Submission>().HasIndex(s => new { s.AssignmentId, s.VersionNumber }).IsUnique();

        b.Entity<Evaluation>()
            .HasOne(e => e.Submission).WithMany(s => s.Evaluations)
            .HasForeignKey(e => e.SubmissionId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Evaluation>()
            .HasOne(e => e.Requirement).WithMany(r => r.Evaluations)
            .HasForeignKey(e => e.RequirementId).OnDelete(DeleteBehavior.NoAction);

        b.Entity<Evaluation>().Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Entity<Evaluation>().Property(e => e.RiskLevel).HasConversion<string>().HasMaxLength(20);
    }
}
