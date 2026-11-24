using Lotus.Infrastructure.Games;

namespace Lotus.Infrastructure.Integrations;

// Only the built-in OverlayEditor flag is changed. Unrelated RTSS settings and hook caches are preserved.
public static class RtssOverlayPlugin
{
    private const string BackupName = "OverlayEditor.plugin.original";
    public static void ActivateLayout(string layout,string layoutConfig,string pluginConfig,string backups,string overlays,string? globalProfile=null)
    {
        var files=new List<string>{layoutConfig,pluginConfig,Path.Combine(backups,"OverlayEditor.cfg.original"),Path.Combine(backups,BackupName)};
        if(globalProfile is not null){files.Add(globalProfile);files.Add(Path.Combine(backups,RtssOverlayBackground.BackupName));}
        PreserveOnFailure(() => { LotusOverlayLayout.Activate(layout,layoutConfig,backups,overlays); Enable(pluginConfig,backups);
            if(globalProfile is not null){if(Path.GetFileName(layout).StartsWith("Lotus-Premium-",StringComparison.OrdinalIgnoreCase))RtssOverlayBackground.Apply(globalProfile,backups);else if(!RtssOverlayBackground.Restore(globalProfile,backups))throw new IOException("OSD background restoration conflicted.");else RtssOverlayBackground.Apply(globalProfile,backups,premium:false);}return true; },files.ToArray());
    }
    public static bool RestoreLayout(string layoutConfig,string pluginConfig,string backups,string? globalProfile=null)
    {
        var files=new List<string>{layoutConfig,pluginConfig,Path.Combine(backups,"OverlayEditor.cfg.original"),Path.Combine(backups,BackupName)};
        if(globalProfile is not null){files.Add(globalProfile);files.Add(Path.Combine(backups,RtssOverlayBackground.BackupName));}
        return PreserveOnFailure(()=> (globalProfile is null||RtssOverlayBackground.Restore(globalProfile,backups)) && Restore(pluginConfig,backups) && LotusOverlayLayout.Restore(layoutConfig,backups),files.ToArray());
    }
    internal static bool PreserveOnFailure(Func<bool> action,string[] files)
    {
        var before=files.ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null);
        void RollBack(){foreach(var (p,bytes) in before){if(bytes is null){if(File.Exists(p))File.Delete(p);}else File.WriteAllBytes(p,bytes);}}
        bool success;
        try{success=action();}
        catch(Exception original)
        {
            try{RollBack();}catch(Exception rollback){throw new IOException("Overlay activation failed and rollback could not be completed.",new AggregateException(original,rollback));}
            throw;
        }
        if(!success)RollBack();
        return success;
    }
    // A RivaTuner that was installed but never opened (fresh PC, silent winget install) has no Config file or no [Plugins]
    // list yet: RTSS writes it on first run, and a plugin missing from it is off. Both cases count as "0" (off).
    public static void Enable(string config, string backups, string plugin = "OverlayEditor.dll")
    {
        var ini = new IniText(File.Exists(config) ? RtssProfiles.ReadAnsi(config) : "");
        var current = ini.Get("Plugins", plugin) ?? "0";
        if (current is not ("0" or "1")) throw new InvalidDataException("Unsupported RTSS plugin configuration.");
        Directory.CreateDirectory(backups);
        var backup = Path.Combine(backups, BackupFor(plugin));
        if (!File.Exists(backup))
        {
            using var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write);
            stream.WriteByte((byte)current[0]);
        }
        if (current == "1") return;
        ini.Set("Plugins", plugin, "1");
        RtssProfiles.WriteAnsi(config, ini.ToString());
        if (new IniText(RtssProfiles.ReadAnsi(config)).Get("Plugins", plugin) != "1")
            throw new IOException("RTSS plugin flag was not verified.");
    }
    internal static string BackupFor(string plugin) => plugin == "OverlayEditor.dll" ? BackupName : Path.GetFileNameWithoutExtension(plugin) + ".plugin.original";
    public static bool Restore(string config, string backups, string plugin = "OverlayEditor.dll")
    {
        var backup = Path.Combine(backups, BackupFor(plugin));
        if (!File.Exists(backup)) return true; // backups from older Lotus versions did not change this flag
        var original = File.ReadAllText(backup);
        if (original is not ("0" or "1")) return false;
        if (!File.Exists(config)) { File.Delete(backup); return true; } // RivaTuner reinstalled or reset: nothing left to restore
        var ini = new IniText(RtssProfiles.ReadAnsi(config));
        var current = ini.Get("Plugins", plugin) ?? "0";
        if (current != "1" && current != original) return false; // a later user change wins
        ini.Set("Plugins", plugin, original);
        RtssProfiles.WriteAnsi(config, ini.ToString());
        if (new IniText(RtssProfiles.ReadAnsi(config)).Get("Plugins", plugin) != original) return false;
        File.Delete(backup);
        return true;
    }
}

