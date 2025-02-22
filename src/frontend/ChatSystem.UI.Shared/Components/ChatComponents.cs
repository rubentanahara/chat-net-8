using Spectre.Console;

namespace ChatSystem.UI.Shared.Components;

public static class ChatComponents
{
    public static void RenderHeader(string title, Color color)
    {
        Console.Clear();
        AnsiConsole.Write(new FigletText(title).Color(color));
        AnsiConsole.Write(new Rule($"[{color.ToMarkup()}]{title}[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();
    }

    public static void RenderChatHeader(string chatId, string partnerName, string requestType = "")
    {
        Console.Clear();
        var headerText = $"Chat with {partnerName}";
        if (!string.IsNullOrEmpty(requestType))
        {
            headerText += $"\nRequest Type: {requestType}";
        }
        
        var header = new Panel(headerText)
            .Header($"Chat ID: {chatId}")
            .Padding(1, 1, 1, 1)
            .Border(BoxBorder.Rounded);

        AnsiConsole.Write(header);
        AnsiConsole.WriteLine();
    }

    public static void RenderMessage(string sender, string content, bool isCurrentUser, DateTime timestamp)
    {
        var messageColor = isCurrentUser ? Color.Blue : Color.Green;
        var alignment = isCurrentUser ? Justify.Right : Justify.Left;
        var prefix = isCurrentUser ? "→" : "←";

        var panel = new Panel(content)
            .Header($"{prefix} {sender}")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(messageColor))
            .Padding(1, 1, 0, 0);

        AnsiConsole.Write(panel);
        AnsiConsole.MarkupLine($"[grey]{timestamp:HH:mm}[/]");
        AnsiConsole.WriteLine();
    }

    public static void RenderTypingIndicator(string userId)
    {
        var spinner = Spinner.Known.Dots;
        AnsiConsole.MarkupLine($"[grey]{userId} is typing{spinner}[/]");
    }

    public static void RenderSeenStatus(string userId)
    {
        AnsiConsole.MarkupLine($"[grey]✓✓ Seen by {userId}[/]");
    }

    public static void RenderInputPrompt()
    {
        AnsiConsole.Markup("\n[blue]Enter message:[/] ");
    }

    public static void RenderError(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error: {message}[/]");
    }

    public static void RenderSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]Success: {message}[/]");
    }

    public static void RenderInfo(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]Info: {message}[/]");
    }

    public static void RenderTable<T>(string title, Table table, IEnumerable<T> items)
    {
        if (!items.Any())
        {
            AnsiConsole.MarkupLine($"[yellow]No {title.ToLower()} found.[/]");
            return;
        }

        var panel = new Panel(table)
            .Header(title)
            .Padding(1, 1, 1, 1)
            .Border(BoxBorder.Rounded);

        AnsiConsole.Write(panel);
    }
} 