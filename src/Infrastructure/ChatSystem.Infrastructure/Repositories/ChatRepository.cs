using ChatSystem.Domain.Entities;
using ChatSystem.Domain.Repositories;
using ChatSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatSystem.Infrastructure.Repositories;

public class ChatRepository : IChatRepository
{
    private readonly ChatDbContext _context;

    public ChatRepository(ChatDbContext context)
    {
        _context = context;
    }

    public async Task<ChatRoom> CreateChatRoomAsync(ChatRoom chatRoom)
    {
        _context.ChatRooms.Add(chatRoom);
        await _context.SaveChangesAsync();
        return chatRoom;
    }

    public async Task<ChatRoom?> GetChatRoomByIdAsync(Guid id)
    {
        return await _context.ChatRooms
            .Include(r => r.Messages)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<IEnumerable<ChatRoom>> GetPendingChatRoomsAsync()
    {
        return await _context.ChatRooms
            .Include(r => r.Messages)
            .Where(r => r.Status == ChatRoomStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<ChatRoom>> GetActiveChatRoomsByUserIdAsync(string userId)
    {
        return await _context.ChatRooms
            .Include(r => r.Messages)
            .Where(r => r.Status == ChatRoomStatus.Active &&
                       (r.RequestorId == userId || r.ListenerId == userId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<ChatMessage> SaveMessageAsync(ChatMessage message)
    {
        _context.ChatMessages.Add(message);
        await _context.SaveChangesAsync();
        return message;
    }

    public async Task<IEnumerable<ChatMessage>> GetChatRoomMessagesAsync(Guid chatRoomId)
    {
        return await _context.ChatMessages
            .Where(m => m.ChatRoomId == chatRoomId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
    }

    public async Task UpdateChatRoomStatusAsync(Guid chatRoomId, ChatRoomStatus status)
    {
        var chatRoom = await _context.ChatRooms.FindAsync(chatRoomId);
        if (chatRoom != null)
        {
            chatRoom.Status = status;
            if (status == ChatRoomStatus.Ended)
            {
                chatRoom.EndedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
        }
    }
} 