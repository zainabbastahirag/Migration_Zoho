using AGONEAIHub.Core.Entities;
using AGONEAIHub.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace AGONEAIHub.Infrastructure.Data;

public class AIHubDbContext : DbContext
{
    public AIHubDbContext(DbContextOptions<AIHubDbContext> options) : base(options) { }

    public DbSet<PromptTemplate> PromptTemplates { get; set; } = null!;
    public DbSet<PromptExecutionLog> PromptExecutionLogs { get; set; } = null!;
    public DbSet<DocumentProcessingJob> DocumentProcessingJobs { get; set; } = null!;
    public DbSet<SearchIndexConfig> SearchIndexConfigs { get; set; } = null!;
    public DbSet<ApiRequestLog> ApiRequestLogs { get; set; } = null!;
    public DbSet<ErrorNotification> ErrorNotifications { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        // ── PromptTemplates ──────────────────────────────────────────
        mb.Entity<PromptTemplate>(e =>
        {
            e.ToTable("PromptTemplates", "aihub");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Project, x.Module, x.Section, x.PromptKey })
                .IsUnique().HasDatabaseName("IX_Prompt_Lookup");
            e.HasIndex(x => x.Project);
            e.HasIndex(x => new { x.Project, x.Module }).HasDatabaseName("IX_Prompt_Module");
            e.Property(x => x.Project).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.Module).HasMaxLength(100);
            e.Property(x => x.Section).HasMaxLength(100);
            e.Property(x => x.PromptKey).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(500);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.Tags).HasMaxLength(500);
        });

        // ── PromptExecutionLogs ──────────────────────────────────────
        mb.Entity<PromptExecutionLog>(e =>
        {
            e.ToTable("PromptExecutionLogs", "aihub");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Project);
            e.HasIndex(x => x.CorrelationId).HasDatabaseName("IX_ExecLog_Correlation");
            e.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_ExecLog_Date");
            e.HasIndex(x => new { x.Project, x.Module, x.Section }).HasDatabaseName("IX_ExecLog_Module");
            e.Property(x => x.Project).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.Module).HasMaxLength(100);
            e.Property(x => x.Section).HasMaxLength(100);
            e.Property(x => x.PromptKey).HasMaxLength(200);
            e.Property(x => x.Model).HasMaxLength(100);

            e.HasOne(x => x.PromptTemplate)
                .WithMany()
                .HasForeignKey(x => x.PromptTemplateId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── ApiRequestLogs ───────────────────────────────────────────
        mb.Entity<ApiRequestLog>(e =>
        {
            e.ToTable("ApiRequestLogs", "aihub");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CorrelationId).HasDatabaseName("IX_ApiLog_Correlation");
            e.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_ApiLog_Date");
            e.HasIndex(x => x.Path).HasDatabaseName("IX_ApiLog_Path");
            e.HasIndex(x => x.StatusCode).HasDatabaseName("IX_ApiLog_Status");
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.HttpMethod).HasMaxLength(10);
            e.Property(x => x.Path).HasMaxLength(500);
            e.Property(x => x.Project).HasMaxLength(50);
            e.Property(x => x.UserAgent).HasMaxLength(500);
            e.Property(x => x.ClientIp).HasMaxLength(50);
        });

        // ── ErrorNotifications ───────────────────────────────────────
        mb.Entity<ErrorNotification>(e =>
        {
            e.ToTable("ErrorNotifications", "aihub");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Project);
            e.HasIndex(x => x.Status).HasDatabaseName("IX_ErrNotif_Status");
            e.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_ErrNotif_Date");
            e.Property(x => x.Project).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.Service).HasMaxLength(100);
            e.Property(x => x.Operation).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(50);
            e.Property(x => x.AcknowledgedBy).HasMaxLength(200);
            e.Property(x => x.ResolvedBy).HasMaxLength(200);
        });

        // ── DocumentProcessingJobs ───────────────────────────────────
        mb.Entity<DocumentProcessingJob>(e =>
        {
            e.ToTable("DocumentProcessingJobs", "aihub");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Project);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Project).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.ModelId).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(50);
        });

        // ── SearchIndexConfigs ───────────────────────────────────────
        mb.Entity<SearchIndexConfig>(e =>
        {
            e.ToTable("SearchIndexConfigs", "aihub");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Project, x.IndexName }).IsUnique();
            e.Property(x => x.Project).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.IndexName).HasMaxLength(200);
        });

        // ── Seed AGONESPot prompt templates ──────────────────────────
        SeedSpotPrompts(mb);
    }

    private static void SeedSpotPrompts(ModelBuilder mb)
    {
        mb.Entity<PromptTemplate>().HasData(
            new PromptTemplate
            {
                Id = 1,
                Project = ProjectName.AGONESPot,
                Module = "FileClassification",
                Section = "General",
                PromptKey = "classify-file",
                Name = "Classify File",
                Description = "Analyzes a file and returns its type, category, and risk level.",
                SystemPrompt = "You are an expert document classifier for the AGONESPot audit platform. " +
                    "Analyze the provided file content and return a JSON object with: " +
                    "fileType (Invoice, Contract, Report, Certificate, Other), " +
                    "category (Financial, Legal, Compliance, HR, Operations), " +
                    "riskLevel (Low, Medium, High, Critical), " +
                    "summary (one-line description).",
                UserPromptTemplate = "File name: {{fileName}}\n\nFile content:\n{{fileContent}}\n\n{{additionalContext}}",
                Model = "gpt-4o",
                MaxTokens = 1024,
                Temperature = 0.3,
                Version = 1,
                Tags = "classification,spot,file",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PromptTemplate
            {
                Id = 2,
                Project = ProjectName.AGONESPot,
                Module = "ReportGeneration",
                Section = "SectionA",
                PromptKey = "generate-section-a",
                Name = "Spot Report - Section A: Executive Summary",
                Description = "Generates the Executive Summary section of a Spot audit report.",
                SystemPrompt = "You are an audit report writer for AGONESPot. Write a professional " +
                    "Executive Summary (Section A) based on the provided audit data. " +
                    "Be concise, factual, and highlight key findings.",
                UserPromptTemplate = "Report Title: {{reportTitle}}\n\nAudit Data:\n{{auditData}}\n\n" +
                    "Write the Executive Summary section.",
                Model = "gpt-4o",
                MaxTokens = 2048,
                Temperature = 0.5,
                Version = 1,
                Tags = "report,spot,section-a,executive-summary",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PromptTemplate
            {
                Id = 3,
                Project = ProjectName.AGONESPot,
                Module = "ReportGeneration",
                Section = "SectionB",
                PromptKey = "generate-section-b",
                Name = "Spot Report - Section B: Findings & Observations",
                Description = "Generates the Findings section with detailed observations.",
                SystemPrompt = "You are an audit report writer for AGONESPot. Write detailed " +
                    "Findings & Observations (Section B). List each finding with: " +
                    "observation, evidence, impact, and recommendation.",
                UserPromptTemplate = "Report Title: {{reportTitle}}\n\nAudit Data:\n{{auditData}}\n\n" +
                    "Write the Findings & Observations section.",
                Model = "gpt-4o",
                MaxTokens = 4096,
                Temperature = 0.5,
                Version = 1,
                Tags = "report,spot,section-b,findings",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PromptTemplate
            {
                Id = 4,
                Project = ProjectName.AGONESPot,
                Module = "ReportGeneration",
                Section = "SectionC",
                PromptKey = "generate-section-c",
                Name = "Spot Report - Section C: Risk Assessment",
                Description = "Generates the Risk Assessment section with risk matrix.",
                SystemPrompt = "You are an audit report writer for AGONESPot. Write a Risk Assessment " +
                    "(Section C). Categorize risks by likelihood and impact. " +
                    "Provide a risk matrix and mitigation strategies.",
                UserPromptTemplate = "Report Title: {{reportTitle}}\n\nAudit Data:\n{{auditData}}\n\n" +
                    "Write the Risk Assessment section.",
                Model = "gpt-4o",
                MaxTokens = 3072,
                Temperature = 0.4,
                Version = 1,
                Tags = "report,spot,section-c,risk",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PromptTemplate
            {
                Id = 5,
                Project = ProjectName.AGONESPot,
                Module = "ReportGeneration",
                Section = "SectionD",
                PromptKey = "generate-section-d",
                Name = "Spot Report - Section D: Recommendations & Action Plan",
                Description = "Generates recommendations with priority, owner, and timeline.",
                SystemPrompt = "You are an audit report writer for AGONESPot. Write Recommendations " +
                    "& Action Plan (Section D). For each recommendation provide: " +
                    "priority (Critical/High/Medium/Low), responsible party, timeline, and expected outcome.",
                UserPromptTemplate = "Report Title: {{reportTitle}}\n\nAudit Data:\n{{auditData}}\n\n" +
                    "Write the Recommendations & Action Plan section.",
                Model = "gpt-4o",
                MaxTokens = 3072,
                Temperature = 0.5,
                Version = 1,
                Tags = "report,spot,section-d,recommendations",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
