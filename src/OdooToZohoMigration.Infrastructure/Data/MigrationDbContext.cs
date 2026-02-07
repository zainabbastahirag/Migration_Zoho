using Microsoft.EntityFrameworkCore;
using OdooToZohoMigration.Core.Entities;

namespace OdooToZohoMigration.Infrastructure.Data;

public class MigrationDbContext : DbContext
{
    public MigrationDbContext(DbContextOptions<MigrationDbContext> options) : base(options) { }

    public DbSet<Job> Jobs { get; set; } = null!;
    public DbSet<Candidate> Candidates { get; set; } = null!;
    public DbSet<Application> Applications { get; set; } = null!;
    public DbSet<ApplicationComment> ApplicationComments { get; set; } = null!;
    public DbSet<ApplicationHistory> ApplicationHistories { get; set; } = null!;
    public DbSet<ApplicationSummary> ApplicationSummaries { get; set; } = null!;
    public DbSet<Stage> Stages { get; set; } = null!;
    public DbSet<SyncLog> SyncLogs { get; set; } = null!;
    public DbSet<MigrationRun> MigrationRuns { get; set; } = null!;
    public DbSet<MigrationLog> MigrationLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // BentongDb schema for operational tables
        modelBuilder.Entity<Job>(entity =>
        {
            entity.ToTable("Jobs", "BentongDb");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OdooId).IsUnique();
            entity.HasIndex(e => e.IsMigrated);
            entity.Property(e => e.ZohoJobId).HasMaxLength(100);
        });

        modelBuilder.Entity<Candidate>(entity =>
        {
            entity.ToTable("Candidates", "BentongDb");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OdooId).IsUnique();
            entity.HasIndex(e => e.IsMigrated);
            entity.Property(e => e.ZohoCandidateId).HasMaxLength(100);
        });

        modelBuilder.Entity<Application>(entity =>
        {
            entity.ToTable("Applications", "BentongDb");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OdooId).IsUnique();
            entity.HasIndex(e => e.IsMigrated);
            entity.HasIndex(e => e.CandidateId);
            entity.HasIndex(e => e.JobId);
            entity.HasIndex(e => e.SummaryId);
            entity.Property(e => e.ZohoApplicationId).HasMaxLength(100);

            entity.HasOne(e => e.Candidate)
                .WithMany()
                .HasForeignKey(e => e.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Job)
                .WithMany()
                .HasForeignKey(e => e.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationComment>(entity =>
        {
            entity.ToTable("ApplicationComment", "BentongDb");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ApplicationId);
            entity.Property(e => e.ZohoNoteId).HasMaxLength(100);

            entity.HasOne(e => e.Application)
                .WithMany(a => a.Comments)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationHistory>(entity =>
        {
            entity.ToTable("ApplicationHistory", "BentongDb");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ApplicationId);
            entity.Property(e => e.ZohoNoteId).HasMaxLength(100);

            entity.HasOne(e => e.Application)
                .WithMany(a => a.Histories)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationSummary>(entity =>
        {
            entity.ToTable("ApplicationSummary", "BentongDb");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ZohoNoteId).HasMaxLength(100);
        });

        modelBuilder.Entity<Stage>(entity =>
        {
            entity.ToTable("Stages", "BentongDb");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OdooId).IsUnique();
        });

        modelBuilder.Entity<SyncLog>(entity =>
        {
            entity.ToTable("SyncLogs", "BentongDb");
            entity.HasKey(e => e.Id);
        });

        // dbo schema for migration tracking tables
        modelBuilder.Entity<MigrationRun>(entity =>
        {
            entity.ToTable("MigrationRuns", "dbo");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.RunType).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50);
        });

        modelBuilder.Entity<MigrationLog>(entity =>
        {
            entity.ToTable("MigrationLogs", "dbo");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.EntityType).HasMaxLength(100);
            entity.Property(e => e.ZohoId).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(50);
        });
    }
}
