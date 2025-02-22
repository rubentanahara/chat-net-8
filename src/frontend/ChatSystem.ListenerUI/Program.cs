using Microsoft.AspNetCore.SignalR.Client;
using Spectre.Console;
using ChatSystem.UI.Shared;

namespace ChatSystem.ListenerUI;

public class Program
{
    private static HubConnection? _hubConnection;
    private static string? _userId;
    private static Guid? _currentChatRoomId;
    private static bool _isInChat = false;
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
            if (!_isInChat)
            {
                await ShowMainMenu();
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
        AnsiConsole.Write(new FigletText("Chat Listener").Color(Color.Green));
    }

    private static async Task ShowMainMenu()
    {
        Console.Clear();
        AnsiConsole.Write(new Rule("[green]Main Menu[/]").RuleStyle("grey").LeftJustified());
        
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .HighlightStyle(new Style(foreground: Color.Green))
                .AddChoices(new[]
                {
                    "View Pending Requests",
                    "View Active Chats",
                    "Exit"
                }));

        switch (choice)
        {
            case "View Pending Requests":
                await ViewPendingRequests();
                break;
            case "View Active Chats":
                await ViewActiveChats();
                break;
            case "Exit":
                Environment.Exit(0);
                break;
        }
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
                Console.WriteLine(); // Add a line break before the message
                ChatUI.RenderMessage(senderId, message, false, DateTime.Now);
                ChatUI.RenderInputPrompt(); // Re-render the input prompt
                if (_currentChatRoomId.HasValue)
                {
                    _ = MarkMessageAsSeen();
                }
            }
        });

        _hubConnection.On<ChatRoomDto>("ChatAccepted", (chatRoom) =>
        {
            if (chatRoom.ListenerId == _userId)
            {
                _currentChatRoomId = chatRoom.Id;
                _isInChat = true;
                _chatScreenShown = false;
                ChatUI.ClearChat();
                ChatUI.RenderChatHeader(chatRoom.RequestorId);
                ChatUI.RenderInputPrompt();
            }
        });

        _hubConnection.On<string>("ChatEnded", (message) =>
        {
            Console.WriteLine(); // Add a line break before the message
            AnsiConsole.MarkupLine($"[red]{message}[/]");
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
                    ChatUI.RenderTypingIndicator(userId);
                }
                else
                {
                    ChatUI.ClearTypingIndicator();
                }
                ChatUI.RenderInputPrompt(); // Re-render the input prompt
            }
        });

        _hubConnection.On<string>("MessagesSeen", (userId) =>
        {
            if (userId != _userId)
            {
                Console.WriteLine(); // Add a line break before the seen status
                AnsiConsole.MarkupLine($"[grey]✓✓ Seen by {userId}[/]");
                ChatUI.RenderInputPrompt(); // Re-render the input prompt
            }
        });

        _hubConnection.On<ChatRoomDto>("NewChatRequest", (chatRoom) =>
        {
            if (!_isInChat)
            {
                AnsiConsole.MarkupLine($"[yellow]New chat request from {chatRoom.RequestorId}![/]");
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

    private static async Task ViewPendingRequests()
    {
        try
        {
            var requests = await _hubConnection!.InvokeAsync<IEnumerable<ChatRoomDto>>("GetPendingRequests");
            var requestsList = requests.ToList();

            while (true)
            {
                Console.Clear();
                AnsiConsole.Write(new Rule("[green]Pending Chat Requests[/]").RuleStyle("grey").LeftJustified());
                AnsiConsole.WriteLine();

                if (!requestsList.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]No pending chat requests found.[/]");
                    AnsiConsole.MarkupLine("\nPress any key to return to main menu...");
                    Console.ReadKey(true);
                    return;
                }

                var table = new Table()
                    .AddColumn(new TableColumn("#").Centered())
                    .AddColumn(new TableColumn("Request ID").NoWrap())
                    .AddColumn(new TableColumn("Requestor").NoWrap())
                    .AddColumn(new TableColumn("Created At").NoWrap());

                for (int i = 0; i < requestsList.Count; i++)
                {
                    var request = requestsList[i];
                    table.AddRow(
                        (i + 1).ToString(),
                        request.Id.ToString(),
                        request.RequestorId,
                        request.CreatedAt.ToString("g")
                    );
                }

                AnsiConsole.Write(table);
                AnsiConsole.WriteLine();

                var selection = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a request to accept:")
                        .PageSize(10)
                        .MoreChoicesText("[grey](Move up and down to reveal more requests)[/]")
                        .AddChoices(
                            requestsList.Select((r, i) => $"{i + 1}. Request from {r.RequestorId}")
                            .Concat(new[] { "Back to Main Menu" })
                        )
                );

                if (selection == "Back to Main Menu")
                {
                    return;
                }

                var index = int.Parse(selection.Split('.')[0]) - 1;
                var selectedRequest = requestsList[index];
                await AcceptChatRequest(selectedRequest.Id);
                if (_isInChat)
                {
                    return;
                }

                // Refresh the requests list
                requests = await _hubConnection!.InvokeAsync<IEnumerable<ChatRoomDto>>("GetPendingRequests");
                requestsList = requests.ToList();
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Failed to get pending requests: {ex.Message}[/]");
            AnsiConsole.MarkupLine("\nPress any key to return to main menu...");
            Console.ReadKey(true);
        }
    }

    private static async Task ViewActiveChats()
    {
        try
        {
            var chats = await _hubConnection!.InvokeAsync<IEnumerable<ChatRoomDto>>("GetActiveChats", _userId);
            var chatsList = chats.ToList();
            
            var table = new Table()
                .AddColumn(new TableColumn("#").Centered())
                .AddColumn(new TableColumn("Chat ID").NoWrap())
                .AddColumn(new TableColumn("Requestor").NoWrap())
                .AddColumn(new TableColumn("Status").NoWrap())
                .AddColumn(new TableColumn("Created At").NoWrap());

            for (int i = 0; i < chatsList.Count; i++)
            {
                var chat = chatsList[i];
                table.AddRow(
                    (i + 1).ToString(),
                    chat.Id.ToString(),
                    chat.RequestorId,
                    chat.Status,
                    chat.CreatedAt.ToString("g")
                );
            }

            ChatUI.RenderTable("Active Chats", table, chatsList);

            if (chatsList.Any())
            {
                var selection = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("\nSelect a chat to join (or press Esc to cancel):")
                        .AddChoices(
                            chatsList.Select((c, i) => $"{i + 1}. Chat with {c.RequestorId}")
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
                        AnsiConsole.MarkupLine($"\n[green]Joined chat with Requestor {selectedChat.RequestorId}![/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("\n[red]Cannot join this chat. It may not be active.[/]");
                        AnsiConsole.MarkupLine("\nPress any key to continue...");
                        Console.ReadKey(true);
                    }
                }
            }
            else
            {
                AnsiConsole.MarkupLine("\nPress any key to continue...");
                Console.ReadKey(true);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Failed to get active chats: {ex.Message}[/]");
            AnsiConsole.MarkupLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }
    }

    private static async Task AcceptChatRequest(Guid chatRoomId)
    {
        try
        {
            await _hubConnection!.InvokeAsync("AcceptChatRequest", chatRoomId, _userId);
            _currentChatRoomId = chatRoomId;
            _isInChat = true;
            _chatScreenShown = false;
            AnsiConsole.MarkupLine("\n[green]Chat request accepted![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Failed to accept chat: {ex.Message}[/]");
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
            await CleanupChatState();
            AnsiConsole.MarkupLine("[yellow]Chat ended.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to end chat: {ex.Message}[/]");
        }
    }

    private static async Task CleanupChatState()
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
}
