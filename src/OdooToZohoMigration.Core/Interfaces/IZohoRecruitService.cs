using OdooToZohoMigration.Core.DTOs.Migration;
using OdooToZohoMigration.Core.DTOs.Zoho;
using OdooToZohoMigration.Core.Entities;

namespace OdooToZohoMigration.Core.Interfaces;

public interface IZohoRecruitService
{
    // Authentication
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
    Task<string> RefreshAccessTokenAsync(CancellationToken ct = default);

    // Jobs
    Task<EntityMigrationResult> CreateJobOpeningAsync(Job job, CancellationToken ct = default);
    Task<EntityMigrationResult> UpdateJobOpeningAsync(string zohoId, Job job, CancellationToken ct = default);
    Task<List<EntityMigrationResult>> CreateJobOpeningsBatchAsync(IEnumerable<Job> jobs, CancellationToken ct = default);
    Task<EntityMigrationResult> UpdateJobOpeningRecruiterAsync(string zohoJobId, string recruiterEmail, CancellationToken ct = default);

    // Candidates
    Task<EntityMigrationResult> CreateCandidateAsync(Candidate candidate, CancellationToken ct = default);
    Task<EntityMigrationResult> UpdateCandidateAsync(string zohoId, Candidate candidate, CancellationToken ct = default);
    Task<List<EntityMigrationResult>> CreateCandidatesBatchAsync(IEnumerable<Candidate> candidates, CancellationToken ct = default);
    Task<ZohoCandidateMinimal?> GetCandidateByIdAsync(string? zohoId, CancellationToken ct = default);

    // Applications
    Task<EntityMigrationResult> AssociateCandidateWithJobAsync(string candidateZohoId, string jobZohoId, string? comments = null, CancellationToken ct = default);
    Task<EntityMigrationResult> UpdateApplicationStatusAsync(string applicationZohoId, string status, CancellationToken ct = default);

    // CVs
    Task<CvUploadResult> UploadCandidateCvAsync(string candidateZohoId, Stream cvStream, string fileName, string contentType, CancellationToken ct = default);

    // Notes (for Comments, Summaries, History)
    Task<EntityMigrationResult> CreateNoteAsync(string parentModule, string parentId, string noteTitle, string noteContent, CancellationToken ct = default);
    Task<List<EntityMigrationResult>> CreateNotesBatchAsync(string parentModule, List<(string ParentId, string Title, string Content)> notes, CancellationToken ct = default);
}
