using System.Reflection;
using TudoMKV.Core;
using TudoMKV.Core.Models;

namespace TudoMKV.Muxing;

/// <summary>Monta a linha de comando do ffmpeg para remuxar um <see cref="MuxJob"/> em MKV sem recodificar.</summary>
public static class FfmpegArguments
{
    /// <summary>Codecs de legenda que o Matroska não aceita por cópia direta, e o formato para o qual convertemos.</summary>
    private static readonly Dictionary<string, string> SubtitleConversions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mov_text"] = "srt",
    };

    /// <summary>Nome gravado como "Writing application" do MKV, como o MakeMKV faz.</summary>
    public static string WritingApplication { get; } = "TudoMKV " + AppVersion();

    /// <param name="encodedAt">Data de gravação do MKV; por padrão, agora.</param>
    public static IReadOnlyList<string> Build(MuxJob job, DateTimeOffset? encodedAt = null)
    {
        var date = (encodedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var sources = job.AllSources;

        if (job.Tracks.Count == 0)
        {
            throw new ArgumentException("Nenhuma faixa selecionada.", nameof(job));
        }

        if (job.ChaptersSourceIndex < 0 || job.ChaptersSourceIndex >= sources.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(job), "A origem de capítulos não existe.");
        }

        var args = new List<string> { "-hide_banner", "-nostdin", "-y" };
        var mediaInputIndexes = new List<int>(sources.Count);
        foreach (var source in sources)
        {
            mediaInputIndexes.Add(mediaInputIndexes.Count);
            args.AddRange(source.InputArguments);
            args.AddRange(["-i", source.Path]);
        }

        var chapterInputIndexes = new Dictionary<int, int>();
        var nextInputIndex = mediaInputIndexes.Count;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i].ChaptersFile is not { } chapters)
            {
                continue;
            }

            args.AddRange(["-i", chapters]);
            chapterInputIndexes[i] = nextInputIndex++;
        }

        // As faixas geradas (ex.: o estéreo de um áudio mono) saem de um filtro e não podem ser copiadas.
        var filters = new List<string>();
        var maps = new List<string>(job.Tracks.Count);
        for (var output = 0; output < job.Tracks.Count; output++)
        {
            var track = job.Tracks[output];
            if (track.InputIndex < 0 || track.InputIndex >= sources.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(job), "Uma faixa aponta para uma origem inexistente.");
            }

            var input = $"{mediaInputIndexes[track.InputIndex]}:{track.Index}";
            if (track.Conversion is { } conversion)
            {
                var label = $"conv{output}";
                filters.Add($"[{input}]{conversion.Filter}[{label}]");
                maps.Add($"[{label}]");
            }
            else
            {
                maps.Add(input);
            }
        }

        if (filters.Count > 0)
        {
            args.AddRange(["-filter_complex", string.Join(';', filters)]);
        }

        foreach (var map in maps)
        {
            args.AddRange(["-map", map]);
        }

        var chaptersInput = chapterInputIndexes.TryGetValue(job.ChaptersSourceIndex, out var chaptersFileInput)
            ? chaptersFileInput.ToString()
            : mediaInputIndexes[job.ChaptersSourceIndex].ToString();
        args.AddRange(["-map_chapters", job.KeepChapters ? chaptersInput : "-1"]);
        args.AddRange(["-c", "copy"]);
        args.AddRange(["-metadata", $"encoding_tool={WritingApplication}"]);
        args.AddRange(["-metadata", $"creation_time={date:yyyy-MM-ddTHH:mm:ss}Z"]);

        for (var output = 0; output < job.Tracks.Count; output++)
        {
            var track = job.Tracks[output];

            if (track.Conversion is { } conversion)
            {
                args.AddRange([$"-c:{output}", conversion.Codec]);
                args.AddRange([$"-b:{output}", $"{conversion.BitRateKbps}k"]);
            }
            else if (track.Type == TrackType.Subtitle && SubtitleConversions.TryGetValue(track.Codec, out var target))
            {
                args.AddRange([$"-c:{output}", target]);
            }

            if (track.Type == TrackType.Attachment)
            {
                // Anexos não têm idioma nem disposição de reprodução.
                continue;
            }

            args.AddRange([$"-metadata:s:{output}", $"language={NormalizeLanguage(track.Language)}"]);
            args.AddRange([$"-metadata:s:{output}", $"title={track.Title ?? string.Empty}"]);
            args.AddRange([$"-disposition:{output}", Disposition(track)]);

            if (track.SourceId is { } sourceId)
            {
                // O MediaInfo só confia em SOURCE_ID listado nas tags de estatística com data igual ou posterior à do MKV.
                args.AddRange([$"-metadata:s:{output}", $"SOURCE_ID={sourceId}"]);
                args.AddRange([$"-metadata:s:{output}", "_STATISTICS_TAGS=SOURCE_ID"]);
                args.AddRange([$"-metadata:s:{output}", $"_STATISTICS_WRITING_APP={WritingApplication}"]);
                args.AddRange([$"-metadata:s:{output}", $"_STATISTICS_WRITING_DATE_UTC={date:yyyy-MM-dd HH:mm:ss}"]);
            }
        }

        // Evita erros de intercalação com legendas esparsas.
        args.AddRange(["-max_interleave_delta", "0"]);
        args.AddRange(["-progress", "pipe:1", "-nostats"]);
        args.AddRange(["-f", "matroska", job.OutputPath]);

        return args;
    }

    private static string AppVersion()
    {
        var version = typeof(FfmpegArguments).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }

    private static string Disposition(MediaTrack track) => (track.IsDefault, track.IsForced) switch
    {
        (true, true) => "default+forced",
        (true, false) => "default",
        (false, true) => "forced",
        _ => "0",
    };

    private static string NormalizeLanguage(string language) =>
        string.IsNullOrWhiteSpace(language) ? "und" : language.Trim().ToLowerInvariant();
}
