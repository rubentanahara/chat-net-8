namespace ChatSystem.Domain.Entities;

public class ChatMessage
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsRead { get; set; }
    public Guid ChatRoomId { get; set; }
} 