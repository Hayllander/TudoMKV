using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TudoMKV.Core;
using TudoMKV.Core.Models;

namespace TudoMKV.Muxing;

/// <summary>Descreve um arquivo usando <c>ffprobe -print_format json</c>.</summary>
public sealed class FfprobeInspector(string? ffprobePath = null) : IMediaInspector
{
    public Task<MediaSource> InspectAsync(string path, CancellationToken cancellationToken = default) =>
        InspectAsync(path, [], cancellationToken);

    /// <summary>Descreve a origem passando opções de entrada ao ffprobe (ex.: <c>-f dvdvideo -title 2</c>).</summary>
    public async Task<MediaSource> InspectAsync(string path, IReadOnlyList<string> inputArguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(ffprobePath ?? ToolLocator.Require("ffprobe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string[] arguments =
        [
            "-v", "error", "-print_format", "json",
            "-show_format", "-show_streams", "-show_chapters",
            .. inputArguments, "-i", path,
        ];
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new MuxException("Não foi possível iniciar o ffprobe.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var log = await stderr;
            if (log.Contains("Unknown input format", StringComparison.Ordinal))
            {
                throw new MuxException(
                    "Este ffmpeg não lê discos. Use uma build com libdvdnav e libbluray, como as da BtbN ou a \"full\" da gyan.dev.",
                    log);
            }

            throw new MuxException($"O ffprobe não conseguiu ler {Path.GetFileName(Path.TrimEndingDirectorySeparator(path))}.", log);
        }

        return Parse(path, await stdout);
    }

    /// <summary>Converte a saída JSON do ffprobe no modelo do TudoMKV.</summary>
    internal static MediaSource Parse(string path, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tracks = new List<MediaTrack>();
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                tracks.Add(ParseStream(stream));
            }
        }

        string? formatName = null;
        var duration = TimeSpan.Zero;
        long size = 0;
        if (root.TryGetProperty("format", out var format))
        {
            formatName = GetString(format, "format_name");
            _ = long.TryParse(GetString(format, "size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
            if (double.TryParse(GetString(format, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                duration = TimeSpan.FromSeconds(seconds);
            }
        }

        var chapters = root.TryGetProperty("chapters", out var chapterArray) ? chapterArray.GetArrayLength() : 0;

        return new MediaSource
        {
            Path = path,
            FormatName = formatName,
            Duration = duration,
            ChapterCount = chapters,
            SizeBytes = size,
            Tracks = tracks,
        };
    }

    private static MediaTrack ParseStream(JsonElement stream)
    {
        var type = GetString(stream, "codec_type") switch
        {
            "video" => TrackType.Video,
            "audio" => TrackType.Audio,
            "subtitle" => TrackType.Subtitle,
            "attachment" => TrackType.Attachment,
            _ => TrackType.Data,
        };

        // Capas de MP4/MKV aparecem como vídeo com attached_pic; tratamos como anexo.
        var disposition = stream.TryGetProperty("disposition", out var d) ? d : default;
        if (type == TrackType.Video && GetFlag(disposition, "attached_pic"))
        {
            type = TrackType.Attachment;
        }

        var tags = stream.TryGetProperty("tags", out var t) ? t : default;
        var language = GetString(tags, "language");

        return new MediaTrack
        {
            Index = stream.GetProperty("index").GetInt32(),
            StreamId = ParseId(GetString(stream, "id")),
            SourceId = GetString(tags, "SOURCE_ID"),
            Type = type,
            Codec = GetString(stream, "codec_name") ?? GetString(tags, "mimetype") ?? "desconhecido",
            CodecLongName = GetString(stream, "codec_long_name"),
            Profile = GetString(stream, "profile"),
            Language = string.IsNullOrWhiteSpace(language) ? "und" : language,
            Title = GetString(tags, "title"),
            IsDefault = GetFlag(disposition, "default"),
            IsForced = GetFlag(disposition, "forced"),
            Width = GetInt(stream, "width"),
            Height = GetInt(stream, "height"),
            Channels = GetInt(stream, "channels"),
            SampleRate = int.TryParse(GetString(stream, "sample_rate"), out var rate) ? rate : null,
            BitRate = MaxBitRate(stream, tags),
            BitsPerSample = ParseBits(stream),
            Selected = type is not TrackType.Data,
        };
    }

    /// <summary>
    /// O bitrate do áudio: o que o ffprobe calcula ou o que as tags BPS/BITRATE trazem, que é o que um
    /// MKV do MakeMKV ou do mkvmerge guarda. Vale o maior, como no ShawScope-Audio.
    /// </summary>
    private static long? MaxBitRate(JsonElement stream, JsonElement tags)
    {
        var values = new List<long>();
        if (long.TryParse(GetString(stream, "bit_rate"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitRate))
        {
            values.Add(bitRate);
        }

        if (tags.ValueKind == JsonValueKind.Object)
        {
            foreach (var tag in tags.EnumerateObject())
            {
                if (IsBitRateTag(tag.Name)
                    && long.TryParse(tag.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromTag))
                {
                    values.Add(fromTag);
                }
            }
        }

        return values.Count > 0 ? values.Max() : null;
    }

    private static bool IsBitRateTag(string name) =>
        name.Contains("BPS", StringComparison.OrdinalIgnoreCase)
        || name.Contains("BITRATE", StringComparison.OrdinalIgnoreCase)
        || name.Contains("BIT_RATE", StringComparison.OrdinalIgnoreCase);

    /// <summary>A profundidade da amostra: bits_per_sample, bits_per_raw_sample ou o número do sample_fmt (s16, s24).</summary>
    private static int? ParseBits(JsonElement stream)
    {
        var sample = GetInt(stream, "bits_per_sample");
        if (sample is > 0)
        {
            return sample;
        }

        var raw = GetInt(stream, "bits_per_raw_sample");
        if (raw is > 0)
        {
            return raw;
        }

        var sampleFormat = GetString(stream, "sample_fmt");
        var match = sampleFormat is null ? Match.Empty : Regex.Match(sampleFormat, @"s(\d+)");
        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits)
                ? bits
                : null;
    }

    private static int? ParseId(string? id) =>
        id is not null && id.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(id.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool GetFlag(JsonElement element, string name) => GetInt(element, name) == 1;
}
