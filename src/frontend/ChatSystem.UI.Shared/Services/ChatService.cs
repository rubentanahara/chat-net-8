using Microsoft.AspNetCore.SignalR.Client;
using ChatSystem.UI.Shared.Components;
using ChatSystem.UI.Shared.DTOs;
using Spectre.Console;

namespace ChatSystem.UI.Shared.Services;

public class ChatService
{
    private readonly HubConnection _hubConnection;
    private readonly string _userId;
    private Guid? _currentChatRoomId;
    private bool _isInChat;
    private bool _chatScreenShown;
    private System.Timers.Timer? _typingTimer;
    private bool _lastTypingStatus;

    public ChatService(string userId, string hubUrl)
    {
        _userId = userId;
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{hubUrl}?userId={userId}")
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();
    }

    public bool IsInChat => _isInChat;
    public Guid? CurrentChatRoomId => _currentChatRoomId;
    public HubConnection Connection => _hubConnection;

    public async Task InitializeConnection()
    {
        try
        {
            await _hubConnection.StartAsync();
            ChatComponents.RenderSuccess("Connected to chat server!");
        }
        catch (Exception ex)
        {
            ChatComponents.RenderError($"Failed to connect to server: {ex.Message}");
            throw;
        }
    }

    public void RegisterBaseEvents()
    {
        _hubConnection.Closed += async (error) =>
        {
            LogConnectionEvent("Connection closed", error);
            await ReconnectAsync();
        };

        _hubConnection.Reconnecting += (error) =>
        {
            LogConnectionEvent("Reconnecting", error);
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += (connectionId) =>
        {
            LogConnectionEvent("Reconnected", null);
            return Task.CompletedTask;
        };

        _hubConnection.On<string, string>("ReceiveMessage", (senderId, message) =>
        {
            if (senderId != _userId)
            {
                Console.WriteLine();
                ChatComponents.RenderMessage(senderId, message, false, DateTime.Now);
                ChatComponents.RenderInputPrompt();
                if (_currentChatRoomId.HasValue)
                {
                    _ = MarkMessageAsSeen();
                }
            }
        });

        _hubConnection.On<string>("ChatEnded", (message) =>
        {
            Console.WriteLine();
            ChatComponents.RenderInfo(message);
            _isInChat = false;
            _currentChatRoomId = null;
            _chatScreenShown = false;
        });

        _hubConnection.On<string, bool>("UserTypingStatus", (userId, isTyping) =>
        {
            if (userId != _userId)
            {
                if (isTyping)
                {
                    ChatComponents.RenderTypingIndicator(userId);
                }
                else
                {
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                    Console.Write(new string(' ', Console.WindowWidth));
                    Console.SetCursorPosition(0, Console.CursorTop);
                }
                ChatComponents.RenderInputPrompt();
            }
        });

        _hubConnection.On<string>("MessagesSeen", (userId) =>
        {
            if (userId != _userId)
            {
                Console.WriteLine();
                ChatComponents.RenderSeenStatus(userId);
                ChatComponents.RenderInputPrompt();
            }
        });
    }

    public async Task SendMessage(string message)
    {
        if (_currentChatRoomId == null || !_isInChat)
        {
            ChatComponents.RenderError("You are not in an active chat.");
            return;
        }

        message = SanitizeMessage(message);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (message.Length > 1000)
        {
            ChatComponents.RenderError("Message is too long. Maximum length is 1000 characters.");
            return;
        }

        try
        {
            await _hubConnection.InvokeAsync("SendMessage", _currentChatRoomId, _userId, message);
            Console.WriteLine();
            ChatComponents.RenderMessage("You", message, true, DateTime.Now);
            ChatComponents.RenderInputPrompt();
        }
        catch (Exception ex)
        {
            ChatComponents.RenderError($"Failed to send message: {ex.Message}");
        }
    }

    public async Task EndChat()
    {
        if (_currentChatRoomId == null)
        {
            return;
        }

        try
        {
            await _hubConnection.InvokeAsync("EndChat", _currentChatRoomId);
            await CleanupChatState();
            ChatComponents.RenderInfo("Chat ended.");
        }
        catch (Exception ex)
        {
            ChatComponents.RenderError($"Failed to end chat: {ex.Message}");
        }
    }

    public async Task ManageTypingStatus()
    {
        if (_typingTimer != null)
        {
            _typingTimer.Stop();
            _typingTimer.Dispose();
            _typingTimer = null;
        }

        _typingTimer = new System.Timers.Timer(1000);
        _typingTimer.Elapsed += async (sender, e) =>
        {
            await UpdateTypingStatus(false);
            _lastTypingStatus = false;
            if (_typingTimer != null)
            {
                _typingTimer.Stop();
                _typingTimer.Dispose();
                _typingTimer = null;
            }
        };
        _typingTimer.AutoReset = false;

        if (!_lastTypingStatus)
        {
            await UpdateTypingStatus(true);
            _lastTypingStatus = true;
        }

        _typingTimer.Start();
    }

    private async Task UpdateTypingStatus(bool isTyping)
    {
        if (_currentChatRoomId.HasValue && _lastTypingStatus != isTyping)
        {
            try
            {
                await _hubConnection.InvokeAsync("UpdateTypingStatus", _currentChatRoomId, _userId, isTyping);
                _lastTypingStatus = isTyping;
            }
            catch
            {
                // Silently handle typing status errors
            }
        }
    }

    private async Task MarkMessageAsSeen()
    {
        if (_currentChatRoomId.HasValue)
        {
            try
            {
                await _hubConnection.InvokeAsync("MarkMessageAsSeen", _currentChatRoomId, _userId);
            }
            catch
            {
                // Silently handle seen status errors
            }
        }
    }

    private async Task CleanupChatState()
    {
        _isInChat = false;
        _currentChatRoomId = null;
        _chatScreenShown = false;
        _lastTypingStatus = false;
        
        if (_typingTimer != null)
        {
            _typingTimer.Stop();
            _typingTimer.Dispose();
            _typingTimer = null;
        }
        
        await UpdateTypingStatus(false);
        Console.Clear();
    }

    private async Task ReconnectAsync()
    {
        ChatComponents.RenderInfo("Attempting to reconnect...");
        await Task.Delay(new Random().Next(0, 5) * 1000);
        await _hubConnection.StartAsync();
    }

    private void LogConnectionEvent(string eventDescription, Exception? error)
    {
        if (error != null)
        {
            ChatComponents.RenderError($"{eventDescription}: {error.Message}");
        }
        else
        {
            ChatComponents.RenderSuccess(eventDescription);
        }
    }

    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        message = new string(message.Where(c => !char.IsControl(c)).ToArray());
        message = message.Trim();
        message = string.Join(" ", message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        
        return message;
    }

    public void SetChatState(Guid chatRoomId, bool isInChat)
    {
        _currentChatRoomId = chatRoomId;
        _isInChat = isInChat;
        _chatScreenShown = false;
    }

    public async Task RenderChatScreen(string partnerName, string requestType = "")
    {
        if (!_currentChatRoomId.HasValue) return;

        ChatComponents.RenderChatHeader(_currentChatRoomId.Value.ToString(), partnerName, requestType);

        try
        {
            var history = await _hubConnection.InvokeAsync<IEnumerable<ChatMessageDto>>(
                "GetChatHistoryAsync", 
                _currentChatRoomId.Value
            );

            foreach (var msg in history)
            {
                ChatComponents.RenderMessage(
                    msg.SenderId == _userId ? "You" : msg.SenderId,
                    msg.Content,
                    msg.SenderId == _userId,
                    msg.Timestamp
                );
            }
        }
        catch (Exception ex)
        {
            ChatComponents.RenderError($"Failed to load chat history: {ex.Message}");
        }

        ChatComponents.RenderInputPrompt();
        _chatScreenShown = true;
    }
} 