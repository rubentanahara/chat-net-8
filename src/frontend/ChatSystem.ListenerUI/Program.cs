using Microsoft.AspNetCore.SignalR.Client;
using Spectre.Console;
using ChatSystem.UI.Shared.Components;
using ChatSystem.UI.Shared.Services;
using ChatSystem.UI.Shared.DTOs;

namespace ChatSystem.ListenerUI;

public class Program
{
    private static ChatService? _chatService;
    private static string? _userId;

    public static async Task Main(string[] args)
    {
        ChatComponents.RenderHeader("Chat Listener", Color.Green);
        
        _userId = PromptForUserId();
        _chatService = new ChatService(_userId, "http://localhost:5050/chathub");
        
        await InitializeChat();
        await RunMainLoop();
    }

    private static async Task InitializeChat()
    {
        await _chatService!.InitializeConnection();
        _chatService.RegisterBaseEvents();
        RegisterListenerEvents();
    }

    private static async Task RunMainLoop()
    {
        while (true)
        {
            if (!_chatService!.IsInChat)
            {
                await ShowMainMenu();
            }
            else
            {
                await HandleChatState();
            }
            await Task.Delay(100);
        }
    }

    private static void RegisterListenerEvents()
    {
        _chatService!.Connection.On<ChatRoomDto>("ChatAccepted", (chatRoom) =>
        {
            if (chatRoom.ListenerId == _userId)
            {
                _chatService.SetChatState(chatRoom.Id, true);
                ChatComponents.RenderSuccess($"Chat accepted with {chatRoom.RequestorId}!");
            }
        });

        _chatService.Connection.On<ChatRoomDto>("NewChatRequest", (chatRoom) =>
        {
            if (!_chatService.IsInChat)
            {
                ChatComponents.RenderInfo($"New chat request from {chatRoom.RequestorId}!");
            }
        });
    }

    private static async Task ShowMainMenu()
    {
        ChatComponents.RenderHeader("Main Menu", Color.Green);
        
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

    private static async Task ViewPendingRequests()
    {
        try
        {
            var requests = await _chatService!.Connection.InvokeAsync<IEnumerable<ChatRoomDto>>("GetPendingRequests");
            var requestsList = requests.ToList();

            ChatComponents.RenderHeader("Pending Chat Requests", Color.Green);

            if (!requestsList.Any())
            {
                ChatComponents.RenderInfo("No pending chat requests found.");
                await Task.Delay(2000);
                return;
            }

            var table = new Table()
                .AddColumn(new TableColumn("#").Centered())
                .AddColumn(new TableColumn("Request ID").NoWrap())
                .AddColumn(new TableColumn("Requestor").NoWrap())
                .AddColumn(new TableColumn("Type").NoWrap())
                .AddColumn(new TableColumn("Initial Message").NoWrap())
                .AddColumn(new TableColumn("Created At").NoWrap());

            for (int i = 0; i < requestsList.Count; i++)
            {
                var request = requestsList[i];
                table.AddRow(
                    (i + 1).ToString(),
                    request.Id.ToString(),
                    request.RequestorId,
                    request.RequestType,
                    request.InitialMessage.Length > 30 
                        ? request.InitialMessage[..30] + "..." 
                        : request.InitialMessage,
                    request.CreatedAt.ToString("g")
                );
            }

            ChatComponents.RenderTable("Pending Requests", table, requestsList);

            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a request to accept:")
                    .PageSize(10)
                    .AddChoices(
                        requestsList.Select((r, i) => $"{i + 1}. Request from {r.RequestorId} ({r.RequestType})")
                        .Concat(new[] { "Back to Main Menu" })
                    )
            );

            if (selection == "Back to Main Menu")
            {
                return;
            }

            var index = int.Parse(selection.Split('.')[0]) - 1;
            var selectedRequest = requestsList[index];

            try
            {
                await _chatService.Connection.InvokeAsync("AcceptChatRequest", selectedRequest.Id, _userId);
                _chatService.SetChatState(selectedRequest.Id, true);
                await _chatService.RenderChatScreen(
                    selectedRequest.RequestorId,
                    selectedRequest.RequestType
                );
                ChatComponents.RenderSuccess($"Chat request from {selectedRequest.RequestorId} accepted!");
            }
            catch (Exception ex)
            {
                ChatComponents.RenderError($"Failed to accept chat: {ex.Message}");
                await Task.Delay(2000);
            }
        }
        catch (Exception ex)
        {
            ChatComponents.RenderError($"Failed to get pending requests: {ex.Message}");
            await Task.Delay(2000);
        }
    }

    private static async Task ViewActiveChats()
    {
        try
        {
            var chats = await _chatService!.Connection.InvokeAsync<IEnumerable<ChatRoomDto>>("GetActiveChats", _userId);
            var chatsList = chats.ToList();

            ChatComponents.RenderHeader("Active Chats", Color.Green);

            if (!chatsList.Any())
            {
                ChatComponents.RenderInfo("No active chats found.");
                await Task.Delay(2000);
                return;
            }

            var table = new Table()
                .AddColumn(new TableColumn("#").Centered())
                .AddColumn(new TableColumn("Chat ID").NoWrap())
                .AddColumn(new TableColumn("Requestor").NoWrap())
                .AddColumn(new TableColumn("Type").NoWrap())
                .AddColumn(new TableColumn("Status").NoWrap())
                .AddColumn(new TableColumn("Created At").NoWrap());

            for (int i = 0; i < chatsList.Count; i++)
            {
                var chat = chatsList[i];
                table.AddRow(
                    (i + 1).ToString(),
                    chat.Id.ToString(),
                    chat.RequestorId,
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
                        chatsList.Select((c, i) => $"{i + 1}. Chat with {c.RequestorId} ({c.RequestType})")
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
                    selectedChat.RequestorId,
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
