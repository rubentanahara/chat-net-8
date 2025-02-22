using ChatSystem.Application.DTOs;
using ChatSystem.Application.Interfaces;
using ChatSystem.Domain.Entities;
using ChatSystem.Domain.Repositories;

namespace ChatSystem.Application.Services;

public class ChatService : IChatService
{
    private readonly IChatRepository _chatRepository;

    public ChatService(IChatRepository chatRepository)
    {
        _chatRepository = chatRepository;
    }

    public async Task<ChatRoomDto> CreateChatRequestAsync(CreateChatRoomDto request)
    {
        var chatRoom = new ChatRoom
        {
            Id = Guid.NewGuid(),
            RequestorId = request.RequestorId,
            CreatedAt = DateTime.UtcNow,
            Status = ChatRoomStatus.Pending
        };

        var createdRoom = await _chatRepository.CreateChatRoomAsync(chatRoom);
        
        // Create initial message
        if (!string.IsNullOrEmpty(request.RequestMessage))
        {
            await _chatRepository.SaveMessageAsync(new ChatMessage
            {
                Id = Guid.NewGuid(),
                ChatRoomId = createdRoom.Id,
                Content = request.RequestMessage,
                SenderId = request.RequestorId,
                Timestamp = DateTime.UtcNow
            });
        }

        return MapToDto(createdRoom);
    }

    public async Task<ChatRoomDto> AcceptChatRequestAsync(AcceptChatRequestDto request)
    {
        var chatRoom = await _chatRepository.GetChatRoomByIdAsync(request.ChatRoomId);
        if (chatRoom == null)
            throw new ArgumentException("Chat room not found");

        if (chatRoom.Status != ChatRoomStatus.Pending)
            throw new InvalidOperationException("Chat room is not in pending status");

        chatRoom.ListenerId = request.ListenerId;
        chatRoom.Status = ChatRoomStatus.Active;
        await _chatRepository.UpdateChatRoomStatusAsync(chatRoom.Id, ChatRoomStatus.Active);

        return MapToDto(chatRoom);
    }

    public async Task<ChatMessageDto> SendMessageAsync(CreateChatMessageDto messageDto)
    {
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            Content = messageDto.Content,
            SenderId = messageDto.SenderId,
            ChatRoomId = messageDto.ChatRoomId,
            Timestamp = DateTime.UtcNow
        };

        var savedMessage = await _chatRepository.SaveMessageAsync(message);
        return MapToDto(savedMessage);
    }

    public async Task<IEnumerable<ChatRoomDto>> GetPendingChatRequestsAsync()
    {
        var pendingRooms = await _chatRepository.GetPendingChatRoomsAsync();
        return pendingRooms.Select(MapToDto);
    }

    public async Task<IEnumerable<ChatMessageDto>> GetChatHistoryAsync(Guid chatRoomId)
    {
        var messages = await _chatRepository.GetChatRoomMessagesAsync(chatRoomId);
        return messages.Select(MapToDto);
    }

    public async Task EndChatAsync(Guid chatRoomId)
    {
        await _chatRepository.UpdateChatRoomStatusAsync(chatRoomId, ChatRoomStatus.Ended);
    }

    public async Task<IEnumerable<ChatRoomDto>> GetActiveChatRoomsForUserAsync(string userId)
    {
        var rooms = await _chatRepository.GetActiveChatRoomsByUserIdAsync(userId);
        return rooms.Select(MapToDto);
    }

    public async Task<ChatRoomDto?> GetChatRoomByIdAsync(Guid chatRoomId)
    {
        var chatRoom = await _chatRepository.GetChatRoomByIdAsync(chatRoomId);
        return chatRoom != null ? MapToDto(chatRoom) : null;
    }

    private static ChatRoomDto MapToDto(ChatRoom room)
    {
        return new ChatRoomDto
        {
            Id = room.Id,
            RequestorId = room.RequestorId,
            ListenerId = room.ListenerId,
            CreatedAt = room.CreatedAt,
            EndedAt = room.EndedAt,
            Status = room.Status.ToString()
        };
    }

    private static ChatMessageDto MapToDto(ChatMessage message)
    {
        return new ChatMessageDto
        {
            Id = message.Id,
            Content = message.Content,
            SenderId = message.SenderId,
            ReceiverId = message.ReceiverId,
            Timestamp = message.Timestamp,
            ChatRoomId = message.ChatRoomId
        };
    }
} 