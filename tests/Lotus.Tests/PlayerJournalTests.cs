using Lotus.Core.Games;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class PlayerJournalTests
{
    [Fact] public void FavoritesAndNotesPersistAndAreCleaned()
    {
        var file = Path.Combine(Path.GetTempPath(), "LotusPlayer-" + Guid.NewGuid() + ".json");
        try
        {
            var j = new PlayerJournal(new JsonPlayerJournalStore(file), ex => throw ex);
            var changes = 0; j.Changed += (_, _) => changes++;
            j.SetFavorite("Steam:1", true); j.SetFavorite("Steam:1", true); // second call is a no-op
            j.SetNote("Steam:1", "  parei no\nchefe  " + new string('x', 200));
            Assert.Equal(2, changes);
            var again = new PlayerJournal(new JsonPlayerJournalStore(file), ex => throw ex);
            Assert.True(again.IsFavorite("Steam:1")); Assert.StartsWith("parei no chefe", again.Note("Steam:1"));
            Assert.Equal(PlayerJournal.NoteLimit, again.Note("Steam:1").Length);
            again.SetNote("Steam:1", " "); again.SetFavorite("Steam:1", false);
            var empty = new PlayerJournal(new JsonPlayerJournalStore(file), ex => throw ex);
            Assert.False(empty.IsFavorite("Steam:1")); Assert.Equal("", empty.Note("Steam:1"));
            File.WriteAllText(file, "{ broken");
            Assert.Equal(0, new PlayerJournal(new JsonPlayerJournalStore(file), _ => { }).FavoriteCount); // corrupt file: start empty, no crash
        }
        finally { File.Delete(file); }
    }

    [Fact] public void ReadsSteamPlaytimeMinutes()
    {
        const string vdf = "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"1593500\" { \"LastPlayed\" \"1\" \"Playtime\" \"1989\" } \"10\" { \"Playtime\" \"0\" } } } } } }";
        var p = SteamPlaytime.Parse(vdf);
        Assert.Equal(1989, p["1593500"]); Assert.False(p.ContainsKey("10"));
    }
}