// The overlay show/hide key, kept by RivaTuner's own HotkeyHandler plugin ([Settings] OSDToggleHotkey in
// Plugins\Client\HotkeyHandler.cfg, read when RivaTuner starts). Only that key and the plugin flag change; the first
// originals are kept, and "none" gives them back (or clears a key Lotus never saved).
public static class RtssOverlayHotkey
{
    public const string Plugin = "HotkeyHandler.dll", BackupName = "HotkeyHandler.cfg.original";
    public static string DefaultConfig => Path.Combine(RtssInstall.Dir, "Plugins", "Client", "HotkeyHandler.cfg");
    public static uint Read(string config, string? pluginConfig = null)
    {
        if (pluginConfig is not null && File.Exists(pluginConfig) && new IniText(RtssProfiles.ReadAnsi(pluginConfig)).Get("Plugins", Plugin) == "0") return 0; // plugin off: no key works
        if (!File.Exists(config)) return 0;
        return Parse(new IniText(RtssProfiles.ReadAnsi(config)).Get("Settings", "OSDToggleHotkey"));
    }
    // "00000091" (RivaTuner) or "00000091h" (MSI Afterburner); 0 when absent or unreadable.
    public static uint Parse(string? value) => uint.TryParse(value?.Trim().TrimEnd('h', 'H'), System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    public static void Apply(string choice, string config, string pluginConfig, string backups)
    {
        if (!Lotus.Core.Integrations.OverlayHotkeys.Choices.Contains(choice)) throw new InvalidOperationException("Unknown overlay hotkey.");
        var files = new[] { config, pluginConfig, Path.Combine(backups, BackupName), Path.Combine(backups, RtssOverlayPlugin.BackupFor(Plugin)) };
        RtssOverlayPlugin.PreserveOnFailure(() =>
        {
            if (Lotus.Core.Integrations.OverlayHotkeys.Code(choice) is not { } code)
            {
                if (File.Exists(Path.Combine(backups, BackupName)))
                {
                    RtssProfiles.RestoreBackup(Path.Combine(backups, BackupName), config);
                    if (!RtssOverlayPlugin.Restore(pluginConfig, backups, Plugin)) throw new IOException("Hotkey plugin flag restoration conflicted.");
                }
                else if (File.Exists(config)) Write(config, 0);
                return true;
            }
            Directory.CreateDirectory(backups);
            RtssProfiles.BackupOnce(config, Path.Combine(backups, BackupName));
            Write(config, code);
            RtssOverlayPlugin.Enable(pluginConfig, backups, Plugin);
            return true;
        }, files);
    }
    private static void Write(string config, uint code)
    {
        var ini = new IniText(File.Exists(config) ? RtssProfiles.ReadAnsi(config) : "");
        ini.Set("Settings", "OSDToggleHotkey", code.ToString("X8"));
        RtssProfiles.WriteAnsi(config, ini.ToString());
        if (Parse(new IniText(RtssProfiles.ReadAnsi(config)).Get("Settings", "OSDToggleHotkey")) != code) throw new IOException("Overlay hotkey was not verified.");
    }
}
