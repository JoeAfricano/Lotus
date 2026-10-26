using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Lotus.App;

// Lotus sound kit. Controls (buttons, toggles, lists, flyouts) use the built-in Fluent sounds; Lotus's own
// moments use short kalimba-like tones synthesised in memory (nothing shipped or downloaded), all in the same
// key (G major pentatonic) so they never clash. Settings › Interface sounds turns everything off; the volume
// slider scales both the Fluent sounds and these tones.
public static class UiSound
{
    public enum Cue { Done, Fail, Score, Welcome, Toggle, Open, Close, Navigate, Startup, Notify }
    private static bool enabled = true;
    private static double volume = 0.6;
    private static readonly Dictionary<Cue, IntPtr> cache = []; // unmanaged copies: async playback reads them after Play returns
    private static readonly object gate = new();
    private static DateTime lastPlay;

    public static bool Enabled
    {
        get => enabled;
        set { enabled = value; ApplyElementSounds(); if (!value) PlaySound(IntPtr.Zero, IntPtr.Zero, 0); }
    }

    // 0..1. Rebuilds the tones at the new level (they are tiny) and scales the Fluent sounds.
    public static double Volume
    {
        get => volume;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (Math.Abs(v - volume) < 0.001) return;
            volume = v;
            lock (gate) { foreach (var p in cache.Values) Marshal.FreeHGlobal(p); cache.Clear(); }
            ApplyElementSounds();
        }
    }

    private static void ApplyElementSounds()
    {
        ElementSoundPlayer.State = enabled && volume > 0 ? ElementSoundPlayerState.On : ElementSoundPlayerState.Off;
        ElementSoundPlayer.Volume = Math.Clamp(volume * 0.8, 0, 1);
    }

    public static void Play(Cue cue)
    {
        if (!enabled || volume <= 0) return;
        // Navigation and toggles can fire in bursts; one tone per 60 ms is plenty.
        if (cue is Cue.Navigate or Cue.Toggle && (DateTime.UtcNow - lastPlay).TotalMilliseconds < 60) return;
        lastPlay = DateTime.UtcNow;
        IntPtr sound;
        lock (gate)
        {
            if (!cache.TryGetValue(cue, out sound))
            {
                var wav = Build(cue);
                sound = Marshal.AllocHGlobal(wav.Length); Marshal.Copy(wav, 0, sound, wav.Length);
                cache[cue] = sound;
            }
        }
        if (!PlaySound(sound, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault)) Serilog.Log.Debug("UI sound {Cue} not played", cue);
    }

    // Notes as (frequency Hz, start ms, length ms, level 0..1). G major pentatonic: G A B D E.
    private const double G4 = 392.0, B4 = 493.9, D5 = 587.3, E5 = 659.3, G5 = 784.0, A5 = 880.0, B5 = 987.8, D6 = 1174.7, E6 = 1318.5, G6 = 1568.0, B6 = 1975.5;
    private static byte[] Build(Cue cue) => cue switch
    {
        Cue.Done => Synth([(D6, 0, 300, 0.85), (G6, 85, 520, 0.8)]),                                   // rising fourth: "ok"
        Cue.Fail => Synth([(E5, 0, 260, 0.75), (B4, 130, 480, 0.7)]),                                  // gentle fall, not an alarm
        Cue.Score => Synth([(G5, 0, 320, 0.7), (B5, 100, 320, 0.7), (D6, 200, 340, 0.75), (G6, 310, 900, 0.85)]), // arpeggio up
        Cue.Welcome => Synth([(G4, 0, 700, 0.55), (D5, 170, 700, 0.6), (G5, 340, 800, 0.65), (B5, 520, 1100, 0.6)]),
        Cue.Startup => Synth([(D5, 0, 520, 0.5), (G5, 140, 700, 0.55), (D6, 280, 900, 0.45)]),         // soft "Lotus is here"
        Cue.Navigate => Synth([(B5, 0, 110, 0.35), (E6, 35, 160, 0.3)]),                                // airy flick between sections
        Cue.Open => Synth([(G5, 0, 160, 0.5), (D6, 55, 240, 0.45)]),                                   // a panel slides in
        Cue.Close => Synth([(D6, 0, 150, 0.4), (G5, 55, 230, 0.38)]),                                  // ...and out
        Cue.Toggle => Synth([(A5, 0, 90, 0.45), (E6, 25, 120, 0.3)]),                                  // star, small switches
        Cue.Notify => Synth([(E6, 0, 220, 0.6), (B6, 120, 380, 0.5)]),                                 // something needs a look
        _ => Synth([(G5, 0, 200, 0.5)]),
    };

    // Kalimba-ish voice: fundamental + the metal bar's inharmonic partial (×2.76, fast decay) + a quiet octave.
    private static byte[] Synth((double Hz, int StartMs, int LengthMs, double Level)[] notes)
    {
        const int rate = 44100;
        var master = 0.08 + 0.42 * volume; // ~-22 dB .. -6 dB peak
        var total = notes.Max(n => n.StartMs + n.LengthMs) * rate / 1000 + rate / 40;
        var mix = new double[total];
        foreach (var (hz, start, length, level) in notes)
        {
            var s0 = start * rate / 1000; var count = length * rate / 1000; var seconds = length / 1000.0;
            for (var i = 0; i < count && s0 + i < total; i++)
            {
                var t = (double)i / rate;
                var attack = Math.Min(1, i / (rate * 0.004));                 // 4 ms, no click
                var decay = Math.Exp(-t * 6.0 / (seconds + 0.05));
                var release = Math.Min(1, (count - i) / (rate * 0.025));        // fade the tail
                var v = Math.Sin(2 * Math.PI * hz * t)
                      + 0.32 * Math.Sin(2 * Math.PI * hz * 2.76 * t) * Math.Exp(-t * 22)
                      + 0.12 * Math.Sin(2 * Math.PI * hz * 2.0 * t + 0.4) * Math.Exp(-t * 9);
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
