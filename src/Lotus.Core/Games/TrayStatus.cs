namespace Lotus.Core.Games;

// The tray icon's quiet status line: several parts of Lotus can each own one line (the game crashed, Modo Console is not
// active...). Nothing here pops up: the line is read in the icon's tooltip and at the top of its menu.
public sealed class TrayStatus
{
    private readonly List<(string Key, string Text)> lines = [];
    public event EventHandler<string?>? Changed;
    public string? Line => lines.Count == 0 ? null : string.Join(" · ", lines.Select(l => l.Text));

    public void Set(string key, string? text)
    {
        var before = Line;
        lines.RemoveAll(l => l.Key == key);
        if (text is { Length: > 0 }) lines.Add((key, text));
        if (Line != before) Changed?.Invoke(this, Line);
    }
    public void Clear(string key) => Set(key, null);
}

// Windows balloons wait while a game is open (Gothic, 27/11/2026: "Sessão registrada no diário" stayed over the whole next
// session). They are shown, oldest first, once no game is open; a full queue drops the oldest.
public sealed class NoticeQueue(Action<string, string> show, int capacity = 4)
{
    private readonly Queue<(string Title, string Body)> waiting = new();
    private bool gameOpen;
    public int Waiting => waiting.Count;
    public bool GameOpen => gameOpen;

    public void Notify(string title, string body)
    {
        if (!gameOpen) { show(title, body); return; }
        if (waiting.Any(w => w.Title == title && w.Body == body)) return;
        if (waiting.Count >= capacity) waiting.Dequeue();
        waiting.Enqueue((title, body));
    }

    // true when a game just opened (the caller takes away a balloon still on screen).
    public bool SetGameOpen(bool open)
    {
        var opened = open && !gameOpen;
        gameOpen = open;
        if (!open && waiting.Count > 0)
        {
            // One balloon replaces the previous one on Windows: the waiting ones go out together, newest title first.
            var all = waiting.ToArray(); waiting.Clear();
            var body = all.Length == 1 ? all[0].Body : string.Join("\n", all.Reverse().Select(n => n.Body));
            show(all[^1].Title, body.Length > 255 ? body[..254] + "…" : body);
        }
        return opened;
    }
}
