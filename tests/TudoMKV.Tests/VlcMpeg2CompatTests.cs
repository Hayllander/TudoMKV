using System.Text;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

public class VlcMpeg2CompatTests
{
    [Fact]
    public void RewritesMuxingApplicationInPlace()
    {
        var file = BuildMkvHeader("Lavf62.15.100");
        using var stream = new MemoryStream();
        stream.Write(file);

        Assert.True(VlcMpeg2Compat.Apply(stream));

        var patched = stream.ToArray();
        Assert.Equal(file.Length, patched.Length);
        var text = Encoding.ASCII.GetString(patched);
        Assert.Contains("libmakemkv\0\0\0", text);
        Assert.Contains("TudoMKV 0.4.5", text);
        Assert.DoesNotContain("Lavf", text);
    }

    [Fact]
    public void LeavesFileAloneWhenTheTextDoesNotFit()
    {
        var file = BuildMkvHeader("Lavf");
        using var stream = new MemoryStream();
        stream.Write(file);

        Assert.False(VlcMpeg2Compat.Apply(stream));
        Assert.Equal(file, stream.ToArray());
    }

    /// <summary>EBML + Segment (tamanho desconhecido) + SeekHead vazio + Info com MuxingApp e WritingApp.</summary>
    private static byte[] BuildMkvHeader(string muxingApp)
    {
        static byte[] Element(byte[] id, byte[] payload) => [.. id, (byte)(0x80 | payload.Length), .. payload];

        var info = Element([0x15, 0x49, 0xA9, 0x66],
        [
            .. Element([0x2A, 0xD7, 0xB1], [0x0F, 0x42, 0x40]),
            .. Element([0x4D, 0x80], Encoding.ASCII.GetBytes(muxingApp)),
            .. Element([0x57, 0x41], Encoding.ASCII.GetBytes("TudoMKV 0.4.5")),
        ]);
        byte[] segment = [0x18, 0x53, 0x80, 0x67, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            .. Element([0x11, 0x4D, 0x9B, 0x74], []), .. info];
        return [.. Element([0x1A, 0x45, 0xDF, 0xA3], Element([0x42, 0x82], Encoding.ASCII.GetBytes("matroska"))), .. segment];
    }
}
