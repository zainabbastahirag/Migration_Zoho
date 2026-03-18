namespace AGONEAIHub.Core.Entities.Spot;

public class SpotDocumentMetadata
{
    public string FileUID { get; set; } = Guid.NewGuid().ToString().ToUpper();
    public string JobId { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int FileSize { get; set; }
    public string FileType { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
    public string Notes { get; set; } = "";
    public double? Confidence { get; set; }
    public string CreatedBy { get; set; } = "";
    public string DeletedBy { get; set; } = "";
    public DateTime? DeletedDate { get; set; }
    public string DocOfCompany { get; set; } = "";
    public string? DocOfCompanyRegNo { get; set; }
    public string DocumentType { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string FileProcessState { get; set; } = "";
    public string FileState { get; set; } = "";
    public string FileURL { get; set; } = "";
    public string Tags { get; set; } = "";
    public bool WrongType { get; set; } = false;
}
