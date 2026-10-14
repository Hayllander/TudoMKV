using System.Buffers.Binary;
using System.Text;

namespace TudoMKV.Disc.Dvd;

/// <summary>Entrada da tabela de títulos (TT_SRPT) do VIDEO_TS.IFO.</summary>
public sealed record VmgTitle(int Number, int Angles, int Chapters, int TitleSet, int TitleSetTitle);

/// <summary>
/// Lê as tabelas dos arquivos IFO de um DVD-Video. Os IFO nunca são criptografados,
/// então dá para listar títulos mesmo de discos que não conseguimos gravar.
/// </summary>
public static class IfoParser
{
    private const int SectorSize = 2048;

    public static IReadOnlyList<VmgTitle> ParseVmgTitles(byte[] vmg)
    {
        RequireSignature(vmg, "DVDVIDEO-VMG");
        var table = Sector(vmg, 0xC4);
        var count = ReadUInt16(vmg, table);

        var titles = new List<VmgTitle>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = table + 8 + i * 12;
            titles.Add(new VmgTitle(
                Number: i + 1,
                Angles: vmg[entry + 1],
                Chapters: ReadUInt16(vmg, entry + 2),
                TitleSet: vmg[entry + 6],
                TitleSetTitle: vmg[entry + 7]));
        }

        return titles;
    }

    /// <summary>
    /// Duração de um título dentro do seu VTS: soma das PGCs (cadeias de programa) que os capítulos usam.
    /// Também devolve a primeira PGC, que identifica títulos repetidos.
    /// </summary>
    public static (TimeSpan Duration, int FirstPgc) ParseTitleDuration(byte[] vts, int titleSetTitle)
    {
        RequireSignature(vts, "DVDVIDEO-VTS");

        var pttTable = Sector(vts, 0xC8);
        var titleCount = ReadUInt16(vts, pttTable);
        if (titleSetTitle < 1 || titleSetTitle > titleCount)
        {
            throw new InvalidDataException($"O título {titleSetTitle} não existe neste VTS.");
        }

        var lastByte = (int)ReadUInt32(vts, pttTable + 4);
        var start = pttTable + (int)ReadUInt32(vts, pttTable + 8 + (titleSetTitle - 1) * 4);
        var end = titleSetTitle < titleCount
            ? pttTable + (int)ReadUInt32(vts, pttTable + 8 + titleSetTitle * 4)
            : pttTable + lastByte + 1;

        var pgcs = new List<int>();
        for (var ptt = start; ptt + 4 <= end; ptt += 4)
        {
            var pgc = ReadUInt16(vts, ptt);
            if (!pgcs.Contains(pgc))
            {
                pgcs.Add(pgc);
            }
        }

        if (pgcs.Count == 0)
        {
            throw new InvalidDataException($"O título {titleSetTitle} não tem capítulos.");
        }

        var pgcTable = Sector(vts, 0xCC);
        var pgcCount = ReadUInt16(vts, pgcTable);
        var duration = TimeSpan.Zero;
        foreach (var pgc in pgcs.Where(p => p >= 1 && p <= pgcCount))
        {
            var pgcStart = pgcTable + (int)ReadUInt32(vts, pgcTable + 8 + (pgc - 1) * 8 + 4);
            duration += ParseBcdTime(vts.AsSpan(pgcStart + 4, 4));
        }

        return (duration, pgcs[0]);
    }

    /// <summary>Tempo no formato do DVD: horas, minutos e segundos em BCD, e quadros com a taxa nos 2 bits altos.</summary>
    internal static TimeSpan ParseBcdTime(ReadOnlySpan<byte> time)
    {
        var frameRate = (time[3] >> 6) switch
        {
            1 => 25.0,
            3 => 30000.0 / 1001,
            _ => 0,
        };
        var frames = Bcd((byte)(time[3] & 0x3F));
        var seconds = Bcd(time[0]) * 3600 + Bcd(time[1]) * 60 + Bcd(time[2]);
        return TimeSpan.FromSeconds(seconds + (frameRate > 0 ? frames / frameRate : 0));
    }

    private static int Bcd(byte value) => (value >> 4) * 10 + (value & 0x0F);

    private static void RequireSignature(byte[] ifo, string signature)
    {
        if (ifo.Length < 0x400 || Encoding.ASCII.GetString(ifo, 0, 12) != signature)
        {
            throw new InvalidDataException($"Arquivo IFO inválido (esperava {signature}).");
        }
    }

    private static int Sector(byte[] ifo, int offset)
    {
        var position = (long)ReadUInt32(ifo, offset) * SectorSize;
        if (position <= 0 || position >= ifo.Length)
        {
            throw new InvalidDataException("Tabela fora do arquivo IFO.");
        }

        return (int)position;
    }

    private static ushort ReadUInt16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));

    private static uint ReadUInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
}
