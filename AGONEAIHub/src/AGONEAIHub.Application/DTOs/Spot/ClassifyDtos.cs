namespace AGONEAIHub.Application.DTOs.Spot;

public class ClassifyResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}

public class ClassifyStatusResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public DocumentMetadataDto? Data { get; set; }
}

public class DocumentMetadataDto
{
    public string FileUID { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int FileSize { get; set; }
    public string FileType { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public string FileState { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public double? Confidence { get; set; }
    public string FileURL { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public string? DocOfCompany { get; set; }
    public string? DocOfCompanyRegNo { get; set; }
    public DateTime CreatedDate { get; set; }
}
