namespace ChatSystem.UI.Shared;

public class ChatRoomDto
{
    public Guid Id { get; set; }
    public string RequestorId { get; set; } = string.Empty;
    public string ListenerId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Guid ChatRoomId { get; set; }
} 