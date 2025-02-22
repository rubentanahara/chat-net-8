using ChatSystem.Application.DTOs;
using ChatSystem.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace ChatSystem.API.Hubs;

public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private static readonly Dictionary<string, string> _userConnections = new();
    private static readonly Dictionary<string, HashSet<string>> _typingUsers = new();

    public ChatHub(IChatService chatService)
    {
        _chatService = chatService;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.GetHttpContext()?.Request.Query["userId"].ToString();
        if (!string.IsNullOrEmpty(userId))
        {
            _userConnections[userId] = Context.ConnectionId;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = _userConnections.FirstOrDefault(x => x.Value == Context.ConnectionId).Key;
        if (!string.IsNullOrEmpty(userId))
        {
            _userConnections.Remove(userId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
            
            // Remove user from typing status when disconnected
            foreach (var chatRoom in _typingUsers.Keys.ToList())
            {
                if (_typingUsers[chatRoom].Contains(userId))
                {
                    _typingUsers[chatRoom].Remove(userId);
                    await NotifyTypingStatusChanged(chatRoom, userId, false);
                }
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<ChatMessageDto> SendMessage(Guid chatRoomId, string senderId, string message)
    {
        var messageDto = new CreateChatMessageDto
        {
            ChatRoomId = chatRoomId,
            SenderId = senderId,
            Content = message
        };

        var createdMessage = await _chatService.SendMessageAsync(messageDto);
        
        var chatRoom = await _chatService.GetChatRoomByIdAsync(chatRoomId);
        if (chatRoom != null)
        {
            await Clients.Group($"user_{chatRoom.RequestorId}").SendAsync("ReceiveMessage", senderId, message);
            await Clients.Group($"user_{chatRoom.ListenerId}").SendAsync("ReceiveMessage", senderId, message);
        }

        return createdMessage;
    }

    public async Task<ChatRoomDto> CreateChatRequest(string requestorId, string requestMessage)
    {
        var request = new CreateChatRoomDto
        {
            RequestorId = requestorId,
            RequestMessage = requestMessage
        };

        var chatRoom = await _chatService.CreateChatRequestAsync(request);
        await Clients.All.SendAsync("NewChatRequest", chatRoom);
        return chatRoom;
    }

    public async Task<IEnumerable<ChatRoomDto>> GetPendingRequests()
    {
        return await _chatService.GetPendingChatRequestsAsync();
    }

    public async Task<ChatRoomDto> AcceptChatRequest(Guid chatRoomId, string listenerId)
    {
        var request = new AcceptChatRequestDto
        {
            ChatRoomId = chatRoomId,
            ListenerId = listenerId
        };

        var chatRoom = await _chatService.AcceptChatRequestAsync(request);
        await Clients.Group($"user_{chatRoom.RequestorId}").SendAsync("ChatAccepted", chatRoom);
        await Clients.Group($"user_{chatRoom.ListenerId}").SendAsync("ChatAccepted", chatRoom);
        return chatRoom;
    }

    public async Task<IEnumerable<ChatRoomDto>> GetActiveChats(string userId)
    {
        return await _chatService.GetActiveChatRoomsForUserAsync(userId);
    }

    public async Task EndChat(Guid chatRoomId)
    {
        await _chatService.EndChatAsync(chatRoomId);
        var chatRoom = await _chatService.GetChatRoomByIdAsync(chatRoomId);
        if (chatRoom != null)
        {
            await Clients.Group($"user_{chatRoom.RequestorId}").SendAsync("ChatEnded", "Chat has ended");
            await Clients.Group($"user_{chatRoom.ListenerId}").SendAsync("ChatEnded", "Chat has ended");
            
            // Clean up typing status when chat ends
            if (_typingUsers.ContainsKey(chatRoomId.ToString()))
            {
                _typingUsers.Remove(chatRoomId.ToString());
            }
        }
    }

    public async Task UpdateTypingStatus(Guid chatRoomId, string userId, bool isTyping)
    {
        var chatRoom = await _chatService.GetChatRoomByIdAsync(chatRoomId);
        if (chatRoom == null) return;

        var chatRoomKey = chatRoomId.ToString();
        if (!_typingUsers.ContainsKey(chatRoomKey))
        {
            _typingUsers[chatRoomKey] = new HashSet<string>();
        }

        if (isTyping)
        {
            _typingUsers[chatRoomKey].Add(userId);
        }
        else
        {
            _typingUsers[chatRoomKey].Remove(userId);
        }

        await NotifyTypingStatusChanged(chatRoomKey, userId, isTyping);
    }

    private async Task NotifyTypingStatusChanged(string chatRoomId, string userId, bool isTyping)
    {
        var chatRoom = await _chatService.GetChatRoomByIdAsync(Guid.Parse(chatRoomId));
        if (chatRoom == null) return;

        var otherUserId = userId == chatRoom.RequestorId ? chatRoom.ListenerId : chatRoom.RequestorId;
        await Clients.Group($"user_{otherUserId}").SendAsync("UserTypingStatus", userId, isTyping);
    }

    public async Task MarkMessageAsSeen(Guid chatRoomId, string userId)
    {
        var chatRoom = await _chatService.GetChatRoomByIdAsync(chatRoomId);
        if (chatRoom == null) return;

        var otherUserId = userId == chatRoom.RequestorId ? chatRoom.ListenerId : chatRoom.RequestorId;
        await Clients.Group($"user_{otherUserId}").SendAsync("MessagesSeen", userId);
    }
} 