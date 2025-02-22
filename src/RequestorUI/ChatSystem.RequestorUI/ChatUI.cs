using Spectre.Console;

namespace ChatSystem.RequestorUI;

public static class ChatUI
{
    private static bool _isTypingIndicatorShown = false;
    private static readonly object _lockObject = new object();

    public static void RenderMessage(string senderId, string content, bool isOwnMessage, DateTime timestamp, bool isSeen = false)
    {
        lock (_lockObject)
        {
            ClearTypingIndicator();
            
            var panel = new Panel(content)
            {
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0),
                Expand = false
            };

            if (isOwnMessage)
            {
                panel.BorderStyle = new Style(Color.Green);
                AnsiConsole.Write(new Rows(
                    new Text($"You • {timestamp:HH:mm}", new Style(Color.Grey)).RightJustified(),
                    new Padder(panel, new Padding(40, 0, 0, 0))
                ));
                if (isSeen)
                {
                    AnsiConsole.Write(new Padder(
                        new Text("✓✓ Seen", new Style(Color.Grey)),
                        new Padding(70, 0, 0, 0)
                    ));
                }
            }
            else
            {
                panel.BorderStyle = new Style(Color.Blue);
                AnsiConsole.Write(new Rows(
                    new Text($"{senderId} • {timestamp:HH:mm}", new Style(Color.Grey)),
                    new Padder(panel, new Padding(0, 0, 40, 0))
                ));
            }
            AnsiConsole.WriteLine();
        }
    }

    public static void RenderTypingIndicator(string userId)
    {
        lock (_lockObject)
        {
            if (!_isTypingIndicatorShown)
            {
                ClearCurrentLine();
                var panel = new Panel("...")
                {
                    Border = BoxBorder.Rounded,
                    Padding = new Padding(1, 0, 1, 0),
                    Expand = false,
                    BorderStyle = new Style(Color.Grey)
                };

                AnsiConsole.Write(new Rows(
                    new Text($"{userId} is typing...", new Style(Color.Grey)),
                    new Padder(panel, new Padding(0, 0, 40, 0))
                ));
                _isTypingIndicatorShown = true;
            }
        }
    }

    public static void ClearTypingIndicator()
    {
        lock (_lockObject)
        {
            if (_isTypingIndicatorShown)
            {
                ClearCurrentLine();
                _isTypingIndicatorShown = false;
            }
        }
    }

    private static void ClearCurrentLine()
    {
        int currentLineCursor = Console.CursorTop;
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write(new string(' ', Console.WindowWidth));
        Console.SetCursorPosition(0, currentLineCursor);
    }

    public static void RenderChatHeader(string otherUserId)
    {
        AnsiConsole.Write(new Rule($"[blue]Chat with {otherUserId}[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();
    }

    public static void RenderInputPrompt()
    {
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]Type your message and press Enter to send. Type 'exit' to end chat.[/]");
    }

    public static void ClearChat()
    {
        Console.Clear();
        _isTypingIndicatorShown = false;
    }
} 