namespace WebhookReceiver.Models;

public class WebhookEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Channel { get; set; }
    public string? QueryString { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? ContentType { get; set; }
    public string? Body { get; set; }
    public string? SourceIp { get; set; }
    public long ContentLength { get; set; }
}

