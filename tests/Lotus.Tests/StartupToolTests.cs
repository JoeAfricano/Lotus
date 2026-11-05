using Lotus.Core;
using Lotus.Core.Maintenance;
using Lotus.Core.Optimization;
using Lotus.Infrastructure.Maintenance;
using Xunit;

namespace Lotus.Tests;

// Windows startup tool with a fake inventory: the real startup list is never changed by tests.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class StartupToolTests
{
    private sealed class FakeInventory(List<StartupEntry> entries) : IStartupInventory
    {
        public List<StartupEntry> Entries { get; } = entries;
        public StartupInventory Read() => new([.. Entries], []);
    }
    private sealed class FakeDetails : IStartupDetails { public StartupDetails Inspect(StartupEntry e) => new(StartupCommand.ExecutableOf(e.Command), "Maker", SignatureState.Valid, "Maker Inc"); }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --min", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Tools\tray.exe /background", @"C:\Tools\tray.exe")]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL", "rundll32.exe")]
    [InlineData("\"unterminated", null)]
    public void TheProgramIsReadFromTheCommand(string command, string? exe) => Assert.Equal(exe, StartupCommand.ExecutableOf(command, s => s));

    [Fact] public async Task OnlyUserItemsCanBeTurnedOffAndTheResultIsReadBack()
    {
        var inv = new FakeInventory([
            new("Discord", @"HKCU\…\Run", "\"C:\\D\\Update.exe\"", true, "Run", "Discord", UserScope: true),
            new("Driver panel", @"HKLM\…\Run", @"C:\Drv\panel.exe", true, "Run", "Driver panel", UserScope: false)]);
        var journal = new MemoryMaintenanceJournal();
        var vm = new StartupToolViewModel(inv, new FakeDetails(), e =>
        {
            var i = inv.Entries.FindIndex(x => x.ApprovalName == e.ApprovalName);
            inv.Entries[i] = e with { Enabled = e.Enabled == false };            // what Windows would now report
            return Task.FromResult(true);
        }, new TextCatalog(), journal, ex => throw ex);
        await vm.LoadAsync();
        Assert.False(vm.Rows.Single(r => r.Title == "Driver panel").CanToggle);   // needs administrator: read-only
        var discord = vm.Rows.Single(r => r.Title == "Discord");
        Assert.True(discord.CanToggle); Assert.Contains("Maker Inc", discord.Detail);
        await vm.ToggleAsync(discord);
        Assert.True(vm.Rows.Single(r => r.Title == "Discord").IsDisabled);
        await vm.ToggleAsync(vm.Rows.Single(r => r.Title == "Discord"));       // and back
        Assert.False(vm.Rows.Single(r => r.Title == "Discord").IsDisabled);
        Assert.Equal(2, journal.Recent().Count(e => e.Tool == "startup" && e.Success));
    }

    [Fact] public async Task AChangeThatDidNotHappenIsReportedAsAConflict()
    {
        var inv = new FakeInventory([new("App", @"HKCU\…\Run", @"C:\A\a.exe", true, "Run", "App", UserScope: true)]);
        var journal = new MemoryMaintenanceJournal();
        var vm = new StartupToolViewModel(inv, new FakeDetails(), _ => Task.FromResult(true), new TextCatalog(), journal, ex => throw ex);
        await vm.LoadAsync();
        await vm.ToggleAsync(vm.Rows.Single());
        Assert.False(vm.Rows.Single().IsDisabled);
        Assert.False(journal.Recent().Single().Success);
    }

    [Fact] public void LotusOwnStartupEntryIsFixedWhenItPointsAtAnotherCopy()
    {
        const string installed = @"C:\Users\x\AppData\Local\Programs\Lotus\Lotus.App.exe", dev = @"C:\Projetos\Lotus\bin\Debug\Lotus.App.exe";
        Assert.True(Lotus.Infrastructure.Setup.WindowsStartup.NeededChange(true, Lotus.Infrastructure.Setup.WindowsStartup.CommandFor(dev), installed));  // wrong copy: rewritten
        Assert.Null(Lotus.Infrastructure.Setup.WindowsStartup.NeededChange(true, Lotus.Infrastructure.Setup.WindowsStartup.CommandFor(installed), installed));
        Assert.True(Lotus.Infrastructure.Setup.WindowsStartup.NeededChange(true, null, installed));
        Assert.False(Lotus.Infrastructure.Setup.WindowsStartup.NeededChange(false, "anything", installed));
        Assert.Null(Lotus.Infrastructure.Setup.WindowsStartup.NeededChange(false, null, installed));
    }

    [Fact] public void ASignedWindowsFileIsVerifiedAsValid()
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var state = WindowsStartupDetails.Verify(notepad);
        Assert.True(state is SignatureState.Valid or SignatureState.Unsigned);  // catalog-signed builds have no embedded signature
        var unsigned = Path.GetTempFileName();
        try { File.WriteAllText(unsigned, "not a program"); Assert.NotEqual(SignatureState.Valid, WindowsStartupDetails.Verify(unsigned)); }
        finally { File.Delete(unsigned); }
    }
}
