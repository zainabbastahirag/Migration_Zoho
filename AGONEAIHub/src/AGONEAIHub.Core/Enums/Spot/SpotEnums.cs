namespace AGONEAIHub.Core.Enums.Spot;

public enum JobState
{
    PendingQueue,
    Processing,
    Success,
    Failed
}

public enum JobType
{
    Classify = 1,
    Report = 2
}

public enum FileStatus
{
    PENDING,
    PROCESSED,
    APPROVED,
    REJECTED
}

public static class CoreDocumentTypes
{
    public const string SSM = "SSM";
    public const string CTOS = "CTOS";
    public const string AuditReport = "AuditReport";
    public const string Unknown = "Unknown";

    public static readonly HashSet<string> CoreTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        SSM, CTOS, AuditReport
    };

    public static bool IsCoreType(string? docType) =>
        !string.IsNullOrWhiteSpace(docType) && CoreTypes.Contains(docType);
}
