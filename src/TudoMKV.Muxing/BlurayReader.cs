using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TudoMKV.Core;
using TudoMKV.Core.Models;
using TudoMKV.Disc.Bluray;

namespace TudoMKV.Muxing;

/// <summary>
/// Lê os títulos (playlists) de um Blu-ray em C# e monta, para cada um, uma lista de concatenação do ffmpeg
/// com os trechos dos .m2ts e um arquivo de capítulos. Assim não dependemos do protocolo <c>bluray:</c>,
/// que só aceita playlists acima de 3 minutos.
/// </summary>
public sealed class BlurayReader(FfprobeInspector inspector) : IDiscReader
{
    public bool CanRead(string path) => BlurayScanner.LooksLikeBluray(path);

    public async Task<DiscContents> ReadAsync(string path, TimeSpan minimumLength, CancellationToken cancellationToken = default)
    {
        var disc = await Task.Run(() => BlurayScanner.Scan(path, minimumLength), cancellationToken);
        var messages = new List<string> { $"Abrindo Blu-ray em {path}" };
        messages.AddRange(disc.Skipped.Select(s => $"Título #{s.Playlist} foi pulado pois {s.Reason}"));

        var workDirectory = WorkDirectory(disc.Root);
        var sources = new List<MediaSource>();
        for (var i = 0; i < disc.Titles.Count; i++)
        {
            var title = disc.Titles[i];
            var number = i + 1;
            sources.Add(await ReadTitleAsync(disc, title, number, workDirectory, cancellationToken));
            foreach (var clip in title.Content.Items.Select(c => c.Clip).Distinct())
            {
                messages.Add($"Arquivo {clip}.m2ts foi adicionado ao título #{number}");
            }
        }

        return new DiscContents(SourceKind.Bluray, disc.Label, sources, messages);
    }

    private async Task<MediaSource> ReadTitleAsync(BlurayDisc disc, BlurayTitle title, int number, string workDirectory, CancellationToken cancellationToken)
    {
        var concat = Path.Combine(workDirectory, title.Playlist + ".ffconcat");
        await File.WriteAllTextAsync(concat, BuildConcat(title), cancellationToken);

        string? chapters = null;
        if (title.Content.Chapters.Count > 0)
        {
            chapters = Path.Combine(workDirectory, title.Playlist + ".chapters.txt");
            await File.WriteAllTextAsync(chapters, BuildChapters(title.Content), cancellationToken);
        }

        // A concatenação perde os PIDs; as faixas (mesma ordem) vêm do primeiro .m2ts, que os tem.
        var probed = await inspector.InspectAsync(title.ClipPaths[0], cancellationToken);
        ApplyPlaylistStreams(probed.Tracks, title.Content.Streams);
        DiscTracks.MarkDefaults(probed.Tracks);

        return new MediaSource
        {
            Path = concat,
            InputArguments = ["-f", "concat", "-safe", "0"],
            ChaptersFile = chapters,
            Name = $"{disc.Label} · Título {number}",
            OutputBaseName = $"{DiscTracks.SafeFileName(disc.Label)}_t{number:00}",
            IsDisc = true,
            FormatName = "bluray",
            Duration = title.Duration,
            ChapterCount = title.Content.Chapters.Count,
            SizeBytes = title.SizeBytes,
            Tracks = probed.Tracks,
        };
    }

    /// <summary>Idiomas vêm da playlist (o .m2ts não os tem); faixas fora da playlist ficam desmarcadas.</summary>
    internal static void ApplyPlaylistStreams(IReadOnlyList<MediaTrack> tracks, IReadOnlyList<BlurayStream> streams)
    {
        if (streams.Count == 0)
        {
            return;
        }

        foreach (var track in tracks)
        {
            var declared = streams.FirstOrDefault(s => s.Pid == track.StreamId);
            if (declared is null)
            {
                track.Selected = false;
                continue;
            }

            if (declared.Language != "und")
            {
                track.Language = declared.Language;
            }
        }
    }

    internal static string BuildConcat(BlurayTitle title)
    {
        var builder = new StringBuilder("ffconcat version 1.0\n");
        for (var i = 0; i < title.Content.Items.Count; i++)
        {
            var item = title.Content.Items[i];
            var path = title.ClipPaths[i].Replace("'", "'\\''", StringComparison.Ordinal);
            builder.Append(CultureInfo.InvariantCulture, $"file '{path}'\n");
            builder.Append(CultureInfo.InvariantCulture, $"inpoint {item.In:0.######}\n");
            builder.Append(CultureInfo.InvariantCulture, $"outpoint {item.Out:0.######}\n");
            builder.Append(CultureInfo.InvariantCulture, $"duration {item.Duration:0.######}\n");
        }

        return builder.ToString();
    }

    internal static string BuildChapters(Playlist playlist)
    {
        var builder = new StringBuilder(";FFMETADATA1\n");
        var total = (long)(playlist.Duration * 1000);
        for (var i = 0; i < playlist.Chapters.Count; i++)
        {
            var start = (long)(playlist.Chapters[i] * 1000);
            var end = i + 1 < playlist.Chapters.Count ? (long)(playlist.Chapters[i + 1] * 1000) : total;
            if (end <= start)
            {
                continue;
            }

            builder.Append(CultureInfo.InvariantCulture, $"[CHAPTER]\nTIMEBASE=1/1000\nSTART={start}\nEND={end}\ntitle=Capítulo {i + 1:00}\n");
        }

        return builder.ToString();
    }

    /// <summary>Pasta temporária por disco para as listas de concatenação e capítulos.</summary>
    private static string WorkDirectory(string root)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..12];
        var directory = Path.Combine(Path.GetTempPath(), "TudoMKV", hash);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
