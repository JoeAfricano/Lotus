using Lotus.Core;
using Lotus.Core.Games;
using Lotus.Core.Maintenance;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

// Disk health, system and overlays with fakes; real reads are read-only.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DiskSystemOverlayTests
{
    private sealed class Disks(params DiskDetail[] disks) : IDiskInspector
    {
        public List<string> Scanned { get; } = [];
        public IReadOnlyList<DiskDetail> Read() => disks;
        public bool StartScan(string letter) { Scanned.Add(letter); return true; }
    }

    [Fact] public async Task WhatWindowsDoesNotReportIsShownAsUnavailableNeverGuessed()
    {
        var text = new TextCatalog();
        var vm = new DiskToolViewModel(new Disks(new DiskDetail("0", "Samsung 980", "Ssd", "NVMe", 1L << 40, "Healthy", "OK", null, null, null, null, null, [new("C:", 100L << 30, 900L << 30)])), text, new MemoryMaintenanceJournal(), ex => throw ex);
        await vm.LoadAsync();
        var facts = vm.Rows.Single().Facts;
        Assert.Equal(text["DkUnavailable"], facts.Single(f => f.Label == text["DkTemp"]).Value);
        Assert.Equal(text["DkUnavailable"], facts.Single(f => f.Label == text["DkWear"]).Value);
        Assert.False(vm.Rows.Single().Warning);
        Assert.Contains(text["DkNeedsAdmin"], vm.Status);
        Assert.Equal("Saudável", facts.Single(f => f.Label == text["DkHealth"]).Value);   // stands alone, so it starts uppercase
        Assert.Equal(new DiskScanTarget("C:", "Verificar C:"), vm.Rows.Single().ScanTargets.Single()); // plain words, not "chkdsk /scan"
    }

    [Fact] public async Task RealWarningsAreFlagged()
    {
        var disks = new Disks(new DiskDetail("1", "Old HDD", "Hdd", "SATA", 1L << 40, "Warning", "Predictive failure", 65, null, 40000, 3, 0, [new("D:", 10L << 30, 1000L << 30)]));
        var journal = new MemoryMaintenanceJournal();
        var vm = new DiskToolViewModel(disks, new TextCatalog(), journal, ex => throw ex);
        await vm.LoadAsync();
        var row = vm.Rows.Single();
        Assert.True(row.Warning);
        Assert.Equal(4, row.Facts.Count(f => f.Warning));                         // health, temperature, errors, low space
        Assert.Equal(["D:"], row.Letters);
        Assert.True(vm.StartScan("D:")); Assert.Equal(["D:"], disks.Scanned);
        Assert.Equal("scan", journal.Recent().Single().Action);
    }

    [Fact] public void LaunchersAndCompatibleToolsAreListedButNeverCountedAsProblems()
    {
        var found = WindowsOverlayScanner.FromProcesses(["steam", "steamwebhelper", "RTSS", "MSIAfterburner", "Overwolf"], false, false, includeContext: true);
        Assert.Contains(found, f => f.Id == "launcher-steam" && !f.Heavy && f.Processes!.Contains("steamwebhelper"));
        Assert.Contains(found, f => f.Id == "compat-rtss" && !f.Heavy);
        Assert.Equal(1, found.Count(f => f.Id == "launcher-steam"));               // one row per launcher
        Assert.Equal("OvAdviceLauncher", OverlayCheckViewModel.AdviceKey("launcher-steam"));
        Assert.Equal("OvAdviceCompat", OverlayCheckViewModel.AdviceKey("compat-rtss"));
        Assert.DoesNotContain(WindowsOverlayScanner.FromProcesses(["steam", "RTSS"], false, false), f => f.Id.StartsWith("launcher-") || f.Id.StartsWith("compat-")); // Preparar e jogar: unchanged
    }

    private sealed class Scanner(params OverlayFinding[] found) : IOverlayScanner { public IReadOnlyList<OverlayFinding> Scan() => found; }

    [Fact] public async Task OverlaysShowTheirRealUsageAndOnlyOverlaysCountInTheSummary()
    {
        var text = new TextCatalog();
        var vm = new OverlayCheckViewModel(new Scanner(new("compat-rtss", "RTSS", false, ["RTSS"]), new("overwolf", "Overwolf", true, ["Overwolf"]), new("launcher-steam", "Steam", false, ["steam"])), text,
            _ => Task.FromResult<IReadOnlyList<ProcessSample>>([new("Overwolf", "Overwolf", null, 300L << 20, 4.5, true, [1], ProcessKind.Normal)]));
        await vm.ScanAsync();
        Assert.Equal(["overwolf", "launcher-steam", "compat-rtss"], vm.Rows.Select(r => r.Id));   // overlays first, compatible tools last
        Assert.Contains("300", vm.Rows[0].Usage); Assert.Equal("", vm.Rows[2].Usage);
        Assert.Equal(text.Format("OvFoundOne", 1), vm.Summary);
    }

    private sealed class Sys : ISystemInspector
    {
        public List<string> Repairs { get; } = [];
        public SystemSnapshot Read(TimeSpan since) => new([new("Cpu", "Ryzen 7"), new("Os", "Windows 11")], [new("Controller", "USB", 43, null)],
            [new(DateTime.Now.AddHours(-2), "nvlddmkm", 153, "Error", "Display driver reset"), new(DateTime.Now.AddHours(-1), "nvlddmkm", 153, "Error", "Display driver reset"), new(DateTime.Now, "Kernel-Power", 41, "Critical", "Unexpected shutdown")]);
        public bool StartRepair(string tool) { Repairs.Add(tool); return true; }
    }

    [Fact] public async Task SystemShowsDevicesAndGroupedEventsAndRepairsOnlyKnownTools()
    {
        var sys = new Sys(); var text = new TextCatalog();
        var vm = new SystemToolViewModel(sys, text, new MemoryMaintenanceJournal(), ex => throw ex);
        await vm.LoadAsync();
        Assert.Equal(2, vm.Hardware.Count);
        Assert.Contains("43", vm.Devices.Single().Detail);
        Assert.Equal(2, vm.Events.Count);                                          // same source and id grouped
        Assert.Contains("2x", vm.Events.Single(e => e.Title.StartsWith("nvlddmkm")).Detail);
        Assert.True(vm.Events.Single(e => e.Title.StartsWith("Kernel-Power")).Warning);
        Assert.False(vm.StartRepair("format c:")); Assert.True(vm.StartRepair("sfc"));
        Assert.Equal(["sfc"], sys.Repairs);
    }
}
