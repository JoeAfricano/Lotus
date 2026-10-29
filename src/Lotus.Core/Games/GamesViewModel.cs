using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lotus.Core.Games;

// Each card carries its own labels and commands, so item templates never depend on the parent DataContext.
public sealed record GameCard(GameEntry Game, string Name, string Store, string Initials, string? CoverPath,
    string PlayLabel, string RemoveLabel, System.Windows.Input.ICommand Launch, System.Windows.Input.ICommand Remove, string OptimizeLabel = "", string MeasureLabel = "",
    string? ShaderLabel = null, bool CanPrecompile = false, string PrecompileLabel = "", string? ShaderId = null, string OpenShaderLabel = "", string PrepareLabel = "", string WhyLabel = "", string OptimizeMenuLabel = "", string DetailsLabel = "");
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
        LaunchCommand = new RelayCommand<GameCard>(Launch, c => c is null || c.Game.CanLaunch); // null: the parameter binding has not arrived yet
        RemoveCommand = new RelayCommand<GameCard>(Remove);
    }

    public Task EnsureLoadedAsync() => loaded || busy ? Task.CompletedTask : RefreshAsync();

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; Notify();
        try
        {
            var (loadedManual, result) = await Task.Run(() => { var m = manualStore.Load().ToList(); return (m, library.Scan(m)); });
            // Fields change on the UI thread only; hidden is read after the scan so a removal made meanwhile is kept.
            manual = loadedManual;
            hidden = new(manualStore.LoadHidden(), StringComparer.OrdinalIgnoreCase);
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
        // The button itself confirms the result (no extra status line).
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
        try { launcher.Launch(card.Game); status = Text.Format("GamesLaunching", card.Name); global::Lotus.Core.Achievements.Activity.Record(global::Lotus.Core.Achievements.Activity.Launch); }
        catch (Exception ex)
        {
            log(ex); status = Text.Format("GamesLaunchFailed", card.Name);
            LaunchFailed?.Invoke(this, Text.Format("GamesLaunchFailed", card.Name)); // shown on any screen (Home has no status line)
        }
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
            Games.Add(Card(g));
        // Home shortcuts: up to 7 games, regardless of the search box.
        Featured.Clear();
        foreach (var g in Visible.OrderByDescending(g => IsFavorite?.Invoke(g.Key) == true).ThenByDescending(g => g.LastPlayed ?? DateTimeOffset.MinValue).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Take(7)) Featured.Add(Card(g));
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(EmptyMessage)); OnPropertyChanged(nameof(HasFeatured));
    }
    public ObservableCollection<GameCard> Featured { get; } = [];
    // Set by the app: favorites (player journal) come first on Home, then the most recently played.
    public Func<string, bool>? IsFavorite { get; set; }
    public void RefreshFeatured() => ApplyFilter();
    // Installed games (not hidden), for the Game list "you have it" marks and the game page Play button.
    public IReadOnlyList<GameEntry> InstalledGames() => [.. all.Where(g => !hidden.Contains(g.Key))];
    public void Play(GameEntry game) => Launch(Card(game));
    public event EventHandler<string>? LaunchFailed;
    // "Continue playing": the most recently played game (Steam keeps the date), else the first one in the library.
    public GameEntry? LastPlayed() => InstalledGames().OrderByDescending(g => g.LastPlayed ?? DateTimeOffset.MinValue).FirstOrDefault();
    public bool HasFeatured => Featured.Count > 0;
    // The library game that is running now (used by the in-game hotkey), or null.
    public GameEntry? RunningGame() => processCheck?.FirstRunning([.. all.Where(g => !hidden.Contains(g.Key))]);
    private GameCard Card(GameEntry g)
    {
        var shader = ScsKillerOutput.Match(shaderStatuses, g);
        return new(g, g.Name, g.Store, Initials(g.Name), g.CoverPath, Text["GamesPlay"], Text["GamesHide"], LaunchCommand, RemoveCommand, Text["GsMenu"], Text["GsMeasureMenu"],
                shader is null ? null : Text["ScsState" + shader.State], shader?.State is ShaderState.Ready or ShaderState.Stale, Text["ScsPrecompile"], shader?.Id, Text["ScsOpen"], Text["PlayPrepare"], Text["WhyMenu"], Text["GamesOptimizeMenu"], Text["GiDetails"]);
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
                var r = SettingsAdvisor.Recommend(p, Goal?.Invoke() ?? "balanced");
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
    public Task<string> ApplySettingsAsync(GameEntry game, SettingsRecommendation recommendation) => SettingsOp(game, () =>
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

    // Runs a settings write off the UI thread; any IO/registry/JSON failure becomes a status message, never a crash.
    private async Task<string> SettingsOp(GameEntry game, Func<string> work)
    {
        string key;
        try { key = await Task.Run(work); }
        catch (Exception ex) { log(ex); key = "GsWriteFailed"; }
        status = Text.Format(key, game.Name); Notify();
        return key;
    }

    public Task<string> RestoreSettingsAsync(GameEntry game) => SettingsOp(game, () =>
        {
            var (recipe, _) = Recipe(game);
            if (recipe is null || settingsStore?.LoadOriginal(game.Key) is not { } original) return "GsNoOriginal";
            if (processCheck?.IsRunning(game) == true) return "GsCloseGame";
            recipe.WriteRaw(game, original);
            if (recipe.ReadRaw(game) != original) return "GsVerifyFailed";
            settingsStore.ClearOriginal(game.Key);
            return "GsRestored";
        });

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
            var target = SettingsAdvisor.Recommend(profileSource!.Read(), Goal?.Invoke() ?? "balanced").TargetFps;
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
    public Task<string> ApplyStepAsync(GameEntry game) => SettingsOp(game, () =>
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


    // ---- Game panel ("Otimizar este jogo"): what the game supports, settings for this PC, in-game tips, last check ----
    public IGameTechScanner? TechScanner { get; set; }
    // Expected FPS line from the Lotus Score (set by the window); null when there is no full test or the game is unknown.
    public Func<GameEntry, SettingsRecommendation, SystemProfile, string?>? ScoreLine { get; set; }
    public Func<string>? ScoreHint { get; set; } // shown when there is no estimate (no full test yet, or a game the Score does not know)
    private readonly Dictionary<string, StutterReport> lastReports = new(StringComparer.OrdinalIgnoreCase);
    public StutterReport? LastReport(GameEntry game) => lastReports.GetValueOrDefault(game.Key);
    public sealed record GameTune(GameSettingsAnalysis Settings, GameTech Tech, IReadOnlyList<string> TechChips, IReadOnlyList<(string Title, string Body)> Tips, StutterReport? Last, bool Running, string? ScoreLine = null, string? ScoreHint = null, LowLatencyInfo? LowLatency = null);
    // Low latency through RivaTuner: State = "ok" | "noRtss" | "notNvidia" | "noExe"; On = current state of that game's profile.
    public sealed record LowLatencyInfo(string State, string? Exe, bool On);
    public Lotus.Core.Integrations.IRtssIntegration? Rtss { get; set; }

    public async Task<GameTune?> TuneAsync(GameEntry game)
    {
        if (await AnalyzeSettingsAsync(game) is not { } settings) return null;
        try
        {
            return await Task.Run(() =>
            {
                var tech = TechScanner?.Scan(game) ?? GameTech.Unknown;
                var chips = new List<string>();
                if (tech.Engine is { } engine) chips.Add(engine);
                if (tech.Dlss) chips.Add("DLSS"); if (tech.FrameGen) chips.Add(Text["IgChipFrameGen"]); if (tech.Fsr) chips.Add("FSR"); if (tech.Xess) chips.Add("XeSS");
                var last = LastReport(game);
                var tips = InGameAdvisor.Advise(tech, settings.Recommendation, profileSource!.Read(), last?.Findings, Goal?.Invoke() ?? "balanced")
                    .Select(t => { object[] a = [.. t.Args.Select(Localize)]; return (Text.Format(t.Key + "Title", a), Text.Format(t.Key + "Body", a)); }).ToList();
                var pc = profileSource.Read();
                return new GameTune(settings, tech, chips, tips, last, processCheck?.FirstRunning([game]) is not null, ScoreLine?.Invoke(game, settings.Recommendation, pc), ScoreHint?.Invoke(), LowLatencyFor(tech, pc));
            });
        }
        catch (Exception ex) { log(ex); return null; }
        // Advisor arguments are enum names (modes, tiers) or numbers; show them in the player's language.
        string Localize(string a) => Enum.TryParse<UpscaleMode>(a, out var m) && !int.TryParse(a, out _) ? Text["GsMode" + m]
            : Enum.TryParse<QualityTier>(a, out var q) && !int.TryParse(a, out _) ? Text["GsTier" + q] : a;
    }

    private LowLatencyInfo LowLatencyFor(GameTech tech, SystemProfile pc)
    {
        var nvidia = System.Text.RegularExpressions.Regex.IsMatch(pc.GpuName, "NVIDIA|GeForce|RTX|GTX", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!nvidia) return new("notNvidia", tech.MainExe, false);
        if (Rtss?.State().Installed != true) return new("noRtss", tech.MainExe, false);
        if (tech.MainExe is not { } exe) return new("noExe", null, false);
        return new("ok", exe, Rtss.LowLatencyOn(exe) == true);
    }

    // Turns the RivaTuner Reflex limiter on (cap = monitor-based FPS cap) or restores that game's profile.
    public async Task<bool> SetLowLatencyAsync(string exe, bool on, int fpsCap)
    {
        if (Rtss is null) return false;
        try
        {
            var ok = on ? await Rtss.EnableLowLatencyAsync(exe, fpsCap) : await Rtss.DisableLowLatencyAsync(exe);
            status = Text[ok ? (on ? "LlOn" : "LlOff") : "LlFailed"]; Notify();
            return ok;
        }
        catch (Exception ex) { log(ex); status = Text["LlFailed"]; Notify(); return false; }
    }
    // ---- Optional shader pre-compilation (SCSKiller) ----
    public const string ScsKillerReleases = "https://github.com/BlueHeisenberg/SCSKiller/releases/latest";
    public bool ShaderToolInstalled => shaders?.IsAvailable == true;
    public bool ShowShaderToolOffer => shaders is not null && !ShaderToolInstalled && loaded && all.Count > 0;

    // ---- Por que está travando? ----
    public IMemoryPressure? Memory { get; set; }
    public sealed record StutterReport(string Summary, IReadOnlyList<StutterFinding> Findings, double AverageFps = 0, double Low1Fps = 0, int Spikes = 0, DateTimeOffset? When = null);

    // Measures the running game, then ranks likely causes. Changes nothing; fixes are offered separately.
    public async Task<StutterReport?> DiagnoseAsync(GameEntry game, int seconds = 30, int startDelaySeconds = 5, CancellationToken token = default)
    {
        if (!CanMeasure || processCheck!.Find(game) is not { } run) { status = Text.Format("GsMeasureNotRunning", game.Name); Notify(); return null; }
        try
        {
            for (var left = startDelaySeconds; left > 0; left--) { status = Text.Format("GsMeasureStartsIn", left); Notify(); await Task.Delay(1000, token); }
            status = Text.Format("WhyMeasuring", seconds); Notify();
            var gpu = gpuSampler?.AverageAsync(TimeSpan.FromSeconds(seconds), token) ?? Task.FromResult<double?>(null);
            var session = await capture!.CaptureAsync(new(new(run.Pid, run.Executable, run.StartedUtc), seconds, Elevated: true, "Lotus: " + game.Name, "{}"), token);
            var gpuBusy = await gpu;
            if (session.Data?.Statistics is not { } s) { status = Text["GsMeasureFailed"]; Notify(); return null; }
            var (recipe, support) = Recipe(game);
            var raw = support is { CanApply: true } ? recipe!.ReadRaw(game) : null;
            var canLower = raw is not null && recipe!.StepQuality(raw, -1) is not null;
            var target = SettingsAdvisor.Recommend(profileSource!.Read(), Goal?.Invoke() ?? "balanced").TargetFps;
            var pending = GameProfile is null ? 0 : (await GameProfile.PreviewAsync()).Count(p => !p.AlreadyApplied);
            var (load, apps) = Memory is null ? (0d, (IReadOnlyList<string>)[]) : await Task.Run(() => Memory.Read(game), token);
            var findings = StutterDiagnosis.Analyze(new(s.AverageFps, s.Low1Fps, s.SpikeCount, s.DurationSeconds, gpuBusy, target,
                ScsKillerOutput.Match(shaderStatuses, game)?.State, pending, load, apps, canLower));
            if (findings.Any(f => f.Fix == StutterFix.LowerQuality)) pendingSteps[game.Key] = -1; // ready for "Otimizar configurações" after closing
            lastReports[game.Key] = new(Text.Format("WhySummary", s.AverageFps, s.Low1Fps, s.SpikeCount, s.DurationSeconds), findings, s.AverageFps, s.Low1Fps, s.SpikeCount, DateTimeOffset.Now);
            var summary = Text.Format("WhySummary", s.AverageFps, s.Low1Fps, s.SpikeCount, s.DurationSeconds);
            status = summary; Notify();
            return new(summary, findings, s.AverageFps, s.Low1Fps, s.SpikeCount, DateTimeOffset.Now);
        }
        catch (OperationCanceledException) { status = ""; Notify(); return null; }
        catch (Exception ex) { log(ex); status = Text["GsMeasureFailed"]; Notify(); return null; }
    }

    // The only fix applied while the game runs: the Game profile (journaled, reversible).
    public async Task<bool> ApplyGameProfileNowAsync()
    {
        if (GameProfile is null) return false;
        try { await GameProfile.PreviewAsync(); var ok = await GameProfile.ApplyAsync(); status = Text[ok ? "WhyProfileApplied" : "PlayApplyPartial"]; Notify(); return ok; }
        catch (Exception ex) { log(ex); status = Text["PlayFailed"]; Notify(); return false; }
    }

    // ---- Preparar e jogar ----
    public Func<string>? Goal { get; set; } // player profile goal: fps | competitive | balanced
    public IGameProfile? GameProfile { get; set; }
    public IGameWatch? Watch { get; set; }
    public event EventHandler<PlaySessionEnded>? SessionEnded;
    public bool CanPrepare => GameProfile is not null;

    // What would change before playing; nothing is applied here.
    public async Task<PlayPlan?> PlanPlayAsync(GameCard card)
    {
        if (GameProfile is null) return null;
        try
        {
            status = Text.Format("PlayAnalyzing", card.Name); Notify();
            var steps = await GameProfile.PreviewAsync();
            var shader = ScsKillerOutput.Match(shaderStatuses, card.Game);
            status = ""; Notify();
            return new(card, steps, card.CanPrecompile && ShaderToolInstalled, shader?.State == ShaderState.Stale);
        }
        catch (Exception ex) { log(ex); status = Text["PlayFailed"]; Notify(); return null; }
    }

    // Applies the confirmed plan, optionally pre-loads shaders, launches and follows the game until it closes.
    public async Task PlayAsync(PlayPlan plan, bool applyProfile, bool precompile, CancellationToken token = default)
    {
        var card = plan.Card; var applied = false;
        try
        {
            if (applyProfile && plan.Pending > 0 && GameProfile is not null)
            {
                status = Text.Format("PlayApplying", card.Name); Notify();
                applied = await GameProfile.ApplyAsync();
                if (!applied) { status = Text["PlayApplyPartial"]; Notify(); }
            }
            if (precompile && plan.OfferShaders) await PrecompileAsync(card, token);
            Launch(card);
            if (Watch is null) return;
            status = Text.Format("PlayWatching", card.Name); Notify();
            var played = await Watch.WaitForSessionAsync(card.Game, TimeSpan.FromMinutes(3), token);
            status = played is null ? Text.Format("PlayNotStarted", card.Name) : Text.Format("PlayEnded", card.Name, (int)played.Value.TotalMinutes); Notify();
            SessionEnded?.Invoke(this, new(card.Game, applied || (applyProfile && plan.Pending == 0), played ?? TimeSpan.Zero));
        }
        catch (OperationCanceledException) { status = ""; Notify(); }
        catch (Exception ex) { log(ex); status = Text["PlayFailed"]; Notify(); }
    }

    public async Task<bool> UndoProfileAsync()
    {
        if (GameProfile is null) return false;
        try { var ok = await GameProfile.UndoAsync(); status = Text[ok ? "PlayUndone" : "PlayUndoConflict"]; Notify(); return ok; }
        catch (Exception ex) { log(ex); status = Text["PlayUndoConflict"]; Notify(); return false; }
    }

    // Live job card: game, stage, bar (done/total) and time left from SCSKiller's own rate.
    private CancellationTokenSource? shaderCts;
    public bool ShaderJobActive { get; private set; }
    public string ShaderJobTitle { get; private set; } = "";
    public string ShaderJobDetail { get; private set; } = "";
    public string ShaderJobEta { get; private set; } = "";
    public double ShaderJobPercent { get; private set; }
    public bool ShaderJobIndeterminate { get; private set; } = true;
    public TaskState ShaderTask { get; } = new();
    public bool ShaderCardVisible => ShaderJobActive || ShaderTask.Done;
    public void DismissShaderCard() { ShaderTask.Reset(); NotifyShaderJob(); }

    public async Task<bool> PrecompileAsync(GameCard card, CancellationToken token = default)
    {
        if (shaders is not { IsAvailable: true } || card.ShaderId is not { } id || ShaderJobActive) return false;
        shaderCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var started = DateTime.UtcNow;
        ShaderJobActive = true; ShaderJobTitle = Text.Format("ScsJobTitle", card.Name); ShaderJobDetail = Text["ScsStageWaiting"]; ShaderJobEta = ""; ShaderJobPercent = 0; ShaderJobIndeterminate = true;
        ShaderTask.Start();
        status = ""; NotifyShaderJob();
        try
        {
            var progress = new Progress<string>(line => OnShaderLine(line, started));
            var code = await shaders.CompileAsync(id, progress, shaderCts.Token);
            shaderStatuses = await shaders.StatusAsync(shaderCts.Token);
            ApplyFilter();
            status = Text.Format(code == 0 ? "ScsDone" : "ScsFailed", card.Name);
            if (code == 0) ShaderTask.Finish(Text.Format("ScsDone", card.Name)); else ShaderTask.Fail();
            return code == 0;
        }
        catch (OperationCanceledException) { status = Text.Format("ScsCancelled", card.Name); ShaderTask.Fail(); return false; }
        catch (Exception ex) { log(ex); status = Text.Format("ScsFailed", card.Name); ShaderTask.Fail(); return false; }
        finally { ShaderJobActive = false; shaderCts?.Dispose(); shaderCts = null; NotifyShaderJob(); Notify(); }
    }
    public void CancelPrecompile() => shaderCts?.Cancel();
    public bool OpenShaderTool() { try { return shaders?.OpenApp() == true; } catch (Exception ex) { log(ex); return false; } }

    private void OnShaderLine(string line, DateTime started)
    {
        if (ScsKillerOutput.ParseProgress(line) is not { } p) return; // other lines (messages) stay in SCSKiller's own log
        var stage = p.Stage is "Waiting" or "Indexing" or "Planning" or "Materializing" or "Warming" or "Paused" or "Done" or "Failed" or "Stopped" ? Text["ScsStage" + p.Stage] : p.Stage;
        ShaderJobIndeterminate = p.Fraction is null;
        if (p.Fraction is { } f) ShaderJobPercent = f * 100;
        if (p.Fraction is { } fr) ShaderTask.Report(fr * 100);
        ShaderJobDetail = p.Total > 0
            ? Text.Format(p.Stage == "Indexing" ? "ScsJobPackages" : "ScsJobCount", stage, p.Done.ToString("N0", Culture), p.Total.ToString("N0", Culture), p.PerSecond.ToString("N0", Culture))
            : stage;
        ShaderJobEta = p.Remaining is { } left ? Text.Format("ScsJobEta", Duration(left), Duration(DateTime.UtcNow - started)) : "";
        NotifyShaderJob();
    }
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);
    private string Duration(TimeSpan t) => t.TotalHours >= 1 ? Text.Format("DurHours", (int)t.TotalHours, t.Minutes)
        : t.TotalMinutes >= 1 ? Text.Format("DurMinutes", (int)t.TotalMinutes, t.Seconds) : Text.Format("DurSeconds", Math.Max(1, (int)t.TotalSeconds));
    private void NotifyShaderJob()
    {
        foreach (var p in new[] { nameof(ShaderJobActive), nameof(ShaderJobTitle), nameof(ShaderJobDetail), nameof(ShaderJobEta), nameof(ShaderJobPercent), nameof(ShaderJobIndeterminate), nameof(ShaderCardVisible) }) OnPropertyChanged(p);
    }

    public void RefreshLanguage() { BuildStores(); ApplyFilter(); Notify(); }
    private void Notify()
    {
        foreach (var p in new[] { nameof(Status), nameof(IsBusy), nameof(IsEmpty), nameof(EmptyMessage), nameof(Summary), nameof(ShowShaderToolOffer) }) OnPropertyChanged(p);
    }
}
