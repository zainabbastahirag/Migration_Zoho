namespace AGONEAIHub.Core.Entities.Spot;

public class SpotCompany
{
    public string CompanyId { get; set; } = string.Empty;
    public string? CompanyProfileJson { get; set; }
    public string? CompanyProfileSourceFileId { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class SpotRegisteredCompany
{
    public string CompanyId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? RepresentativeName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? CompanyAddress { get; set; }
    public string? Email { get; set; }
}
