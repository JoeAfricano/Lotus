using Lotus.Core;
using Lotus.Core.Maintenance;
using Xunit;

namespace Lotus.Tests;

public sealed class MemoryCompressionTests
{
    private sealed class Upkeep : IWindowsUpkeep
    {
        public WindowsStatus Read() => new(false, false, false, TimeSpan.FromHours(2), false);
        public Task<RestorePointResult> CreateRestorePointAsync(string d, CancellationToken t = default) => Task.FromResult(new RestorePointResult(true, true, 1, 2, null));
        public void OpenWindowsUpdate() { } public void OpenSystemRestore() { }
    }
    private sealed class Memory(MemoryCompressionStatus status, MemoryCompressionResult result) : IMemoryCompression
    {
        public MemoryCompressionStatus Status = status;
        public int Enables, Undos;
        public MemoryCompressionStatus Read() => Status;
        public Task<MemoryCompressionResult> EnableAsync(CancellationToken t = default)
        {
            Enables++;
            if (result.Ok) Status = Status with { Saved = new(Status.Active, Status.SysMain, Status.SysMainRunning, DateTimeOffset.Now), RestartPending = true };
            return Task.FromResult(result);
        }
        public Task<MemoryCompressionResult> UndoAsync(CancellationToken t = default) { Undos++; return Task.FromResult(result); }
    }
    private const long Gb16 = 16L << 30, Gb32 = 32L << 30;
    private static MemoryCompressionStatus Off(long ram = Gb16) => new(false, SysMainStart.Disabled, false, ram, null, false);

    [Fact] public void RecommendedOnlyWhenOffOnSmallRam()
    {
        Assert.True(MemoryCompressionRules.Recommended(Off()));
        Assert.False(MemoryCompressionRules.Recommended(Off(Gb32)));
        Assert.False(MemoryCompressionRules.Recommended(Off() with { RestartPending = true }));
        var on = new MemoryCompressionStatus(true, SysMainStart.Automatic, true, Gb16, null, false);
        Assert.True(MemoryCompressionRules.IsOn(on));
        Assert.False(MemoryCompressionRules.Recommended(on));
        Assert.False(MemoryCompressionRules.IsOn(on with { SysMainRunning = false }));
    }

    [Fact] public async Task ReadsThenTurnsOnAndOffersUndo()
    {
        var text = new TextCatalog();
        var mem = new Memory(Off(), new(true, true, null));
        var vm = new WindowsUpkeepViewModel(new Upkeep(), text, new MemoryMaintenanceJournal(), ex => throw ex, mem);
        await vm.LoadAsync();
        Assert.True(vm.HasMemory); Assert.False(vm.MemoryOn); Assert.True(vm.CanEnableMemory); Assert.False(vm.CanUndoMemory);
        Assert.Equal(text["MzOffAdviceSmall"], vm.MemoryAdvice);
        Assert.Equal(0, mem.Enables); // reading never changes anything
        await vm.EnableMemoryAsync();
        Assert.Equal(1, mem.Enables);
        Assert.Equal(text["MzEnabled"], vm.MemoryStatus);
        Assert.Equal(text["MzRestartPending"], vm.MemoryText);
        Assert.False(vm.CanEnableMemory); Assert.True(vm.CanUndoMemory);
    }

    [Fact] public async Task DeclinedOrFailedIsReportedHonestly()
    {
        var text = new TextCatalog();
        var declined = new WindowsUpkeepViewModel(new Upkeep(), text, new MemoryMaintenanceJournal(), ex => throw ex, new Memory(Off(), new(false, false, null)));
        await declined.LoadAsync(); await declined.EnableMemoryAsync();
        Assert.Equal(text["MzDeclined"], declined.MemoryStatus); Assert.True(declined.CanEnableMemory); Assert.False(declined.CanUndoMemory);
        var failed = new WindowsUpkeepViewModel(new Upkeep(), text, new MemoryMaintenanceJournal(), ex => throw ex, new Memory(Off(Gb32), new(true, false, "Access denied")));
        await failed.LoadAsync();
        Assert.Equal(text["MzOffAdvice"], failed.MemoryAdvice);
        await failed.EnableMemoryAsync();
        Assert.Equal(text.Format("MzError", "Access denied"), failed.MemoryStatus);
        var none = new WindowsUpkeepViewModel(new Upkeep(), text, new MemoryMaintenanceJournal(), ex => throw ex);
        await none.LoadAsync();
        Assert.False(none.HasMemory);
    }
}
