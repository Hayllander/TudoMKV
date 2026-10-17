using System.Text;

namespace TudoMKV.Muxing;

/// <summary>
/// O VLC 3 só passa MPEG-2 de MKV pelo seu packetizer quando o "Muxing application" contém "libmakemkv"
/// (modules/demux/mkv/matroska_segment_parse.cpp). Sem ele, ao avançar ou voltar com a decodificação por
/// hardware ligada, a imagem fica verde até o fim da cena, mesmo com o vídeo idêntico ao do MakeMKV.
/// Reescrevemos o MuxingApp no próprio lugar (sem mudar tamanhos) para o VLC tratar o arquivo igual.
/// </summary>
public static class VlcMpeg2Compat
{
    internal const string Marker = "libmakemkv";

    private const uint SegmentId = 0x18538067;
    private const uint InfoId = 0x1549A966;
    private const uint MuxingAppId = 0x4D80;
    private const int HeaderBytes = 64 * 1024;

    /// <summary>Marca o MKV; devolve false se não achou o MuxingApp ou se não cabe o texto.</summary>
    public static bool Apply(string mkvPath)
    {
        using var stream = new FileStream(mkvPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        return Apply(stream);
    }

    internal static bool Apply(Stream stream)
    {
        var header = new byte[(int)Math.Min(HeaderBytes, stream.Length)];
        stream.Position = 0;
        stream.ReadExactly(header);

        if (FindMuxingApp(header) is not var (offset, length) || length < Marker.Length)
        {
            return false;
        }

        // Strings EBML podem terminar em zeros; o que sobra do texto antigo vira preenchimento.
        var value = new byte[length];
        Encoding.ASCII.GetBytes(Marker).CopyTo(value, 0);
        stream.Position = offset;
        stream.Write(value);
        return true;
    }

    private static (int Offset, int Length)? FindMuxingApp(byte[] data)
    {
        // Cabeçalho EBML, depois o Segment; o Info é filho direto do Segment e o MuxingApp, do Info.
        if (!TryReadElement(data, 0, out _, out var ebmlData, out var ebmlSize))
        {
            return null;
        }

        var position = ebmlData + ebmlSize;
        if (!TryReadElement(data, position, out var id, out var segmentData, out _) || id != SegmentId)
        {
            return null;
        }

        position = segmentData;
        while (TryReadElement(data, position, out id, out var childData, out var childSize))
        {
            if (id == InfoId)
            {
                var inner = (long)childData;
                var end = Math.Min(childData + childSize, data.Length);
                while (inner < end && TryReadElement(data, inner, out var innerId, out var innerData, out var innerSize))
                {
                    if (innerId == MuxingAppId)
                    {
                        return innerData + innerSize <= data.Length ? (innerData, (int)innerSize) : null;
                    }

                    inner = innerData + innerSize;
                }

                return null;
            }

            position = childData + childSize;
        }

        return null;
    }

    private static bool TryReadElement(byte[] data, long position, out uint id, out int dataOffset, out long size)
    {
        id = 0;
        dataOffset = 0;
        size = 0;
        if (position < 0 || position >= data.Length)
        {
            return false;
        }

        var p = (int)position;
        if (!TryReadVint(data, ref p, keepMarker: true, out var rawId) || rawId > uint.MaxValue)
        {
            return false;
        }

        if (!TryReadVint(data, ref p, keepMarker: false, out var rawSize))
        {
            return false;
        }

        id = (uint)rawId;
        dataOffset = p;
        size = (long)Math.Min(rawSize, (ulong)int.MaxValue * 4); // tamanho desconhecido = todos os bits em 1
        return true;
    }

    private static bool TryReadVint(byte[] data, ref int position, bool keepMarker, out ulong value)
    {
        value = 0;
        if (position >= data.Length || data[position] == 0)
        {
            return false;
        }

        var first = data[position];
        var length = 1;
        while ((first & (0x80 >> (length - 1))) == 0)
        {
            length++;
        }

        if (position + length > data.Length)
        {
            return false;
        }

        value = keepMarker ? first : (ulong)(first & (0xFF >> length));
        for (var i = 1; i < length; i++)
        {
            value = (value << 8) | data[position + i];
        }

        position += length;
        return true;
    }
}
