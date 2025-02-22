namespace ChatSystem.Application.DTOs;

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Guid ChatRoomId { get; set; }
}

public class CreateChatMessageDto
{
    public string Content { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public Guid ChatRoomId { get; set; }
} 