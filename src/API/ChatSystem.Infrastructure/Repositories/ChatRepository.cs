using ChatSystem.Domain.Entities;
using ChatSystem.Domain.Repositories;
using ChatSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ChatSystem.Infrastructure.Repositories;

public class ChatRepository : IChatRepository
{
    private readonly ChatDbContext _context;
    private readonly ILogger<ChatRepository> _logger;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private static readonly TimeSpan LOCK_TIMEOUT = TimeSpan.FromSeconds(30);

    private const int MAX_MESSAGES_PER_ROOM = 50;
    private const int MAX_PENDING_ROOMS = 100;
    private const int MAX_ACTIVE_ROOMS = 50;

    public ChatRepository(
        ChatDbContext context,
        ILogger<ChatRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private async Task<IDisposable> AcquireLockAsync(string key)
    {
        var @lock = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!await @lock.WaitAsync(LOCK_TIMEOUT))
        {
            throw new TimeoutException($"Failed to acquire lock for key {key}");
        }
        return new AsyncLockReleaser(@lock);
    }

    private class AsyncLockReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public AsyncLockReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _semaphore.Release();
                _disposed = true;
            }
        }
    }

    public async Task<ChatRoom> CreateChatRoomAsync(ChatRoom chatRoom)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(chatRoom);
            
            using (await AcquireLockAsync($"create_room_{chatRoom.RequestorId}"))
            {
                // Check if user already has active rooms
                var activeRooms = await _context.ChatRooms
                    .AsNoTracking()
                    .Where(r => r.RequestorId == chatRoom.RequestorId && 
                               r.Status == ChatRoomStatus.Active)
                    .CountAsync();

                if (activeRooms >= MAX_ACTIVE_ROOMS)
                {
                    throw new InvalidOperationException($"User {chatRoom.RequestorId} has reached the maximum number of active rooms");
                }

                await _context.ChatRooms.AddAsync(chatRoom);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created chat room with ID: {ChatRoomId}", chatRoom.Id);
                return chatRoom;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating chat room");
            throw;
        }
    }

    public async Task<ChatRoom?> GetChatRoomByIdAsync(Guid id)
    {
        try
        {
            var chatRoom = await _context.ChatRooms
                .AsNoTracking()
                .Include(r => r.Messages
                    .OrderBy(m => m.Timestamp)
                    .Take(MAX_MESSAGES_PER_ROOM))
                .FirstOrDefaultAsync(r => r.Id == id);

            if (chatRoom == null)
            {
                _logger.LogWarning("Chat room not found with ID: {ChatRoomId}", id);
                return null;
            }

            return chatRoom;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving chat room with ID: {ChatRoomId}", id);
            throw;
        }
    }

    public async Task<IEnumerable<ChatRoom>> GetPendingChatRoomsAsync()
    {
        try
        {
            return await _context.ChatRooms
                .AsNoTracking()
                .Where(r => r.Status == ChatRoomStatus.Pending)
                .OrderByDescending(r => r.CreatedAt)
                .Take(MAX_PENDING_ROOMS)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pending chat rooms");
            throw;
        }
    }

    public async Task<IEnumerable<ChatRoom>> GetActiveChatRoomsByUserIdAsync(string userId)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(userId);
            return await _context.ChatRooms
                .AsNoTracking()
                .Where(r => r.Status == ChatRoomStatus.Active &&
                           (r.RequestorId == userId || r.ListenerId == userId))
                .OrderByDescending(r => r.CreatedAt)
                .Take(MAX_ACTIVE_ROOMS)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active chat rooms for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<ChatMessage> SaveMessageAsync(ChatMessage message)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(message);
            
            using (await AcquireLockAsync($"save_message_{message.ChatRoomId}"))
            {
                // Verify chat room exists
                var chatRoom = await _context.ChatRooms
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == message.ChatRoomId);

                if (chatRoom == null)
                {
                    throw new KeyNotFoundException($"ChatRoom with ID {message.ChatRoomId} not found");
                }

                // Only check active status for non-initial messages
                if (chatRoom.Status != ChatRoomStatus.Active && chatRoom.Status != ChatRoomStatus.Pending)
                {
                    throw new InvalidOperationException("Cannot send messages to ended chat rooms");
                }

                // Check message count
                var messageCount = await _context.ChatMessages
                    .Where(m => m.ChatRoomId == message.ChatRoomId)
                    .CountAsync();

                if (messageCount >= MAX_MESSAGES_PER_ROOM)
                {
                    throw new InvalidOperationException($"Chat room has reached the maximum message limit of {MAX_MESSAGES_PER_ROOM}");
                }

                await _context.ChatMessages.AddAsync(message);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Saved message for chat room: {ChatRoomId}", message.ChatRoomId);
                return message;
            }
        }
        catch (Exception ex) when (ex is not KeyNotFoundException && ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Error saving message for chat room: {ChatRoomId}", message?.ChatRoomId);
            throw;
        }
    }

    public async Task<IEnumerable<ChatMessage>> GetChatRoomMessagesAsync(Guid chatRoomId)
    {
        try
        {
            // First verify the chat room exists and is active
            var chatRoom = await _context.ChatRooms
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == chatRoomId);

            if (chatRoom == null)
            {
                _logger.LogWarning("Chat room not found with ID: {ChatRoomId}", chatRoomId);
                return Enumerable.Empty<ChatMessage>();
            }

            if (chatRoom.Status != ChatRoomStatus.Active)
            {
                _logger.LogWarning("Chat room {ChatRoomId} is not active", chatRoomId);
                return Enumerable.Empty<ChatMessage>();
            }

            return await _context.ChatMessages
                .AsNoTracking()
                .Where(m => m.ChatRoomId == chatRoomId)
                .OrderBy(m => m.Timestamp)
                .Take(MAX_MESSAGES_PER_ROOM)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving messages for chat room: {ChatRoomId}", chatRoomId);
            throw;
        }
    }

    public async Task UpdateChatRoomStatusAsync(Guid chatRoomId, ChatRoomStatus status)
    {
        try
        {
            using (await AcquireLockAsync($"room_status_{chatRoomId}"))
            {
                var chatRoom = await _context.ChatRooms.FindAsync(chatRoomId)
                    ?? throw new KeyNotFoundException($"ChatRoom with ID {chatRoomId} not found");

                // Validate status transition
                if (chatRoom.Status == ChatRoomStatus.Ended)
                {
                    throw new InvalidOperationException("Cannot update status of an ended chat room");
                }

                if (status == ChatRoomStatus.Active && chatRoom.Status != ChatRoomStatus.Pending)
                {
                    throw new InvalidOperationException("Can only activate pending chat rooms");
                }

                chatRoom.Status = status;
                if (status == ChatRoomStatus.Ended)
                {
                    chatRoom.EndedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Updated chat room {ChatRoomId} status to {Status}", chatRoomId, status);
            }
        }
        catch (Exception ex) when (ex is not KeyNotFoundException && ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Error updating chat room status: {ChatRoomId}", chatRoomId);
            throw;
        }
    }
} 