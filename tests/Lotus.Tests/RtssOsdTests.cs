using System.IO.MemoryMappedFiles;
using System.Text;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class RtssOsdTests
{
    // Same header layout as RTSSSharedMemoryV2: signature, version, app entries, then OSD entries and the OSD frame counter.
    private static MemoryMappedFile FakeRtss(string name, uint entrySize = 512 + 4096 + 4096, uint slots = 8)
    {
        const uint osdOffset = 64;
        var map = MemoryMappedFile.CreateNew(name, osdOffset + entrySize * slots);
        using var v = map.CreateViewAccessor();
        v.Write(0, 0x52545353u); v.Write(4, 0x00020015u); v.Write(20, entrySize); v.Write(24, osdOffset); v.Write(28, slots); v.Write(32, 0u);
        var other = Encoding.ASCII.GetBytes("MSI Afterburner\0"); v.WriteArray(osdOffset + entrySize + 256, other, 0, other.Length); // slot 1 taken by another app
        return map;
    }
    private static string Read(MemoryMappedViewAccessor v, long at, int max) { var b = new byte[max]; v.ReadArray(at, b, 0, max); var e = Array.IndexOf(b, (byte)0); return Encoding.Latin1.GetString(b, 0, e < 0 ? max : e); }

    [Fact] public void ClaimsAFreeSlotWritesTextAndReleasesIt()
    {
        var name = "LotusOsdTest-" + Guid.NewGuid(); const uint entry = 512 + 4096 + 4096;
        using var map = FakeRtss(name);
        using var v = map.CreateViewAccessor();
        var osd = new RtssOsd(name);
        Assert.True(osd.Show("<C=F27BA8>LOTUS<C> Analisando 27 s"));
        long slot2 = 64 + entry * 2;
        Assert.Equal("Lotus", Read(v, slot2 + 256, 256)); Assert.Equal("<C=F27BA8>LOTUS<C> Analisando 27 s", Read(v, slot2 + 512, 4096));
        Assert.Equal("MSI Afterburner", Read(v, 64 + entry + 256, 256)); // never touches another app's slot
        Assert.Equal(1u, v.ReadUInt32(32));
        Assert.True(osd.Show("26 s")); Assert.Equal("26 s", Read(v, slot2 + 512, 4096)); Assert.Equal(2u, v.ReadUInt32(32)); // same slot reused
        Assert.True(osd.Clear()); Assert.Equal("", Read(v, slot2 + 256, 256)); Assert.Equal("", Read(v, slot2 + 512, 4096));
        Assert.False(new RtssOsd("NoSuchMap-" + Guid.NewGuid()).Show("x")); // RTSS not running
    }
}
