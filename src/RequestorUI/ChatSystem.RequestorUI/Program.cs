using Microsoft.AspNetCore.SignalR.Client;
using Spectre.Console;
using System.Text.Json;

namespace ChatSystem.RequestorUI;

public class ChatRoomDto
{
    public Guid Id { get; set; }
    public string RequestorId { get; set; } = string.Empty;
    public string ListenerId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Guid ChatRoomId { get; set; }
}

public class Program
{
    private static HubConnection? _hubConnection;
    private static string? _userId;
    private static Guid? _currentChatRoomId;
    private static bool _isInChat = false;
    private static bool _isWaitingForAcceptance = false;
    private static System.Timers.Timer? _typingTimer;
    private static bool _lastTypingStatus = false;
    private static bool _chatScreenShown = false;

    public static async Task Main(string[] args)
    {
        ShowWelcomeScreen();
        
        _userId = PromptForUserId();
        await InitializeSignalRConnection();

        while (true)
        {
            if (!_isInChat && !_isWaitingForAcceptance)
            {
                ShowMainMenu();
            }
            else if (_isWaitingForAcceptance)
            {
                await HandleWaitingState();
            }
            else
            {
                await HandleChatState();
            }
        }
    }

    private static void ShowWelcomeScreen()
    {
        Console.Clear();
        AnsiConsole.Write(new FigletText("Chat Requestor").Color(Color.Blue));
    }

    private static void ShowMainMenu()
    {
        Console.Clear();
        AnsiConsole.Write(new Rule("[blue]Main Menu[/]").RuleStyle("grey").LeftJustified());
        
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices(new[]
                {
                    "Start New Chat",
                    "View Active Chats",
                    "Exit"
                }));

