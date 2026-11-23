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
            if(globalProfile is not null){if(Path.GetFileName(layout).StartsWith("Lotus-Premium-",StringComparison.OrdinalIgnoreCase))RtssOverlayBackground.Apply(globalProfile,backups);else if(!RtssOverlayBackground.Restore(globalProfile,backups))throw new IOException("OSD background restoration conflicted.");}return true; },files.ToArray());
    }
    public static bool RestoreLayout(string layoutConfig,string pluginConfig,string backups,string? globalProfile=null)
    {
        var files=new List<string>{layoutConfig,pluginConfig,Path.Combine(backups,"OverlayEditor.cfg.original"),Path.Combine(backups,BackupName)};
        if(globalProfile is not null){files.Add(globalProfile);files.Add(Path.Combine(backups,RtssOverlayBackground.BackupName));}
        return PreserveOnFailure(()=> (globalProfile is null||RtssOverlayBackground.Restore(globalProfile,backups)) && Restore(pluginConfig,backups) && LotusOverlayLayout.Restore(layoutConfig,backups),files.ToArray());
    }
    private static bool PreserveOnFailure(Func<bool> action,string[] files)
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
    public static void Enable(string config, string backups)
    {
        var ini = new IniText(File.Exists(config) ? RtssProfiles.ReadAnsi(config) : "");
        var current = ini.Get("Plugins", "OverlayEditor.dll") ?? "0";
        if (current is not ("0" or "1")) throw new InvalidDataException("Unsupported RTSS plugin configuration.");
        Directory.CreateDirectory(backups);
        var backup = Path.Combine(backups, BackupName);
        if (!File.Exists(backup))
        {
            using var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write);
            stream.WriteByte((byte)current[0]);
        }
        if (current == "1") return;
        ini.Set("Plugins", "OverlayEditor.dll", "1");
        RtssProfiles.WriteAnsi(config, ini.ToString());
        if (new IniText(RtssProfiles.ReadAnsi(config)).Get("Plugins", "OverlayEditor.dll") != "1")
            throw new IOException("RTSS plugin flag was not verified.");
    }
    public static bool Restore(string config, string backups)
    {
        var backup = Path.Combine(backups, BackupName);
        if (!File.Exists(backup)) return true; // backups from older Lotus versions did not change this flag
        var original = File.ReadAllText(backup);
        if (original is not ("0" or "1")) return false;
        if (!File.Exists(config)) { File.Delete(backup); return true; } // RivaTuner reinstalled or reset: nothing left to restore
        var ini = new IniText(RtssProfiles.ReadAnsi(config));
        var current = ini.Get("Plugins", "OverlayEditor.dll") ?? "0";
        if (current != "1" && current != original) return false; // a later user change wins
        ini.Set("Plugins", "OverlayEditor.dll", original);
        RtssProfiles.WriteAnsi(config, ini.ToString());
        if (new IniText(RtssProfiles.ReadAnsi(config)).Get("Plugins", "OverlayEditor.dll") != original) return false;
        File.Delete(backup);
        return true;
    }
}
