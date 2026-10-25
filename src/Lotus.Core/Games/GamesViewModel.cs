using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lotus.Core.Games;

// Each card carries its own labels and commands, so item templates never depend on the parent DataContext.
public sealed record GameCard(GameEntry Game, string Name, string Store, string Initials, string? CoverPath,
    string PlayLabel, string RemoveLabel, System.Windows.Input.ICommand Launch, System.Windows.Input.ICommand Remove, string OptimizeLabel = "", string MeasureLabel = "",
    string? ShaderLabel = null, bool CanPrecompile = false, string PrecompileLabel = "", string? ShaderId = null);
public sealed record StoreFilter(string Id, string Label);
// Result of "Otimizar configurações": hardware, the recommendation as readable lines, and whether Lotus can apply it.
public sealed record GameSettingsAnalysis(SettingsRecommendation Recommendation, IReadOnlyList<string> Lines, string Hardware, GameSettingsSupport? Support, bool HasOriginal, int? PendingStep = null);
public sealed record MeasureResult(string Summary, TuningDecision Decision, string DecisionText, bool CanApplyStep);

// "Meus Jogos": scans launchers in the background, filters by text and store, launches on request.
// "Remove from list" only hides a game (launcher or manual); a later scan can bring it back. Nothing is uninstalled.
public sealed class GamesViewModel : ObservableObject
{
    private readonly IGameLibrary library;
    private readonly IManualGameStore manualStore;
    private readonly IGameLauncher launcher;
    private readonly Action<Exception> log;
    private readonly ISystemProfileSource? profileSource;
    private readonly IReadOnlyList<IGameSettingsRecipe> recipes;
    private readonly IGameSettingsStore? settingsStore;
    private readonly IGameProcessCheck? processCheck;
    private readonly Benchmark.IFrameCaptureProvider? capture;
    private readonly IGpuLoadSampler? gpuSampler;
    private readonly IShaderPrecompiler? shaders;
    private IReadOnlyList<ShaderStatus> shaderStatuses = [];
    private readonly Dictionary<string, int> pendingSteps = new(StringComparer.OrdinalIgnoreCase);
    private List<GameEntry> all = [];
    private List<GameEntry> manual = [];
    private HashSet<string> hidden = new(StringComparer.OrdinalIgnoreCase);
    private string search = "", store = "", status = "";
    private bool busy, loaded;
    public ITextCatalog Text { get; }
    public ObservableCollection<GameCard> Games { get; } = [];
    public ObservableCollection<StoreFilter> Stores { get; } = [];
    public string Status => status;
    public bool IsBusy => busy;
    public bool IsEmpty => !busy && loaded && Games.Count == 0;
    public string EmptyMessage => !Visible.Any() ? Text["GamesNoneFound"] : Text["GamesNoMatch"];
    public string Summary => Text.Format("GamesSummary", Visible.Count(), Visible.Select(g => g.Store).Distinct().Count());
    private IEnumerable<GameEntry> Visible => all.Where(g => !hidden.Contains(g.Key));
    // Games still installed (or still valid manual entries) that the user removed from the list.
    public IReadOnlyList<GameEntry> Removed => all.Where(g => hidden.Contains(g.Key)).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    public string Search { get => search; set { if (SetProperty(ref search, value ?? "")) ApplyFilter(); } }
    public StoreFilter? SelectedStore
    {
        get => Stores.FirstOrDefault(s => s.Id == store);
        set { var id = value?.Id ?? ""; if (store != id) { store = id; OnPropertyChanged(); ApplyFilter(); } }
    }
    public IRelayCommand<GameCard> LaunchCommand { get; }
    public IRelayCommand<GameCard> RemoveCommand { get; }

    public GamesViewModel(IGameLibrary library, IManualGameStore manualStore, IGameLauncher launcher, ITextCatalog text, Action<Exception> log,
        ISystemProfileSource? profileSource = null, IReadOnlyList<IGameSettingsRecipe>? recipes = null, IGameSettingsStore? settingsStore = null, IGameProcessCheck? processCheck = null,
        Benchmark.IFrameCaptureProvider? capture = null, IGpuLoadSampler? gpuSampler = null, IShaderPrecompiler? shaders = null)
    {
        this.library = library; this.manualStore = manualStore; this.launcher = launcher; Text = text; this.log = log;
        this.profileSource = profileSource; this.recipes = recipes ?? []; this.settingsStore = settingsStore; this.processCheck = processCheck; this.capture = capture; this.gpuSampler = gpuSampler; this.shaders = shaders;
        LaunchCommand = new RelayCommand<GameCard>(Launch, c => c?.Game.CanLaunch == true);
        RemoveCommand = new RelayCommand<GameCard>(Remove);
    }

