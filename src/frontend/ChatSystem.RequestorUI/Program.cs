using Microsoft.AspNetCore.SignalR.Client;
using Spectre.Console;
using ChatSystem.UI.Shared.Components;
using ChatSystem.UI.Shared.Services;
using ChatSystem.UI.Shared.DTOs;

namespace ChatSystem.RequestorUI;

public static class StringExtensions
{
    public static string SplitCamelCase(this string str)
    {
        return string.Concat(str.Select((x, i) => i > 0 && char.IsUpper(x) ? " " + x : x.ToString()));
    }
}

public class Program
{
    private static ChatService? _chatService;
    private static string? _userId;
    private static bool _isWaitingForAcceptance = false;

    public static async Task Main(string[] args)
    {
        ChatComponents.RenderHeader("Chat Requestor", Color.Blue);
        
        _userId = PromptForUserId();
        _chatService = new ChatService(_userId, "http://localhost:5050/chathub");
        
        await InitializeChat();
        await RunMainLoop();
    }

    private static async Task InitializeChat()
    {
        await _chatService!.InitializeConnection();
        _chatService.RegisterBaseEvents();
        RegisterRequestorEvents();
    }

    private static async Task RunMainLoop()
    {
        while (true)
        {
            if (!_chatService!.IsInChat && !_isWaitingForAcceptance)
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
            await Task.Delay(100);
        }
    }

    private static void RegisterRequestorEvents()
    {
        _chatService!.Connection.On<ChatRoomDto>("ChatAccepted", (chatRoom) =>
        {
            ChatComponents.RenderSuccess($"Chat accepted by Listener {chatRoom.ListenerId}!");
            _isWaitingForAcceptance = false;
            _chatService.SetChatState(chatRoom.Id, true);
        });
    }

    private static async Task ShowMainMenu()
    {
        ChatComponents.RenderHeader("Main Menu", Color.Blue);
        
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
        ChatComponents.RenderHeader("Waiting for Listener", Color.Yellow);
        ChatComponents.RenderInfo("Waiting for a listener to accept your chat request...");
        AnsiConsole.MarkupLine("[grey]Press 'C' to cancel the request or 'R' to refresh status[/]");

        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.C)
            {
                await _chatService!.EndChat();
                _isWaitingForAcceptance = false;
                return;
            }
            else if (key.Key == ConsoleKey.R)
            {
                var chatRoom = await _chatService!.Connection.InvokeAsync<ChatRoomDto>(
                    "GetChatRoomByIdAsync", 
                    _chatService.CurrentChatRoomId
                );
                
                if (chatRoom != null && chatRoom.Status == "Active")
                {
                    _isWaitingForAcceptance = false;
                    _chatService.SetChatState(chatRoom.Id, true);
                    ChatComponents.RenderSuccess($"Chat accepted by Listener {chatRoom.ListenerId}!");
                    return;
                }
            }
        }
    }

    private static async Task HandleChatState()
    {
        var message = AnsiConsole.Ask<string>("\nEnter message (type 'exit' to end chat):");

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (message.ToLower() == "exit")
        {
            await _chatService!.EndChat();
            return;
        }

        await _chatService!.ManageTypingStatus();
        await _chatService.SendMessage(message);
    }

    private static string PromptForUserId()
    {
        while (true)
        {
            var userId = AnsiConsole.Ask<string>("Enter your user ID:").Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                ChatComponents.RenderError("User ID cannot be empty.");
                continue;
            }
            if (userId.Length > 50)
            {
                ChatComponents.RenderError("User ID is too long. Maximum length is 50 characters.");
                continue;
            }
            if (!userId.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
            {
                ChatComponents.RenderError("User ID can only contain letters, numbers, underscores, and hyphens.");
                continue;
            }
            return userId;
        }
    }

    private static async Task StartNewChat()
    {
        ChatComponents.RenderHeader("Start New Chat", Color.Blue);

        var requestTypes = new[] { "Hardware", "Software", "Network" };
        var requestType = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What type of support are you looking for?")
                .AddChoices(requestTypes)
        );

        var message = AnsiConsole.Ask<string>("What would you like to discuss?");

        try
        {
            var request = new CreateChatRequestDto(
                _userId!,
                requestType,
                message
            );

            var response = await _chatService!.Connection.InvokeAsync<ChatRoomDto>("CreateChatRequest", request);
            _chatService.SetChatState(response.Id, false);
            _isWaitingForAcceptance = true;
            
            ChatComponents.RenderSuccess("Chat request created! Waiting for a listener...");
            ChatComponents.RenderInfo("Press 'C' to cancel the request or 'R' to refresh status");
        }
        catch (Exception ex)
        {
            ChatComponents.RenderError($"Failed to create chat: {ex.Message}");
            AnsiConsole.MarkupLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }
    }

    private static async Task ViewActiveChats()
    {
        try
        {
            var chats = await _chatService!.Connection.InvokeAsync<IEnumerable<ChatRoomDto>>("GetActiveChats", _userId);
            var chatsList = chats.ToList();

            ChatComponents.RenderHeader("Active Chats", Color.Blue);

            if (!chatsList.Any())
            {
                ChatComponents.RenderInfo("No active chats found.");
                AnsiConsole.MarkupLine("\nPress any key to return to main menu...");
                Console.ReadKey(true);
                return;
            }

            var table = new Table()
                .AddColumn(new TableColumn("#").Centered())
                .AddColumn(new TableColumn("Chat ID").NoWrap())
                .AddColumn(new TableColumn("Listener").NoWrap())
                .AddColumn(new TableColumn("Type").NoWrap())
                .AddColumn(new TableColumn("Status").NoWrap())
                .AddColumn(new TableColumn("Created At").NoWrap());

            for (int i = 0; i < chatsList.Count; i++)
            {
                var chat = chatsList[i];
                table.AddRow(
                    (i + 1).ToString(),
                    chat.Id.ToString(),
                    chat.ListenerId ?? "Waiting...",
                    chat.RequestType,
                    chat.Status,
                    chat.CreatedAt.ToString("g")
                );
            }

            ChatComponents.RenderTable("Active Chats", table, chatsList);

            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a chat to join:")
                    .PageSize(10)
                    .AddChoices(
                        chatsList.Select((c, i) => $"{i + 1}. Chat with {c.ListenerId ?? "Pending"}")
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
                _chatService.SetChatState(selectedChat.Id, true);
                await _chatService.RenderChatScreen(
                    selectedChat.ListenerId ?? "Unknown",
                    selectedChat.RequestType
                );
            }
            else
            {
                ChatComponents.RenderError("Cannot join this chat. It may not be active.");
                await Task.Delay(2000);
            }
        }
        catch (Exception ex)
        {
            ChatComponents.RenderError($"Failed to get active chats: {ex.Message}");
            await Task.Delay(2000);
        }
    }
}
