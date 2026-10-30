using System.Diagnostics;

namespace UuDok.WinHelper;

/// <summary>
/// Beskytter brukerens egne Office-vinduer. Et program avsluttes bare når vi kan påvise at
/// automasjonen startet en NY prosess. Har COM koblet seg til en instans brukeren allerede kjører,
/// lukkes bare våre egne dokumenter – aldri programmet.
/// </summary>
public sealed class OfficeSession
{
    public dynamic App { get; }
    public bool Owned { get; }
    public IReadOnlyList<int> OwnedPids { get; }

    private OfficeSession(dynamic app, bool owned, List<int> pids) { App = app; Owned = owned; OwnedPids = pids; }

    public static OfficeSession Start(string progId, string processName)
    {
        var before = Process.GetProcessesByName(processName).Select(p => p.Id).ToHashSet();
        var t = Type.GetTypeFromProgID(progId) ?? throw new InvalidOperationException($"{progId} er ikke installert.");
        dynamic app = Activator.CreateInstance(t)!;
        var mine = Process.GetProcessesByName(processName).Select(p => p.Id).Where(id => !before.Contains(id)).ToList();
        if (Environment.GetEnvironmentVariable("UUDOK_PIDFILE") is { } pf && mine.Count > 0)
            File.WriteAllText(pf, string.Join(",", mine));
        return new OfficeSession(app, mine.Count == 1, mine);
    }

    /// <summary>Avslutter programmet bare hvis vi eier prosessen og den ikke har andre dokumenter åpne.</summary>
    public void QuitIfOwned(bool word)
    {
        if (!Owned) return;
        try
        {
            int open = word ? (int)App.Documents.Count : (int)App.Presentations.Count;
            if (open > 0) return;
            if (word) App.Quit(SaveChanges: 0); else App.Quit();
        }
        catch { }
    }
}
