namespace OdooToZohoMigration.Core.Entities;

public class Candidate
{
    public int Id { get; set; }
    public int OdooId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? ResumeText { get; set; }
    public string? ResumeUrl { get; set; }
    public string? VectorEmbeddingJson { get; set; }
    public DateTime LastSyncedAt { get; set; }
    public string? CountryOfBirth { get; set; }
    public string? CurrentLocation { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? ExperienceSummary { get; set; }
    public string? Gender { get; set; }
    public string? LanguagesJson { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? MaritalStatus { get; set; }
    public string? Mobile { get; set; }
    public int? NationalityId { get; set; }
    public string? PlaceOfBirth { get; set; }
    public DateTime ProfileCreatedAt { get; set; }
    public int? RelevantExperienceYears { get; set; }
    public string? Skills { get; set; }
    public int? TotalExperienceYears { get; set; }
    public bool? IsIndex { get; set; }

    // Migration tracking
    public string? ZohoCandidateId { get; set; }
    public bool IsMigrated { get; set; }
    public DateTime? MigratedAt { get; set; }
    public string? MigrationError { get; set; }
    public bool IsCvMigrated { get; set; }
    public DateTime? CvMigratedAt { get; set; }
}
