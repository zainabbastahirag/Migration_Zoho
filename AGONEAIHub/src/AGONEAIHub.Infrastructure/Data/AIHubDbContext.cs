using AGONEAIHub.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AGONEAIHub.Infrastructure.Data;

public class AIHubDbContext : DbContext
{
    public AIHubDbContext(DbContextOptions<AIHubDbContext> options) : base(options) { }

    public DbSet<PromptTemplate> PromptTemplates { get; set; } = null!;
    public DbSet<PromptExecutionLog> PromptExecutionLogs { get; set; } = null!;
    public DbSet<DocumentProcessingJob> DocumentProcessingJobs { get; set; } = null!;
    public DbSet<SearchIndexConfig> SearchIndexConfigs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        mb.Entity<PromptTemplate>(e =>
        {
            e.ToTable("PromptTemplates");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Project, x.PromptKey }).IsUnique();
            e.Property(x => x.Project).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.PromptKey).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(500);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.Category).HasMaxLength(200);
        });

        mb.Entity<PromptExecutionLog>(e =>
        {
            e.ToTable("PromptExecutionLogs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Project);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Project).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.PromptKey).HasMaxLength(200);
            e.Property(x => x.Model).HasMaxLength(100);
        });

        mb.Entity<DocumentProcessingJob>(e =>
        {
            e.ToTable("DocumentProcessingJobs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Project);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Project).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.ModelId).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(50);
        });

        mb.Entity<SearchIndexConfig>(e =>
        {
            e.ToTable("SearchIndexConfigs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Project, x.IndexName }).IsUnique();
            e.Property(x => x.Project).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.IndexName).HasMaxLength(200);
        });
    }
}