    public Task EnsureLoadedAsync() => loaded || busy ? Task.CompletedTask : RefreshAsync();

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; Notify();
        try
        {
            var result = await Task.Run(() =>
            {
                manual = manualStore.Load().ToList();
                hidden = new(manualStore.LoadHidden(), StringComparer.OrdinalIgnoreCase);
                return library.Scan(manual);
            });
            // Optional SCSKiller status; a failure here never blocks the library.
            try { shaderStatuses = shaders is { IsAvailable: true } ? await shaders.StatusAsync(CancellationToken.None) : []; }
            catch (Exception ex) { log(ex); shaderStatuses = []; }
            all = result.Games.ToList();
            foreach (var e in result.Errors) log(new InvalidOperationException("Game scan: " + e));
            status = "";
        }
        catch (Exception ex) { log(ex); status = Text["GamesScanFailed"]; }
        finally { busy = false; loaded = true; BuildStores(); ApplyFilter(); Notify(); }
    }

    // "Escanear jogos": scans every platform again; the UI then offers to restore anything in Removed.
    public async Task<IReadOnlyList<GameEntry>> RescanAsync()
    {
        await RefreshAsync();
        if (status.Length == 0) { status = Text.Format("GamesRescanDone", Visible.Count()); Notify(); }
        return Removed;
    }

    public void Restore(IEnumerable<GameEntry> games)
    {
        var count = games.Count(g => hidden.Remove(g.Key));
        if (count == 0) return;
        try { manualStore.SaveHidden(hidden.ToList()); status = Text.Format("GamesRestored", count); }
        catch (Exception ex) { log(ex); status = Text["GamesSaveFailed"]; }
        BuildStores(); ApplyFilter(); Notify();
    }

    public async Task<bool> AddManualAsync(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exePath))
        { status = Text["GamesInvalidExe"]; Notify(); return false; }
        var full = Path.GetFullPath(exePath);
        // Manual games are identified by their executable path; adding an existing (even removed) one brings it back.
        var entry = new GameEntry(GameStores.Manual, full.ToLowerInvariant(), Path.GetFileNameWithoutExtension(full), Path.GetDirectoryName(full), full);
        manual.RemoveAll(g => g.Id == entry.Id); manual.Add(entry); hidden.Remove(entry.Key);
        try { await Task.Run(() => { manualStore.Save(manual); manualStore.SaveHidden(hidden.ToList()); }); }
        catch (Exception ex) { log(ex); status = Text["GamesSaveFailed"]; Notify(); return false; }
        await RefreshAsync();
        status = Text.Format("GamesAdded", entry.Name); Notify();
        return true;
    }

    private void Remove(GameCard? card)
    {
        if (card is null) return;
        hidden.Add(card.Game.Key);
        try { manualStore.SaveHidden(hidden.ToList()); status = Text.Format("GamesHidden", card.Name); }
        catch (Exception ex) { log(ex); hidden.Remove(card.Game.Key); status = Text["GamesSaveFailed"]; }
        BuildStores(); ApplyFilter(); Notify();
    }

    private void Launch(GameCard? card)
    {
        if (card is null) return;
        try { launcher.Launch(card.Game); status = Text.Format("GamesLaunching", card.Name); }
        catch (Exception ex) { log(ex); status = Text.Format("GamesLaunchFailed", card.Name); }
        Notify();
    }

    private void BuildStores()
    {
        var current = store;
        Stores.Clear(); Stores.Add(new("", Text["GamesAllStores"]));
        foreach (var s in GameStores.All.Where(s => Visible.Any(g => g.Store == s))) Stores.Add(new(s, s));
        if (Stores.All(s => s.Id != current)) store = "";
        OnPropertyChanged(nameof(SelectedStore));
    }

    // Accent- and case-insensitive name search, combined with the store filter.
    private void ApplyFilter()
    {
        var term = Normalize(search);
        Games.Clear();
        foreach (var g in Visible.Where(g => (store.Length == 0 || g.Store == store) && (term.Length == 0 || Normalize(g.Name).Contains(term)))
                                 .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var shader = ScsKillerOutput.Match(shaderStatuses, g);
            Games.Add(new(g, g.Name, g.Store, Initials(g.Name), g.CoverPath, Text["GamesPlay"], Text["GamesHide"], LaunchCommand, RemoveCommand, Text["GsMenu"], Text["GsMeasureMenu"],
                shader is null ? null : Text["ScsState" + shader.State], shader?.State is ShaderState.Ready or ShaderState.Stale, Text["ScsPrecompile"], shader?.Id));
        }
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(EmptyMessage));
    }

    public static string Normalize(string value)
    {
        var d = value.Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToLowerInvariant().Trim();
    }
    public static string Initials(string name)
    {
        var words = name.Split([' ', ':', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).ToArray();
        return words.Length == 0 ? "?" : string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    // ---- Smart game settings ----
    public bool CanOptimizeSettings => profileSource is not null;

    public async Task<GameSettingsAnalysis?> AnalyzeSettingsAsync(GameEntry game)
    {
        if (profileSource is null) return null;
        try
        {
            return await Task.Run(() =>
            {
                var p = profileSource.Read();
                var r = SettingsAdvisor.Recommend(p);
                var (recipe, support) = Recipe(game);
                var lines = new List<string>
                {
                    Text.Format("GsLineTarget", r.TargetFps),
                    Text.Format("GsLinePreset", Text["GsTier" + r.Tier]),
                    Text.Format("GsLineTextures", Text["GsTier" + r.TextureTier]),
                    r.Upscaler == Upscaler.None ? Text["GsLineNoUpscale"] : Text.Format("GsLineUpscale", Text["GsUpscaler" + r.Upscaler], Text["GsMode" + r.Mode]),
                    Text.Format("GsLineCap", r.FpsCap)
                };
                lines.AddRange(r.ReasonKeys.Select(k => Text[k]));
                var hardware = Text.Format("GsHardware", string.IsNullOrWhiteSpace(p.GpuName) ? "?" : p.GpuName, p.VramMb / 1024.0, p.Width, p.Height, p.RefreshHz);
                return new GameSettingsAnalysis(r, lines, hardware, support, settingsStore?.LoadOriginal(game.Key) is not null, pendingSteps.TryGetValue(game.Key, out var step) ? step : null);
            });
        }
        catch (Exception ex) { log(ex); status = Text["GsFailed"]; Notify(); return null; }
    }

    private (IGameSettingsRecipe? Recipe, GameSettingsSupport? Support) Recipe(GameEntry game)
    {
        foreach (var r in recipes) if (r.Probe(game) is { } s) return (r, s);
        return (null, null);
    }

    // Writes only with the game closed, after saving the original once, and verifies by reading back.
    public async Task<string> ApplySettingsAsync(GameEntry game, SettingsRecommendation recommendation)
    {
        var key = await Task.Run(() =>
        {
            var (recipe, support) = Recipe(game);
            if (recipe is null || support is not { CanApply: true } || settingsStore is null) return "GsNotSupported";
            if (processCheck?.IsRunning(game) == true) return "GsCloseGame";
            var raw = recipe.ReadRaw(game);
            if (raw is null) return "GsNotSupported";
            var updated = recipe.Transform(raw, recommendation);
            if (updated == raw) return "GsAlready";
            settingsStore.SaveOriginalOnce(game.Key, raw);
            recipe.WriteRaw(game, updated);
            return recipe.ReadRaw(game) == updated ? "GsApplied" : "GsVerifyFailed";
        });
        status = Text.Format(key, game.Name); Notify();
        return key;
    }

    public async Task<string> RestoreSettingsAsync(GameEntry game)
    {
        var key = await Task.Run(() =>
        {
            var (recipe, _) = Recipe(game);
            if (recipe is null || settingsStore?.LoadOriginal(game.Key) is not { } original) return "GsNoOriginal";
            if (processCheck?.IsRunning(game) == true) return "GsCloseGame";
            recipe.WriteRaw(game, original);
            if (recipe.ReadRaw(game) != original) return "GsVerifyFailed";
            settingsStore.ClearOriginal(game.Key);
            return "GsRestored";
        });
        status = Text.Format(key, game.Name); Notify();
        return key;
    }

    // Measures the running game with PresentMon while sampling GPU load, then decides one quality step.
    public bool CanMeasure => capture is not null && processCheck is not null && profileSource is not null;
    public bool IsGameRunning(GameEntry game) => processCheck?.Find(game) is not null;

    public async Task<MeasureResult?> MeasureAsync(GameEntry game, int seconds = 45, int startDelaySeconds = 10, CancellationToken token = default)
    {
        if (!CanMeasure || processCheck!.Find(game) is not { } run) { status = Text.Format("GsMeasureNotRunning", game.Name); Notify(); return null; }
        try
        {
            for (var left = startDelaySeconds; left > 0; left--) { status = Text.Format("GsMeasureStartsIn", left); Notify(); await Task.Delay(1000, token); }
            status = Text.Format("GsMeasuring", seconds); Notify();
            var gpu = gpuSampler?.AverageAsync(TimeSpan.FromSeconds(seconds), token) ?? Task.FromResult<double?>(null);
            var session = await capture!.CaptureAsync(new(new(run.Pid, run.Executable, run.StartedUtc), seconds, Elevated: true, "Lotus: " + game.Name, "{}"), token);
            var gpuBusy = await gpu;
            if (session.Data?.Statistics is not { } s) { status = Text["GsMeasureFailed"]; Notify(); return null; }
            var (recipe, support) = Recipe(game);
            var raw = support is { CanApply: true } ? recipe!.ReadRaw(game) : null;
            var canRaise = raw is not null && recipe!.StepQuality(raw, +1) is not null;
            var canLower = raw is not null && recipe!.StepQuality(raw, -1) is not null;
            var target = SettingsAdvisor.Recommend(profileSource!.Read()).TargetFps;
            var decision = QualityTuner.Decide(s.AverageFps, s.Low1Fps, target, gpuBusy, canRaise, canLower);
            if (decision.Step != 0) pendingSteps[game.Key] = decision.Step; else pendingSteps.Remove(game.Key);
            var summary = Text.Format("GsMeasureSummary", s.AverageFps, s.Low1Fps, gpuBusy is { } g ? $"{g:0}%" : "?", target);
            status = summary; Notify();
            return new(summary, decision, Text[decision.ReasonKey], decision.Step != 0);
        }
        catch (OperationCanceledException) { status = ""; Notify(); return null; }
        catch (Exception ex) { log(ex); status = Text["GsMeasureFailed"]; Notify(); return null; }
    }

    // Applies the step decided by the last measurement (game closed, original saved once, verified).
    public async Task<string> ApplyStepAsync(GameEntry game)
    {
        var key = await Task.Run(() =>
        {
            if (!pendingSteps.TryGetValue(game.Key, out var step)) return "GsNoPendingStep";
            var (recipe, support) = Recipe(game);
            if (recipe is null || support is not { CanApply: true } || settingsStore is null) return "GsNotSupported";
            if (processCheck?.IsRunning(game) == true) return "GsCloseGame";
            if (recipe.ReadRaw(game) is not { } raw || recipe.StepQuality(raw, step) is not { } updated) return "GsAlready";
            settingsStore.SaveOriginalOnce(game.Key, raw);
            recipe.WriteRaw(game, updated);
            if (recipe.ReadRaw(game) != updated) return "GsVerifyFailed";
            pendingSteps.Remove(game.Key);
            return step > 0 ? "GsStepRaised" : "GsStepLowered";
        });
        status = Text.Format(key, game.Name); Notify();
        return key;
    }

    // ---- Optional shader pre-compilation (SCSKiller) ----
    public const string ScsKillerReleases = "https://github.com/BlueHeisenberg/SCSKiller/releases/latest";
    public bool ShaderToolInstalled => shaders?.IsAvailable == true;
    public bool ShowShaderToolOffer => shaders is not null && !ShaderToolInstalled && loaded && all.Count > 0;

    public async Task<bool> PrecompileAsync(GameCard card, CancellationToken token = default)
    {
        if (shaders is not { IsAvailable: true } || card.ShaderId is not { } id) return false;
        try
        {
            status = Text.Format("ScsCompiling", card.Name); Notify();
            var progress = new Progress<string>(line => { status = Text.Format("ScsProgress", card.Name, line.Length > 120 ? line[..120] + "…" : line); Notify(); });
            var code = await shaders.CompileAsync(id, progress, token);
            shaderStatuses = await shaders.StatusAsync(token);
            ApplyFilter();
            status = Text.Format(code == 0 ? "ScsDone" : "ScsFailed", card.Name); Notify();
            return code == 0;
        }
        catch (OperationCanceledException) { status = ""; Notify(); return false; }
        catch (Exception ex) { log(ex); status = Text.Format("ScsFailed", card.Name); Notify(); return false; }
    }

    public void RefreshLanguage() { BuildStores(); ApplyFilter(); Notify(); }
    private void Notify()
    {
        foreach (var p in new[] { nameof(Status), nameof(IsBusy), nameof(IsEmpty), nameof(EmptyMessage), nameof(Summary), nameof(ShowShaderToolOffer) }) OnPropertyChanged(p);
    }
}
