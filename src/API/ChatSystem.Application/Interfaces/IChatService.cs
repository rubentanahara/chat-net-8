using ChatSystem.Application.DTOs;

namespace ChatSystem.Application.Interfaces;

public interface IChatService
{
    Task<ChatRoomDto> CreateChatRequestAsync(CreateChatRoomDto request);
    Task<ChatRoomDto> AcceptChatRequestAsync(AcceptChatRequestDto request);
    Task<ChatMessageDto> SendMessageAsync(CreateChatMessageDto message);
    Task<IEnumerable<ChatRoomDto>> GetPendingChatRequestsAsync();
    Task<IEnumerable<ChatMessageDto>> GetChatHistoryAsync(Guid chatRoomId);
    Task EndChatAsync(Guid chatRoomId);
    Task<IEnumerable<ChatRoomDto>> GetActiveChatRoomsForUserAsync(string userId);
    Task<ChatRoomDto?> GetChatRoomByIdAsync(Guid chatRoomId);
} 