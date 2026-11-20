namespace Lotus.Core.Brand;

// Opening intro (José, 19/11/2026): the full 7.4 s intro the first time Lotus opens and after each update; a short
// cut (~2.4 s) on the other openings. Any click or Esc skips it and Settings can turn it off.
public enum IntroKind { None, Full, Short }

public static class IntroPlan
{
    public const string SkipArgument = "--no-intro";
    public const string ForceArgument = "--intro";   // plays it even in a development instance (to test)

    public static IntroKind Decide(bool enabled, bool reduceMotion, string? seenVersion, string currentVersion,
        bool minimized, bool restarted, bool devInstance, bool skipArgument, bool forceArgument)
    {
        if (skipArgument || minimized || restarted) return IntroKind.None;
        if (devInstance && !forceArgument) return IntroKind.None;   // script-driven test instances open straight away
        if (!enabled || reduceMotion) return IntroKind.None;
        return string.Equals(seenVersion, currentVersion, StringComparison.OrdinalIgnoreCase) ? IntroKind.Short : IntroKind.Full;
    }
}
