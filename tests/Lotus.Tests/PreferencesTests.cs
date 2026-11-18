using Lotus.Core;
using Lotus.Infrastructure;
using Serilog;
using Xunit;

namespace Lotus.Tests;

public sealed class PreferencesTests : IDisposable
{
    [Fact] public void Calibration_roundtrip_preserves_measurements_depth_and_hardware_identity()
    {
        var step=new Lotus.Core.Telemetry.AutoStep("baseline",Lotus.Core.Telemetry.GpuSetting.Stock,Lotus.Core.Telemetry.TuneVerdict.Stable,1000,65,2700,120);
        var report=new Lotus.Core.Telemetry.AutoTuneReport(step,null,null,null,[step],false);
        var calibration=new Lotus.Core.Telemetry.GpuTuneCalibration("hardware-id","RTX 5060",DateTimeOffset.UtcNow,report,Depth:Lotus.Core.Telemetry.GpuTestDepth.Quick);
        Assert.True(CreateStore().TrySave(new InterfacePreferences { GpuTuneCalibration=calibration }));
        var loaded=CreateStore().Load().Value.GpuTuneCalibration!;
        Assert.Equal(calibration.GpuIdentity,loaded.GpuIdentity); Assert.Equal(calibration.Depth,loaded.Depth);
        Assert.Equal(step,loaded.Report.Baseline); Assert.Single(loaded.Report.Steps);
    }

