using Lotus.Core.Games;

namespace Lotus.Core.Optimization;

// "Preparar e jogar" uses the same Game profile as Otimizar (same journal, same undo), so both screens stay in sync.
public sealed class GameProfileAdapter(PowerProfilesViewModel profiles) : IGameProfile
{
    private IReadOnlyList<PlannedChange> last = [];
    private bool loaded;

    public async Task<IReadOnlyList<ProfileStep>> PreviewAsync()
    {
        if (!loaded) { await profiles.RefreshAsync(); loaded = true; }
        profiles.SelectProfileCommand.Execute("game");
        last = await profiles.PreviewProfileAsync();
        return [.. last.Select(c => new ProfileStep(c.Title, c.From, c.To, c.AlreadyApplied))];
    }

    public async Task<bool> ApplyAsync()
    {
        if (last.Count == 0) return true;
        await profiles.ApplyProfileAsync(last);
        var after = await profiles.PreviewProfileAsync(); // verified by reading the state back
        last = [];
        return after.All(c => c.AlreadyApplied);
    }

    public async Task<bool> UndoAsync()
    {
        if (!profiles.CanUndoProfile) return true;
        await profiles.UndoProfileAsync();
        return !profiles.CanUndoProfile;
    }
}
