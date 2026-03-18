namespace OdooToZohoMigration.Core.DTOs.Migration;

public class CvUploadResult
{
    public bool Success { get; set; }
    public string? AttachmentId { get; set; }
    public string? ErrorMessage { get; set; }
}
