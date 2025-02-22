using ChatSystem.Domain.Entities;

namespace ChatSystem.Domain.Repositories;

public interface IChatRepository
{
    Task<ChatRoom> CreateChatRoomAsync(ChatRoom chatRoom);
    Task<ChatRoom?> GetChatRoomByIdAsync(Guid id);
    Task<IEnumerable<ChatRoom>> GetPendingChatRoomsAsync();
    Task<IEnumerable<ChatRoom>> GetActiveChatRoomsByUserIdAsync(string userId);
    Task<ChatMessage> SaveMessageAsync(ChatMessage message);
    Task<IEnumerable<ChatMessage>> GetChatRoomMessagesAsync(Guid chatRoomId);
    Task UpdateChatRoomStatusAsync(Guid chatRoomId, ChatRoomStatus status);
} 