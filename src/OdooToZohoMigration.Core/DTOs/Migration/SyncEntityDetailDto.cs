namespace OdooToZohoMigration.Core.DTOs.Migration;

/// <summary>
/// Detailed sync status for a single entity record.
/// Used to inspect individual records and find out why they failed.
/// </summary>
public class SyncEntityDetailDto
{
    public int Id { get; set; }
    public int OdooId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string SyncStatus { get; set; } = string.Empty;   // "Synced", "Pending", "Failed"
    public string? ZohoId { get; set; }
    public DateTime? SyncedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AdditionalInfo { get; set; }
}

/// <summary>
/// Paginated result for querying entity sync status
/// </summary>
public class PagedSyncResultDto
{
    public List<SyncEntityDetailDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public string Filter { get; set; } = "all";
    public string EntityType { get; set; } = string.Empty;
}

/// <summary>
/// Query parameters for filtering sync status
/// </summary>
public class SyncStatusQueryDto
{
    /// <summary>
    /// Filter: "all", "synced", "pending", "failed"
    /// </summary>
    public string Filter { get; set; } = "all";

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// Optional search by name or email
    /// </summary>
    public string? Search { get; set; }

    /// <summary>
    /// Optional error message filter
    /// </summary>
    public string? ErrorFilter { get; set; }
}
