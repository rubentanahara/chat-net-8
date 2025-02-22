using Microsoft.AspNetCore.SignalR.Client;
using Spectre.Console;
using ChatSystem.UI.Shared;

namespace ChatSystem.RequestorUI;

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
                await ShowMainMenu();
            }
            else if (_isWaitingForAcceptance)
            {
                await HandleWaitingState();
            }
            else
            {
                await HandleChatState();
            }
            await Task.Delay(100); // Small delay to prevent CPU spinning
        }
    }

    private static void ShowWelcomeScreen()
    {
        Console.Clear();
        AnsiConsole.Write(new FigletText("Chat Requestor").Color(Color.Blue));
    }

    private static async Task ShowMainMenu()
    {
        Console.Clear();
        AnsiConsole.Write(new Rule("[blue]Main Menu[/]").RuleStyle("grey").LeftJustified());
        
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .HighlightStyle(new Style(foreground: Color.Green))
                .AddChoices(new[]
                {
                    "Start New Chat",
                    "View Active Chats",
                    "Exit"
                }));

        switch (choice)
        {
            case "Start New Chat":
                await StartNewChat();
                break;
            case "View Active Chats":
                await ViewActiveChats();
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
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Waiting for a listener to accept your chat request...[/]");
        AnsiConsole.MarkupLine("[grey]Press 'C' to cancel the request or 'R' to refresh status[/]");
        AnsiConsole.WriteLine();

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
                    _chatScreenShown = false;
                    Console.Clear();
                    AnsiConsole.MarkupLine($"[green]Chat accepted by Listener {chatRoom.ListenerId}![/]");
                    return;
                }
            }
        }

        await Task.Delay(100); // Small delay to prevent CPU spinning
    }

    private static async Task HandleChatState()
    {
        if (!_chatScreenShown)
        {
            await RenderChatScreen();
        }

        var message = AnsiConsole.Ask<string>("Enter message:");

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (message.Length > 1000)
        {
            AnsiConsole.MarkupLine("[red]Message is too long. Maximum length is 1000 characters.[/]");
            return;
        }

        await ManageTypingTimer();

        if (message.ToLower() == "exit")
        {
            await EndChat();
            return;
        }

        await SendMessage(message);
        await MarkMessageAsSeen();
    }

    private static async Task RenderChatScreen()
    {
        ChatUI.ClearChat();
        ChatUI.RenderChatHeader(_currentChatRoomId?.ToString() ?? "Unknown");

        if (_currentChatRoomId.HasValue)
        {
            try
            {
                var history = await _hubConnection!.InvokeAsync<IEnumerable<ChatMessageDto>>("GetChatHistoryAsync", _currentChatRoomId.Value);
                foreach (var msg in history)
                {
                    ChatUI.RenderMessage(
                        msg.SenderId == _userId ? "You" : msg.SenderId,
                        msg.Content,
                        msg.SenderId == _userId,
                        msg.Timestamp
                    );
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to load chat history: {ex.Message}[/]");
            }
        }

        ChatUI.RenderInputPrompt();
        _chatScreenShown = true;
    }

    private static async Task ManageTypingTimer()
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

    private static async Task InitializeSignalRConnection()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"http://localhost:5050/chathub?userId={_userId}")
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

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

        RegisterSignalREvents();

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

    private static void RegisterSignalREvents()
    {
        if (_hubConnection == null)
        {
            Console.WriteLine("Hub connection is not established.");
            return;
        }

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
            if (userId != _userId)
            {
                if (isTyping)
                {
                    ChatUI.RenderTypingIndicator(userId);
                }
                else
                {
                    ChatUI.ClearTypingIndicator();
                }
            }
        });

        _hubConnection.On<string>("MessagesSeen", (userId) =>
        {
            if (userId != _userId)
            {
                AnsiConsole.MarkupLine($"[grey]✓✓ Seen by {userId}[/]");
            }
        });
    }

    private static async Task ReconnectAsync()
    {
        if (_hubConnection == null)
        {
            AnsiConsole.MarkupLine("[red]Hub connection is not established. Cannot reconnect.[/]");
            return;
        }

        AnsiConsole.MarkupLine("[yellow]Attempting to reconnect...[/]");
        await Task.Delay(new Random().Next(0, 5) * 1000);
        await _hubConnection.StartAsync();
    }

    private static void LogConnectionEvent(string eventDescription, Exception? error)
    {
        if (error != null)
        {
            AnsiConsole.MarkupLine($"[red]{eventDescription}: {error.Message}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]{eventDescription}[/]");
        }
    }

    private static string PromptForUserId()
    {
        while (true)
        {
            var userId = AnsiConsole.Ask<string>("Enter your user ID:").Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                AnsiConsole.MarkupLine("[red]User ID cannot be empty.[/]");
                continue;
            }
            if (userId.Length > 50)
            {
                AnsiConsole.MarkupLine("[red]User ID is too long. Maximum length is 50 characters.[/]");
                continue;
            }
            if (!userId.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
            {
                AnsiConsole.MarkupLine("[red]User ID can only contain letters, numbers, underscores, and hyphens.[/]");
                continue;
            }
            return userId;
        }
    }

    private static async Task StartNewChat()
    {
        Console.Clear();
        AnsiConsole.Write(new Rule("[blue]Start New Chat[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();
        
        var initialMessage = AnsiConsole.Ask<string>("Enter your initial message:");
        
        try
        {
            var response = await _hubConnection!.InvokeAsync<ChatRoomDto>("CreateChatRequest", _userId, initialMessage);
            _currentChatRoomId = response.Id;
            _isWaitingForAcceptance = true;
            AnsiConsole.MarkupLine($"\n[green]Chat request created! Waiting for a listener...[/]");
            AnsiConsole.MarkupLine("[grey]Press 'C' to cancel the request or 'R' to refresh status[/]");
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
        try
        {
            while (true)
            {
                Console.Clear();
                AnsiConsole.Write(new Rule("[blue]Active Chats[/]").RuleStyle("grey").LeftJustified());
                AnsiConsole.WriteLine();

                var chats = await _hubConnection!.InvokeAsync<IEnumerable<ChatRoomDto>>("GetActiveChats", _userId);
                var chatsList = chats.ToList();

                if (!chatsList.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]No active chats found.[/]");
                    AnsiConsole.MarkupLine("\nPress any key to return to main menu...");
                    Console.ReadKey(true);
                    return;
                }

                var table = new Table()
                    .AddColumn(new TableColumn("#").Centered())
                    .AddColumn(new TableColumn("Chat ID").NoWrap())
                    .AddColumn(new TableColumn("Listener").NoWrap())
                    .AddColumn(new TableColumn("Status").NoWrap())
                    .AddColumn(new TableColumn("Created At").NoWrap());

                for (int i = 0; i < chatsList.Count; i++)
                {
                    var chat = chatsList[i];
                    table.AddRow(
                        (i + 1).ToString(),
                        chat.Id.ToString(),
                        chat.ListenerId,
                        chat.Status,
                        chat.CreatedAt.ToString("g")
                    );
                }

                AnsiConsole.Write(table);
                AnsiConsole.WriteLine();

                var selection = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a chat to join:")
                        .PageSize(10)
                        .MoreChoicesText("[grey](Move up and down to reveal more chats)[/]")
                        .AddChoices(
                            chatsList.Select((c, i) => $"{i + 1}. Chat with {c.ListenerId}")
                            .Concat(new[] { "Back to Main Menu" })
                        )
                );

                if (selection == "Back to Main Menu")
                {
                    return;
                }

                var index = int.Parse(selection.Split('.')[0]) - 1;
                var selectedChat = chatsList[index];
                
                if (selectedChat.Status == "Active")
                {
                    _currentChatRoomId = selectedChat.Id;
                    _isInChat = true;
                    _chatScreenShown = false;
                    AnsiConsole.MarkupLine($"\n[green]Joined chat with Listener {selectedChat.ListenerId}![/]");
                    return;
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
            AnsiConsole.MarkupLine("\nPress any key to return to main menu...");
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

        message = SanitizeMessage(message);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (message.Length > 1000)
        {
            AnsiConsole.MarkupLine("[red]Message is too long. Maximum length is 1000 characters.[/]");
            return;
        }

        try
        {
            await _hubConnection!.InvokeAsync("SendMessage", _currentChatRoomId, _userId, message);
            Console.WriteLine(); // Add a line break before the message
            ChatUI.RenderMessage("You", message, true, DateTime.Now);
            ChatUI.RenderInputPrompt(); // Re-render the input prompt
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
            catch
            {
                // Silently handle seen status errors
            }
        }
    }

    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        // Remove control characters
        message = new string(message.Where(c => !char.IsControl(c)).ToArray());
        
        // Trim whitespace
        message = message.Trim();
        
        // Replace multiple spaces with single space
        message = string.Join(" ", message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        
        return message;
    }
}
