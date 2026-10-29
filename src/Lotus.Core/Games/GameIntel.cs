namespace Lotus.Core.Games;

// What Lotus can tell about a game from its install folder (read-only): the upscalers and frame generation it ships,
// and the engine. Libraries are what these features install next to the game, so the signal is real, not guessed.
public sealed record GameTech(bool Dlss, bool FrameGen, bool Fsr, bool Xess, string? Engine)
{
    public static readonly GameTech Unknown = new(false, false, false, false, null);
    public bool AnyUpscaler => Dlss || Fsr || Xess;

    // File names (lower case, no folder) → features. Kept small and documented on purpose.
    public static GameTech FromFiles(IEnumerable<string> fileNames)
    {
        bool dlss = false, fg = false, fsr = false, xess = false; string? engine = null;
        foreach (var raw in fileNames)
        {
            var f = raw.ToLowerInvariant();
            if (f == "nvngx_dlss.dll") dlss = true;
            else if (f == "nvngx_dlssg.dll") fg = true;
            else if (f == "libxess.dll" || f.StartsWith("libxess_")) xess = true;
            else if (f.StartsWith("amd_fidelityfx") || f.StartsWith("ffx_fsr") || f.Contains("fsr2") || f.Contains("fsr3")) fsr = true;
            if (engine is null)
                engine = f switch
                {
                    "unityplayer.dll" => "Unity",
                    "engine2.dll" => "Source 2",
                    "re_chunk_000.pak" => "RE Engine",
                    _ when f.EndsWith("-wingdk-shipping.exe") || f.EndsWith("-win64-shipping.exe") => "Unreal Engine",
                    _ when f.EndsWith(".utoc") || f.EndsWith(".ucas") => "Unreal Engine",
                    _ => null,
                };
        }
        return new(dlss, fg, fsr, xess, engine);
    }
}
public interface IGameTechScanner { GameTech Scan(GameEntry game); }

// In-game settings advice: what to change inside the game's own menus, most impactful first. Combines what the game
// supports (GameTech), what the PC can do (SettingsRecommendation) and, when there is one, the last in-game measurement.
public sealed record InGameTip(string Key, string[] Args, int Weight);

public static class InGameAdvisor
{
    public static IReadOnlyList<InGameTip> Advise(GameTech tech, SettingsRecommendation r, SystemProfile pc, IReadOnlyList<StutterFinding>? measured = null, string goal = "balanced")
    {
        var tips = new List<InGameTip>();
        var keys = measured?.Select(f => f.Key).ToHashSet() ?? [];
        bool gpuBound = keys.Contains("WhyGpuLimit"), cpuBound = keys.Contains("WhyCpuLimit");
        bool stutter = keys.Contains("WhyShaders") || keys.Contains("WhyUnknownStutter");
        var rtx = pc.GpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase);
        var frameGenGpu = rtx && System.Text.RegularExpressions.Regex.IsMatch(pc.GpuName, @"RTX\s*[45]0\d0", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var mode = r.Mode == UpscaleMode.Off ? UpscaleMode.Quality : r.Mode;
        var needsHeadroom = r.Mode != UpscaleMode.Off || gpuBound;

        // Upscaling: the biggest single win when the GPU is the limit. Use what the game actually ships.
        if (needsHeadroom)
        {
            if (tech.Dlss && rtx) tips.Add(new("IgDlss", [mode.ToString()], gpuBound ? 100 : 80));
            else if (tech.Xess && pc.GpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase)) tips.Add(new("IgXess", [mode.ToString()], gpuBound ? 95 : 75));
            else if (tech.Fsr) tips.Add(new("IgFsr", [mode.ToString()], gpuBound ? 95 : 75));
            else if (tech.Xess) tips.Add(new("IgXess", [mode.ToString()], gpuBound ? 90 : 70));
            else if (tech.Dlss && !rtx) tips.Add(new("IgDlssNoRtx", [], 40));
        }
        if (tech.FrameGen && frameGenGpu && goal != "competitive") tips.Add(new("IgFrameGen", [], gpuBound ? 85 : 55));
        // Bottleneck-specific settings.
        if (gpuBound) tips.Add(new("IgGpuHeavy", [], 90));
        if (cpuBound) tips.Add(new("IgCpuHeavy", [], 90));
        // Engine traits.
        if (tech.Engine == "Unreal Engine") tips.Add(new("IgUnreal", [], stutter ? 88 : 35));
        else if (stutter) tips.Add(new("IgStutterCap", [r.FpsCap.ToString()], 70));
        // Starting preset for this PC, and textures limited by VRAM.
        tips.Add(new("IgPreset", [r.Tier.ToString(), r.TextureTier.ToString(), r.FpsCap.ToString()], 60));
        if (goal == "competitive") tips.Add(new("IgCompetitive", [], 65));
        return [.. tips.OrderByDescending(t => t.Weight)];
    }
}
