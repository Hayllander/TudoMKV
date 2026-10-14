using System.Buffers.Binary;
using System.Text;

namespace TudoMKV.Disc.Bluray;

/// <summary>Um trecho de um clipe (.m2ts) tocado pela playlist, com tempos em segundos na linha do tempo do clipe.</summary>
public sealed record PlayItem(string Clip, double In, double Out)
{
    public double Duration => Out - In;
}

public enum BlurayStreamKind
{
    Video,
    Audio,
    Subtitle,
}

/// <summary>Uma faixa declarada na tabela STN da playlist.</summary>
public sealed record BlurayStream(int Pid, BlurayStreamKind Kind, int CodingType, string Language);

/// <summary>Conteúdo de um arquivo .mpls: clipes, capítulos (em segundos desde o início) e faixas.</summary>
public sealed record Playlist(IReadOnlyList<PlayItem> Items, IReadOnlyList<double> Chapters, IReadOnlyList<BlurayStream> Streams, int Angles)
{
    public double Duration => Items.Sum(i => i.Duration);
}

/// <summary>Lê playlists de Blu-ray (BDMV/PLAYLIST/*.mpls). Os tempos no disco usam um relógio de 45 kHz.</summary>
public static class MplsParser
{
    private const double Clock = 45000.0;

    public static Playlist Parse(byte[] data)
    {
        var signature = Encoding.ASCII.GetString(data, 0, 4);
        if (signature != "MPLS")
        {
            throw new InvalidDataException("Arquivo MPLS inválido.");
        }

        var playListStart = (int)U32(data, 8);
        var marksStart = (int)U32(data, 12);

        var itemCount = U16(data, playListStart + 6);
        var items = new List<PlayItem>(itemCount);
        var itemStarts = new List<double>(itemCount);
        IReadOnlyList<BlurayStream> streams = [];
        var angles = 1;

        var position = playListStart + 10;
        var elapsed = 0.0;
        for (var i = 0; i < itemCount; i++)
        {
            var length = U16(data, position);
            var item = position + 2;

            var clip = Encoding.ASCII.GetString(data, item, 5);
            var multiAngle = (data[item + 10] & 0x10) != 0;
            var inTime = U32(data, item + 12) / Clock;
            var outTime = U32(data, item + 16) / Clock;

            var stn = item + 32;
            if (multiAngle)
            {
                angles = Math.Max(angles, data[stn]);
                stn += 2 + (data[stn] - 1) * 10;
            }

            // As faixas do primeiro item valem para a playlist inteira (os outros repetem a mesma tabela).
            if (i == 0)
            {
                streams = ParseStn(data, stn);
            }

            items.Add(new PlayItem(clip, inTime, outTime));
            itemStarts.Add(elapsed);
            elapsed += outTime - inTime;
            position += 2 + length;
        }

        var chapters = new List<double>();
        var markCount = U16(data, marksStart + 4);
        for (var m = 0; m < markCount; m++)
        {
            var mark = marksStart + 6 + m * 14;
            if (data[mark + 1] != 1)
            {
                continue; // só marcas de entrada (capítulos)
            }

            var itemIndex = U16(data, mark + 2);
            if (itemIndex >= items.Count)
            {
                continue;
            }

            var time = itemStarts[itemIndex] + U32(data, mark + 4) / Clock - items[itemIndex].In;
            if (chapters.Count == 0 || time - chapters[^1] > 0.5)
            {
                chapters.Add(Math.Max(0, time));
            }
        }

        return new Playlist(items, chapters, streams, angles);
    }

    private static List<BlurayStream> ParseStn(byte[] data, int stn)
    {
        var video = data[stn + 4];
        var audio = data[stn + 5];
        var pg = data[stn + 6] + data[stn + 10];
        var position = stn + 16;

        var streams = new List<BlurayStream>();
        Read(BlurayStreamKind.Video, video);
        Read(BlurayStreamKind.Audio, audio);
        Read(BlurayStreamKind.Subtitle, pg);
        return streams;

        void Read(BlurayStreamKind kind, int count)
        {
            for (var s = 0; s < count; s++)
            {
                var entryLength = data[position];
                var entryType = data[position + 1];
                var pid = entryType switch
                {
                    1 => U16(data, position + 2),
                    2 => U16(data, position + 4),
                    _ => U16(data, position + 3),
                };
                position += 1 + entryLength;

                var attributesLength = data[position];
                var coding = data[position + 1];
                var language = kind switch
                {
                    BlurayStreamKind.Audio => Encoding.ASCII.GetString(data, position + 3, 3),
                    BlurayStreamKind.Subtitle when coding == 0x92 => Encoding.ASCII.GetString(data, position + 3, 3),
                    BlurayStreamKind.Subtitle => Encoding.ASCII.GetString(data, position + 2, 3),
                    _ => "und",
                };
                position += 1 + attributesLength;

                streams.Add(new BlurayStream(pid, kind, coding, CleanLanguage(language)));
            }
        }
    }

    private static string CleanLanguage(string language)
    {
        var trimmed = language.Trim('\0', ' ').ToLowerInvariant();
        return trimmed.Length == 3 && trimmed.All(char.IsAsciiLetterLower) ? trimmed : "und";
    }

    private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));

    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
}
