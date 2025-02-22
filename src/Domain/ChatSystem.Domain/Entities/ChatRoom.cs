namespace ChatSystem.Domain.Entities;

public class ChatRoom
{
    public Guid Id { get; set; }
    public string RequestorId { get; set; } = string.Empty;
    public string ListenerId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public ChatRoomStatus Status { get; set; }
    public List<ChatMessage> Messages { get; set; } = new();
}

public enum ChatRoomStatus
{
    Pending,
    Active,
    Ended
} 