        switch (choice)
        {
            case "Start New Chat":
                _ = StartNewChat();
                break;
            case "View Active Chats":
                _ = ViewActiveChats();
                break;
            case "Exit":
                Environment.Exit(0);
                break;
        }
    }

    private static async Task HandleWaitingState()
    {
        Console.Clear();
        AnsiConsole.Write(new Rule("[yellow]Waiting for Listener[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.MarkupLine("\n[yellow]Waiting for a listener to accept your chat request...[/]");
        AnsiConsole.MarkupLine("[grey]Press 'C' to cancel the request or 'R' to refresh status[/]\n");

        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.C)
            {
                await EndChat();
                _isWaitingForAcceptance = false;
                _currentChatRoomId = null;
                return;
            }
            else if (key.Key == ConsoleKey.R)
            {
                var chatRoom = await _hubConnection!.InvokeAsync<ChatRoomDto>("GetChatRoomByIdAsync", _currentChatRoomId);
                if (chatRoom != null && chatRoom.Status == "Active")
                {
                    _isWaitingForAcceptance = false;
                    _isInChat = true;
                    Console.Clear();
                    AnsiConsole.MarkupLine($"[green]Chat accepted by Listener {chatRoom.ListenerId}![/]");
                    return;
                }
            }
        }

        await Task.Delay(500);
    }

    private static async Task HandleChatState()
    {
        if (!_chatScreenShown)
        {
            ChatUI.ClearChat();
            ChatUI.RenderChatHeader(_currentChatRoomId?.ToString() ?? "Unknown");
            ChatUI.RenderInputPrompt();
            _chatScreenShown = true;
        }

        var message = AnsiConsole.Ask<string>("Enter message:");

        // Clear any existing typing timer
        if (_typingTimer != null)
        {
            _typingTimer.Stop();
            _typingTimer.Dispose();
            _typingTimer = null;
        }

        if (message.ToLower() == "exit")
        {
            await EndChat();
            _chatScreenShown = false;
            _lastTypingStatus = false;
            await UpdateTypingStatus(false);
        }
        else if (!string.IsNullOrWhiteSpace(message))
        {
            // Create new typing timer
            _typingTimer = new System.Timers.Timer(1000);
            _typingTimer.Elapsed += async (sender, e) =>
            {
                await UpdateTypingStatus(false);
                _lastTypingStatus = false;
            };
            _typingTimer.AutoReset = false;

            if (!_lastTypingStatus)
            {
                await UpdateTypingStatus(true);
                _lastTypingStatus = true;
            }

            await SendMessage(message);
            await MarkMessageAsSeen();

            // Start the timer to clear typing status
            _typingTimer.Start();
        }
    }

    private static async Task InitializeSignalRConnection()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"http://localhost:5050/chathub?userId={_userId}")
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<string, string>("ReceiveMessage", (senderId, message) =>
        {
            if (senderId != _userId)
            {
                ChatUI.RenderMessage(senderId, message, false, DateTime.Now);
                if (_currentChatRoomId.HasValue)
                {
                    _ = MarkMessageAsSeen();
                }
            }
        });

        _hubConnection.On<ChatRoomDto>("ChatAccepted", (chatRoom) =>
        {
            AnsiConsole.MarkupLine($"[green]Chat accepted by Listener {chatRoom.ListenerId}![/]");
            _isWaitingForAcceptance = false;
            _isInChat = true;
            ChatUI.ClearChat();
            ChatUI.RenderChatHeader(chatRoom.ListenerId);
            ChatUI.RenderInputPrompt();
        });

        _hubConnection.On<string>("ChatEnded", (message) =>
        {
            AnsiConsole.MarkupLine($"[red]{message}[/]");
            _isInChat = false;
            _isWaitingForAcceptance = false;
            _currentChatRoomId = null;
        });

        _hubConnection.On<string, bool>("UserTypingStatus", (userId, isTyping) =>
        {
            if (isTyping && userId != _userId)
            {
                ChatUI.RenderTypingIndicator(userId);
            }
        });

        _hubConnection.On<string>("MessagesSeen", (userId) =>
        {
            if (userId != _userId)
            {
                AnsiConsole.MarkupLine($"[grey]✓✓ Seen by {userId}[/]");
            }
        });

        try
        {
            await _hubConnection.StartAsync();
            AnsiConsole.MarkupLine("[green]Connected to chat server![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to connect to server: {ex.Message}[/]");
            Environment.Exit(1);
        }
    }

    private static string PromptForUserId()
    {
        return AnsiConsole.Ask<string>("Enter your user ID:");
    }

    private static async Task StartNewChat()
    {
        Console.Clear();
        AnsiConsole.Write(new Rule("[blue]Start New Chat[/]").RuleStyle("grey").LeftJustified());
        
        var initialMessage = AnsiConsole.Ask<string>("\nEnter your initial message:");
        
        try
        {
            var response = await _hubConnection!.InvokeAsync<ChatRoomDto>("CreateChatRequest", _userId, initialMessage);
            _currentChatRoomId = response.Id;
            _isWaitingForAcceptance = true;
            AnsiConsole.MarkupLine($"\n[green]Chat request created! Waiting for a listener...[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Failed to create chat: {ex.Message}[/]");
            AnsiConsole.MarkupLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }
    }

    private static async Task ViewActiveChats()
    {
        Console.Clear();
        AnsiConsole.Write(new Rule("[blue]Active Chats[/]").RuleStyle("grey").LeftJustified());

        try
        {
            var chats = await _hubConnection!.InvokeAsync<IEnumerable<ChatRoomDto>>("GetActiveChats", _userId);
            var chatsList = chats.ToList();
            
            if (!chatsList.Any())
            {
                AnsiConsole.MarkupLine("\n[yellow]No active chats found.[/]");
                AnsiConsole.MarkupLine("\nPress any key to continue...");
                Console.ReadKey(true);
                return;
            }

            var table = new Table()
                .AddColumn("#")
                .AddColumn("Chat ID")
                .AddColumn("Listener")
                .AddColumn("Status")
                .AddColumn("Created At");

            for (int i = 0; i < chatsList.Count; i++)
            {
                var chat = chatsList[i];
                table.AddRow(
                    (i + 1).ToString(),
                    chat.Id.ToString(), 
                    chat.ListenerId, 
                    chat.Status, 
                    chat.CreatedAt.ToString()
                );
            }

            AnsiConsole.Write(table);

            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("\nSelect a chat to join (or press Esc to cancel):")
                    .AddChoices(
                        chatsList.Select((c, i) => $"{i + 1}. Chat with {c.ListenerId}")
                        .Concat(new[] { "Cancel" })
                    )
            );

            if (selection != "Cancel")
            {
                var index = int.Parse(selection.Split('.')[0]) - 1;
                var selectedChat = chatsList[index];
                
                if (selectedChat.Status == "Active")
                {
                    _currentChatRoomId = selectedChat.Id;
                    _isInChat = true;
                    _chatScreenShown = false;
                    AnsiConsole.MarkupLine($"\n[green]Joined chat with Listener {selectedChat.ListenerId}![/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("\n[red]Cannot join this chat. It may not be active.[/]");
                    AnsiConsole.MarkupLine("\nPress any key to continue...");
                    Console.ReadKey(true);
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Failed to get active chats: {ex.Message}[/]");
            AnsiConsole.MarkupLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }
    }

    private static async Task SendMessage(string message)
    {
        if (_currentChatRoomId == null || !_isInChat)
        {
            AnsiConsole.MarkupLine("[red]You are not in an active chat.[/]");
            return;
        }

        try
        {
            await _hubConnection!.InvokeAsync("SendMessage", _currentChatRoomId, _userId, message);
            ChatUI.RenderMessage("You", message, true, DateTime.Now);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to send message: {ex.Message}[/]");
        }
    }

    private static async Task EndChat()
    {
        if (_currentChatRoomId == null)
        {
            return;
        }

        try
        {
            await _hubConnection!.InvokeAsync("EndChat", _currentChatRoomId);
            _isInChat = false;
            _isWaitingForAcceptance = false;
            _currentChatRoomId = null;
            AnsiConsole.MarkupLine("[yellow]Chat ended.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to end chat: {ex.Message}[/]");
        }
    }

    private static async Task UpdateTypingStatus(bool isTyping)
    {
        if (_currentChatRoomId.HasValue && _lastTypingStatus != isTyping)
        {
            try
            {
                await _hubConnection!.InvokeAsync("UpdateTypingStatus", _currentChatRoomId, _userId, isTyping);
                _lastTypingStatus = isTyping;
            }
            catch (Exception)
            {
                // Silently handle typing status errors
            }
        }
    }

    private static async Task MarkMessageAsSeen()
    {
        if (_currentChatRoomId.HasValue)
        {
            try
            {
                await _hubConnection!.InvokeAsync("MarkMessageAsSeen", _currentChatRoomId, _userId);
            }
            catch (Exception ex)
            {
                // Silently handle seen status errors
            }
        }
    }
}
