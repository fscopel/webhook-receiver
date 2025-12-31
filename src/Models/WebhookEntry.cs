using Google.Cloud.Firestore;

namespace WebhookReceiver.Models;

[FirestoreData]
public class WebhookEntry
{
    [FirestoreProperty]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    
    [FirestoreProperty]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    
    [FirestoreProperty]
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    
    [FirestoreProperty]
    public string Method { get; set; } = string.Empty;
    
    [FirestoreProperty]
    public string Path { get; set; } = string.Empty;
    
    [FirestoreProperty]
    public string? Channel { get; set; }
    
    [FirestoreProperty]
    public string? QueryString { get; set; }
    
    [FirestoreProperty]
    public Dictionary<string, string> Headers { get; set; } = new();
    
    [FirestoreProperty]
    public string? ContentType { get; set; }
    
    [FirestoreProperty]
    public string? Body { get; set; }
    
    [FirestoreProperty]
    public string? SourceIp { get; set; }
    
    [FirestoreProperty]
    public long ContentLength { get; set; }
}
