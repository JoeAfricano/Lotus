using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Lotus.App;

// Lotus interface sounds. Clicks, navigation and flyouts use the built-in Fluent sounds (ElementSoundPlayer);
// moments of their own (task done, failure, score, welcome) are short bell-like chimes synthesised in memory,
// so nothing is shipped or downloaded. Everything follows Settings › Interface sounds and stays quiet (-18 dB).
public static class UiSound
{
    public enum Cue { Done, Fail, Score, Welcome, Toggle, Open }
    private static bool enabled = true;
    private static readonly Dictionary<Cue, IntPtr> cache = []; // unmanaged copies: async playback reads them after Play returns
    private static readonly object gate = new();

    public static bool Enabled
    {
        get => enabled;
        set
        {
            enabled = value;
            ElementSoundPlayer.State = value ? ElementSoundPlayerState.On : ElementSoundPlayerState.Off;
            ElementSoundPlayer.Volume = 0.35;
            if (!value) PlaySound(IntPtr.Zero, IntPtr.Zero, 0); // stop anything still ringing
        }
    }

    public static void Play(Cue cue)
    {
        if (!enabled) return;
        IntPtr sound;
        lock (gate)
        {
            if (!cache.TryGetValue(cue, out sound))
            {
                var wav = Build(cue);
                sound = Marshal.AllocHGlobal(wav.Length); Marshal.Copy(wav, 0, sound, wav.Length);
                cache[cue] = sound; // kept for the life of the app (a few KB per cue)
            }
        }
        PlaySound(sound, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
    }

    // Notes as (frequency Hz, start ms, length ms, level 0..1). Soft attack, exponential decay, a quiet octave
    // partial for a bell colour. Pentatonic intervals keep everything consonant.
    private static byte[] Build(Cue cue) => cue switch
    {
        Cue.Done => Synth([(1318.5, 0, 260, 0.9), (1975.5, 90, 420, 0.75)]),                       // E6 → B6, rising "ok"
        Cue.Fail => Synth([(659.3, 0, 220, 0.8), (493.9, 120, 380, 0.7)]),                         // E5 → B4, gentle drop
        Cue.Score => Synth([(1046.5, 0, 300, 0.7), (1318.5, 110, 300, 0.7), (1568.0, 220, 320, 0.7), (2093.0, 340, 700, 0.8)]), // C major arpeggio
        Cue.Welcome => Synth([(784.0, 0, 500, 0.6), (1174.7, 160, 520, 0.6), (1568.0, 320, 900, 0.65)]), // G5 D6 G6, open fifth
        Cue.Toggle => Synth([(1760.0, 0, 90, 0.5)]),
        _ => Synth([(1174.7, 0, 160, 0.55), (1568.0, 60, 220, 0.5)]),
    };

    private static byte[] Synth((double Hz, int StartMs, int LengthMs, double Level)[] notes)
    {
        const int rate = 44100; const double master = 0.125; // about -18 dB
        var total = notes.Max(n => n.StartMs + n.LengthMs) * rate / 1000 + rate / 50;
        var mix = new double[total];
        foreach (var (hz, start, length, level) in notes)
        {
            var s0 = start * rate / 1000; var count = length * rate / 1000;
            for (var i = 0; i < count && s0 + i < total; i++)
            {
                var t = (double)i / rate;
                var attack = Math.Min(1, i / (rate * 0.006));                 // 6 ms, no click
                var decay = Math.Exp(-t * 7.5 / (length / 1000.0 + 0.05));
                var release = Math.Min(1, (count - i) / (rate * 0.02));         // fade the tail
                var v = Math.Sin(2 * Math.PI * hz * t) + 0.18 * Math.Sin(2 * Math.PI * hz * 2.01 * t) * Math.Exp(-t * 14);
                mix[s0 + i] += v * level * attack * decay * release;
            }
        }
        var peak = Math.Max(1, mix.Max(Math.Abs));
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + total * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(total * 2);
        foreach (var v in mix) w.Write((short)Math.Clamp(v / peak * master * short.MaxValue, short.MinValue, short.MaxValue));
        return ms.ToArray();
    }

    private const uint SndAsync = 0x0001, SndNoDefault = 0x0002, SndMemory = 0x0004;
    [DllImport("winmm.dll", SetLastError = false)] private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
}
