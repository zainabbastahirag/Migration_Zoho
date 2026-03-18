namespace AGONEAIHub.Core.Entities.Spot;

public class SpotJobLog
{
    public int Id { get; set; }
    public string JobId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
