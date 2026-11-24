using System.Text.Json;
using Lotus.Infrastructure.Games;

namespace Lotus.Infrastructure.Integrations;

// The default OSD fill covers the entire rectangle from (0,0) to a top-centered layout. The artwork already has alpha.
public static class RtssOverlayBackground
{
    internal const string BackupName="OverlayEditor.background.original.json";
    static readonly Dictionary<string,string> Desired=new(){["EnableOSD"]="1",["EnableFill"]="0",["EnableBgnd"]="0"};
    // premium: the OSD on with RivaTuner's own fill and box off (the artwork has alpha). Other layouts: only the OSD on, so a
    // fresh RivaTuner (or one with "Show On-Screen Display" off) shows the chosen style. The first originals are kept.
    public static void Apply(string profile,string backups,bool premium=true)
    {
        var ini=new IniText(File.Exists(profile)?RtssProfiles.ReadAnsi(profile):""); // a never-opened RivaTuner may not have written Global yet
        var original=Desired.Keys.ToDictionary(k=>k,k=>ini.Get("OSD",k));
        if(original.Values.Any(v=>v is not (null or "0" or "1")))throw new InvalidDataException("Unsupported OSD flag.");
        Directory.CreateDirectory(backups);var backup=Path.Combine(backups,BackupName);
        if(!File.Exists(backup))File.WriteAllText(backup,JsonSerializer.Serialize(original));
        var wanted=premium?Desired:Desired.Where(p=>p.Key=="EnableOSD").ToDictionary(p=>p.Key,p=>p.Value);
        foreach(var (key,value) in wanted)ini.Set("OSD",key,value);
        RtssProfiles.WriteAnsi(profile,ini.ToString());
        var verify=new IniText(RtssProfiles.ReadAnsi(profile));
        if(wanted.Any(p=>verify.Get("OSD",p.Key)!=p.Value))throw new IOException("OSD flags were not verified.");
    }
    public static bool Restore(string profile,string backups)
    {
        var backup=Path.Combine(backups,BackupName);if(!File.Exists(backup))return true;
        var original=JsonSerializer.Deserialize<Dictionary<string,string?>>(File.ReadAllText(backup));
        if(original is null||original.Count!=3||original.Keys.Any(k=>!Desired.ContainsKey(k))||original.Values.Any(v=>v is not (null or "0" or "1")))return false;
        if(!File.Exists(profile)){File.Delete(backup);return true;} // RivaTuner reinstalled or reset: nothing left to restore
        var raw=RtssProfiles.ReadAnsi(profile);var ini=new IniText(raw);
        if(original.Any(p=>ini.Get("OSD",p.Key)!=Desired[p.Key]&&ini.Get("OSD",p.Key)!=p.Value))return false;
        foreach(var (key,value) in original)if(value is not null)ini.Set("OSD",key,value);
        raw=ini.ToString();
        foreach(var key in original.Where(p=>p.Value is null).Select(p=>p.Key))
        {
            bool osd=false;var lines=new List<string>();
            foreach(var line in raw.Split("\r\n")){var trim=line.Trim();if(trim.StartsWith('['))osd=trim.Equals("[OSD]",StringComparison.OrdinalIgnoreCase);if(osd&&trim.StartsWith(key+"=",StringComparison.OrdinalIgnoreCase))continue;lines.Add(line);}
            raw=string.Join("\r\n",lines);
        }
        RtssProfiles.WriteAnsi(profile,raw);
        var verify=new IniText(RtssProfiles.ReadAnsi(profile));if(original.Any(p=>verify.Get("OSD",p.Key)!=p.Value))return false;
        File.Delete(backup);return true;
    }
}
