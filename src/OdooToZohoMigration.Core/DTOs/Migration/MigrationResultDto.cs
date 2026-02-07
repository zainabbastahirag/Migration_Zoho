namespace OdooToZohoMigration.Core.DTOs.Migration;

public class MigrationResultDto
{
    public bool Success { get; set; }
    public int RunId { get; set; }
    public string Message { get; set; } = string.Empty;
    public MigrationRunSummaryDto? Summary { get; set; }
}

public class MigrationRunSummaryDto
{
    public int JobsMigrated { get; set; }
    public int JobsFailed { get; set; }
    public int CandidatesMigrated { get; set; }
    public int CandidatesFailed { get; set; }
    public int ApplicationsMigrated { get; set; }
    public int ApplicationsFailed { get; set; }
    public int CvsUploaded { get; set; }
    public int CvsFailed { get; set; }
    public int CommentsMigrated { get; set; }
    public int CommentsFailed { get; set; }
    public int SummariesMigrated { get; set; }
    public int SummariesFailed { get; set; }
    public int HistoryMigrated { get; set; }
    public int HistoryFailed { get; set; }
    public TimeSpan Duration { get; set; }
}
