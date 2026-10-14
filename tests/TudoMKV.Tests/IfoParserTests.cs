using TudoMKV.Disc.Dvd;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

public class IfoParserTests
{
    [Fact]
    public void ParsesPalBcdTime()
    {
        // 01:23:45 e 12 quadros a 25 fps (bits 7-6 = 01).
        var time = IfoParser.ParseBcdTime([0x01, 0x23, 0x45, 0x40 | 0x12]);

        Assert.Equal(new TimeSpan(0, 1, 23, 45, 480).TotalSeconds, time.TotalSeconds, 3);
    }

    [Fact]
    public void ParsesNtscBcdTime()
    {
        // 00:00:10 e 15 quadros a 29,97 fps (bits 7-6 = 11).
        var time = IfoParser.ParseBcdTime([0x00, 0x00, 0x10, 0xC0 | 0x15]);

        Assert.Equal(10 + 15 / (30000.0 / 1001), time.TotalSeconds, 3);
    }

    [Fact]
    public void RejectsFilesThatAreNotIfo()
    {
        Assert.Throws<InvalidDataException>(() => IfoParser.ParseVmgTitles(new byte[4096]));
    }

    [Theory]
    [InlineData("MEU FILME", "MEU FILME")]
    [InlineData("A/B:C", "A_B_C")]
    [InlineData("   ", "DVD")]
    public void MakesSafeFileNames(string label, string expected)
    {
        Assert.Equal(expected, DvdReader.SafeFileName(label));
    }
}
