namespace OdooToZohoMigration.Core.Entities;

public class Application
{
    public int Id { get; set; }
    public int OdooId { get; set; }
    public int JobId { get; set; }
    public int CandidateId { get; set; }
    public int? StageId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime LastSyncedAt { get; set; }
    public int SummaryId { get; set; }
    public DateTime? AppliedAt { get; set; }
    public DateTime? AvailabilityDate { get; set; }
    public int? CampaignId { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int? CompanyId { get; set; }
    public string? CoverNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? DepartmentId { get; set; }
    public decimal? ExpectedSalary { get; set; }
    public string? InterviewAvailability { get; set; }
    public bool IsActive { get; set; }
    public string? KanbanState { get; set; }
    public decimal? OfferedSalary { get; set; }
    public int? PreviousStageId { get; set; }
    public int? Priority { get; set; }
    public int? RecruiterId { get; set; }
    public string? SalaryCurrency { get; set; }
    public int? SourceId { get; set; }
    public double? SuccessProbability { get; set; }

    // Migration tracking
    public string? ZohoApplicationId { get; set; }
    public bool IsMigrated { get; set; }
    public DateTime? MigratedAt { get; set; }
    public string? MigrationError { get; set; }

    // Navigation properties
    public Job Job { get; set; } = null!;
    public Candidate Candidate { get; set; } = null!;
    public ICollection<ApplicationComment> Comments { get; set; } = new List<ApplicationComment>();
    public ICollection<ApplicationHistory> Histories { get; set; } = new List<ApplicationHistory>();
}
