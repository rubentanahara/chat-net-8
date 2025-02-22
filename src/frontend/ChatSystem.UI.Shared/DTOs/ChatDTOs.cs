namespace ChatSystem.UI.Shared.DTOs;

public record ChatMessageDto(
    Guid Id,
    Guid ChatRoomId,
    string SenderId,
    string Content,
    DateTime Timestamp,
    bool IsRead
);

public record ChatRoomDto(
    Guid Id,
    string RequestorId,
    string? ListenerId,
    string Status,
    DateTime CreatedAt,
    DateTime? EndedAt,
    string RequestType,
    string InitialMessage
);

public record CreateChatRequestDto(
    string RequestorId,
    string RequestType,
    string InitialMessage
); 