using AGONEAIHub.Core.Enums;

namespace AGONEAIHub.Core.Entities;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public ProjectName Project { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
