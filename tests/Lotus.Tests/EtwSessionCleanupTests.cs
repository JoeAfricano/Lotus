using Lotus.Infrastructure.Benchmark;
using Xunit;

namespace Lotus.Tests;

// Stellar Blade study, 25/11/2026: stuck ETW sessions made a new PresentMon lose every event and record nothing.
public sealed class EtwSessionCleanupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lotus-etw-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void Only_sessions_named_by_Lotus_are_listed_for_stopping()
    {
        const string logman = """
            Conjunto de Coletores de Dados          Tipo                          Status
            -------------------------------------------------------------------------------
            Circular Kernel Context Logger          Rastreamento                  Em execução
            PresentMonDataProvider                  Rastreamento                  Em execução
            Lotus-0123456789abcdef0123456789abcdef  Rastreamento                  Em execução
            LotusPacing-fedcba9876543210fedcba9876543210 Rastreamento             Em execução
            LotusNoVrr-0123456789ABCDEF0123456789ABCDEF Rastreamento              Em execução
            Lotus-short                             Rastreamento                  Em execução
            NotLotus-0123456789abcdef0123456789abcdef Rastreamento                Em execução

            O comando foi concluído com êxito.
            """;
        Assert.Equal(["Lotus-0123456789abcdef0123456789abcdef", "LotusPacing-fedcba9876543210fedcba9876543210", "LotusNoVrr-0123456789ABCDEF0123456789ABCDEF"],
            EtwSessionCleanup.LotusSessions(logman));
        Assert.Empty(EtwSessionCleanup.LotusSessions(""));
    }

    [Fact]
    public void A_recording_without_frames_makes_the_next_one_clean_first()
    {
        Assert.False(EtwSessionCleanup.NeedsCleanup(root, []));
        Assert.True(EtwSessionCleanup.NeedsCleanup(root, ["Lotus-0123456789abcdef0123456789abcdef"])); // left by a crash
        EtwSessionCleanup.MarkSuspect(root, "NoFrames");
        Assert.True(EtwSessionCleanup.NeedsCleanup(root, []));
        EtwSessionCleanup.ClearSuspect(root);
        Assert.False(EtwSessionCleanup.NeedsCleanup(root, []));
    }

    [Fact]
    public void Date_time_column_gets_the_time_zone_back_the_right_way()
    {
        // 20:41 local at UTC-3 was written as 17:41 by PresentMon 2.6 --date_time.
        Assert.Equal(new DateTime(2026, 11, 25, 20, 41, 5), PresentMonCsv.LocalTime("2026-11-25 17:41:05.123", TimeSpan.FromHours(-3))!.Value.AddMilliseconds(-123));
        Assert.Null(PresentMonCsv.LocalTime("not a time", TimeSpan.Zero));
    }
}