    [Fact] public void Oversized_save_preserves_the_last_valid_file()
    {
        Assert.True(CreateStore().TrySave(new InterfacePreferences()));
        var previous=File.ReadAllBytes(FilePath);
        var huge=new Lotus.Core.Telemetry.GpuTuneCalibration(new string('x',270_000),"GPU",DateTimeOffset.UtcNow,new(null,null,null,null,[],false));
        Assert.False(CreateStore().TrySave(new InterfacePreferences { GpuTuneCalibration=huge }));
        Assert.Equal(previous,File.ReadAllBytes(FilePath)); Assert.Empty(Directory.GetFiles(directory,"*.tmp"));
    }
    private readonly string directory = Path.Combine(Path.GetTempPath(), "lotus-preferences-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "preferences.json");
    private JsonPreferencesStore CreateStore() => new(FilePath, new LoggerConfiguration().CreateLogger());

    [Fact] public void MissingPreferencesUsePortugueseDefaultsWithoutWriting()
    {
        var loaded = CreateStore().Load();
        Assert.Equal("Missing", loaded.Status);
        Assert.Equal(new InterfacePreferences(), loaded.Value);
        Assert.False(File.Exists(FilePath));
    }
    [Fact] public void AllPreferencesSurviveRestartAndReplacementKeepsBackup()
    {
        var original = new InterfacePreferences { Language = "en-US", Theme = "Graphite", SidebarExpanded = false, ReduceMotion = true };
        Assert.True(CreateStore().TrySave(original));
        Assert.Equal(original, CreateStore().Load().Value);
        var updated = original with { Language = "pt-BR", Theme = "Midnight" };
        Assert.True(CreateStore().TrySave(updated));
        Assert.Equal(updated, CreateStore().Load().Value);
        Assert.Contains("en-US", File.ReadAllText(FilePath + ".bak"));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":77}")]
    [InlineData("{\"ReduceMotion\":\"not a boolean\"}")]
    public void InvalidPreferencesDoNotPreventStartupOrOverwriteOriginal(string json)
    {
        Directory.CreateDirectory(directory); File.WriteAllText(FilePath, json);
        var service = new PreferencesService(CreateStore());
        Assert.Equal(new InterfacePreferences(), service.Current);
        Assert.NotEqual("Loaded", service.LoadStatus);
        Assert.Equal(json, File.ReadAllText(FilePath));
    }
    [Fact] public void UnsupportedLanguageAndThemeAreNormalizedWhileValidFieldsSurvive()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, "{\"Language\":\"xx\",\"Theme\":\"unexpected\",\"ReduceMotion\":true,\"SidebarExpanded\":false}");
        var loaded = CreateStore().Load();
        Assert.Equal("Normalized", loaded.Status); Assert.Equal("pt-BR", loaded.Value.Language);
        Assert.Equal("Midnight", loaded.Value.Theme); Assert.True(loaded.Value.ReduceMotion); Assert.False(loaded.Value.SidebarExpanded);
    }
    [Fact] public void OversizedFileFallsBackWithoutOverwriting()
    {
        Directory.CreateDirectory(directory); File.WriteAllText(FilePath, new string(' ', 270_000));
        Assert.Equal("Oversized", CreateStore().Load().Status); Assert.Equal(270_000, new FileInfo(FilePath).Length);
    }
    [Fact] public void LockedDestinationPreservesLastCompleteFileAndCurrentState()
    {
        var service = new PreferencesService(CreateStore());
        Assert.True(service.Update(service.Current with { Theme = "Graphite" }));
        var previous = File.ReadAllText(FilePath);
        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(service.Update(service.Current with { Language = "en-US" }));
            Assert.Equal("pt-BR", service.Current.Language); Assert.Equal("SaveFailed", service.ErrorKey);
        }
        Assert.Equal(previous, File.ReadAllText(FilePath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    [Fact] public void SelectingSettingsAppliesImmediatelyAndKeepsSidebar()
    {
        var service = new PreferencesService(CreateStore());
        Assert.True(service.Update(service.Current with { SidebarExpanded = false }));
        var text = new TextCatalog(); var vm = new PreferencesViewModel(service, text) { LanguageIndex = 1, ThemeIndex = 1, ReduceMotion = true };
        Assert.Equal(text["Saved"], vm.Message);
        Assert.Equal("en-US", service.Current.Language); Assert.False(service.Current.SidebarExpanded);
        Assert.True(service.Current.ReduceMotion);
    }
    [Fact] public void FailedImmediateSelectionReturnsControlToPersistedValue()
    {
        var service = new PreferencesService(CreateStore());
        Assert.True(service.Update(service.Current with { Theme = "Graphite" }));
        var text = new TextCatalog(); var vm = new PreferencesViewModel(service, text);
        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            vm.LanguageIndex = 1;
            Assert.Equal(0, vm.LanguageIndex); Assert.Equal("pt-BR", service.Current.Language);
            Assert.Equal(text["SaveFailed"], vm.Message);
        }
    }
    [Fact] public void TemporaryNoSelectionFromComboBoxCannotOverwritePreferences()
    {
        var service = new PreferencesService(CreateStore());
        var vm = new PreferencesViewModel(service, new TextCatalog()) { LanguageIndex = 1, ThemeIndex = 3 };
        vm.LanguageIndex = -1; vm.ThemeIndex = -1;
        Assert.Equal(1, vm.LanguageIndex); Assert.Equal(3, vm.ThemeIndex);
        Assert.Equal("en-US", CreateStore().Load().Value.Language);
        Assert.Equal("Graphite", CreateStore().Load().Value.Theme);
    }
    [Fact] public void ThemeOptionsRemainLocalizedAfterLiveLanguageChanges()
    {
        var service = new PreferencesService(CreateStore()); var text = new TextCatalog();
        service.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PreferencesService.Current)) text.SetLanguage(service.Current.Language); };
        var vm = new PreferencesViewModel(service, text) { ThemeIndex = 3 };
        Assert.Equal("Grafite", vm.ThemeOptions[vm.ThemeIndex]);
        vm.LanguageIndex = 1;
        Assert.Equal("Graphite", vm.ThemeOptions[vm.ThemeIndex]);
        vm.LanguageIndex = 0;
        Assert.Equal("Grafite", vm.ThemeOptions[vm.ThemeIndex]);
    }
    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
