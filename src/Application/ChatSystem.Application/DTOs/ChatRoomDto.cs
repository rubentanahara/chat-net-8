namespace ChatSystem.Application.DTOs;

public class ChatRoomDto
{
    public Guid Id { get; set; }
    public string RequestorId { get; set; } = string.Empty;
    public string ListenerId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CreateChatRoomDto
{
    public string RequestorId { get; set; } = string.Empty;
    public string RequestMessage { get; set; } = string.Empty;
}

public class AcceptChatRequestDto
{
    public Guid ChatRoomId { get; set; }
    public string ListenerId { get; set; } = string.Empty;
} 