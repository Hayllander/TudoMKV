using TudoMKV.Core.Models;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

/// <summary>
/// As regras do ShawScope-Audio: idioma em três letras, nome da faixa "English DTS-HD MA 5.1" e o
/// resumo por faixa que o aplicativo escreve no log depois de gravar.
/// </summary>
public class TrackNamingTests
{
    private static MediaTrack Audio(
        int index,
        string codec,
        int? channels = null,
        string? profile = null,
        string? language = null,
        string? codecLongName = null) =>
        new()
        {
            Index = index,
            Type = TrackType.Audio,
            Codec = codec,
            Channels = channels,
            Profile = profile,
            Language = language ?? "und",
            CodecLongName = codecLongName,
        };

    [Theory]
    [InlineData("pt", "por")]
    [InlineData("POR", "por")]
    [InlineData("en", "eng")]
    [InlineData("zh", "chi")]
    [InlineData("por", "por")]
    [InlineData("", "und")]
    [InlineData(null, "und")]
    public void NormalizesTheLanguageToThreeLetters(string? input, string expected) =>
        Assert.Equal(expected, TrackNaming.Language(input));

    [Theory]
    [InlineData("por", "Portuguese")]
    [InlineData("pt", "Portuguese")]
    [InlineData("eng", "English")]
    [InlineData("en", "English")]
    [InlineData("jpn", "Japanese")]
    [InlineData("fre", "French")]
    [InlineData("deu", "German")]
    [InlineData("zho", "Chinese")]
    [InlineData("und", "Unknown")]
    [InlineData("", "Unknown")]
    public void ShowsTheLanguageNameInEnglish(string input, string expected) =>
        Assert.Equal(expected, TrackNaming.LanguageName(input));

    [Theory]
    [InlineData("ac3", null, "AC3")]
    [InlineData("eac3", null, "DDP")]
    [InlineData("truehd", null, "TrueHD")]
    [InlineData("flac", null, "FLAC")]
    [InlineData("aac", null, "AAC")]
    [InlineData("dts", null, "DTS")]
    [InlineData("dts", "DTS-HD MA", "DTS-HD MA")]
    [InlineData("dts", "DTS-HD HRA", "DTS-HD")]
    [InlineData("pcm_bluray", null, "PCM")]
    [InlineData("pcm_dvd", null, "PCM")]
    [InlineData("subrip", null, "SUBRIP")]
    public void ShortensTheCodecForTheTrackName(string codec, string? profile, string expected) =>
        Assert.Equal(expected, TrackNaming.CodecShort(codec, profile));

    [Theory]
    [InlineData("dts", "DTS-HD MA", 8, "DTS-HD Master Audio")]
    [InlineData("dts", null, 6, "DTS")]
    [InlineData("eac3", "Dolby Digital Plus + Atmos", 6, "Dolby Digital Plus + Atmos")]
    [InlineData("eac3", null, 6, "Dolby Digital Plus")]
    [InlineData("ac3", null, 6, "Dolby Digital")]
    [InlineData("ac3", "Dolby Digital EX", 6, "Dolby Digital EX")]
    [InlineData("truehd", "Dolby TrueHD + Atmos", 8, "Dolby TrueHD + Atmos")]
    [InlineData("truehd", null, 8, "Dolby TrueHD")]
    [InlineData("flac", null, 2, "FLAC")]
    [InlineData("pcm_bluray", null, 8, "PCM Multichannel")]
    [InlineData("pcm_dvd", null, 2, "PCM Stereo")]
    [InlineData("pcm_dvd", null, 1, "PCM Mono")]
    public void LabelsTheCodecForTheSummary(string codec, string? profile, int channels, string expected) =>
        Assert.Equal(expected, TrackNaming.CodecLabel(codec, profile, channels, null));

    [Fact]
    public void FallsBackToTheLongCodecNameInTheSummary()
    {
        Assert.Equal("Dolby AC-4", TrackNaming.CodecLabel("ac4", null, 6, "Dolby AC-4"));
        Assert.Equal("ac4", TrackNaming.CodecLabel("ac4", null, 6, null));
    }

    [Theory]
    [InlineData(1, "Mono")]
    [InlineData(2, "Stereo")]
    [InlineData(6, "5.1")]
    [InlineData(8, "7.1")]
    [InlineData(null, "N/A")]
    [InlineData(4, "4 ch")]
    public void DescribesTheLayoutInTheSummary(int? channels, string expected) =>
        Assert.Equal(expected, TrackNaming.AudioLayout(channels));

    [Theory]
    [InlineData(1, "1.0")]
    [InlineData(2, "2.0")]
    [InlineData(6, "5.1")]
    [InlineData(8, "7.1")]
    [InlineData(null, "N/A")]
    [InlineData(4, "4ch")]
    public void DescribesTheChannelsInTheTrackName(int? channels, string expected) =>
        Assert.Equal(expected, TrackNaming.ChannelPretty(channels));

    [Fact]
    public void NamesTheAudioTrackLikeTheScript()
    {
        Assert.Equal(
            "English DTS-HD MA 5.1",
            TrackNaming.AudioTitle(Audio(1, "dts", 6, "DTS-HD MA", "en")));
        Assert.Equal(
            "Portuguese AC3 2.0",
            TrackNaming.AudioTitle(Audio(1, "ac3", 2, null, "pt")));
    }

    [Fact]
    public void NamesTheGeneratedTrackByTheCodecItWillHave()
    {
        var pcm = Audio(1, "pcm_bluray", 1, null, "en");
        pcm.EncodeTo = AudioEncoding.Ac3;

        Assert.Equal("English AC3 1.0", TrackNaming.AudioTitle(pcm));
    }

    [Theory]
    [InlineData("por", "Portuguese (Subtitles)")]
    [InlineData("pt", "Portuguese (Subtitles)")]
    [InlineData("eng", "English (Subtitles)")]
    [InlineData("en", "English (Subtitles)")]
    [InlineData("spa", "Spanish (Subtitles)")]
    [InlineData("und", "Unknown (Subtitles)")]
    public void NamesTheSubtitleTrack(string language, string expected) =>
        Assert.Equal(expected, TrackNaming.SubtitleTitle(language));

    [Fact]
    public void WritesTheSummaryOfEachAudioTrack()
    {
        var source = new MediaSource
        {
            Path = "filme.mkv",
            Tracks =
            [
                new MediaTrack { Index = 0, Type = TrackType.Video, Codec = "h264" },
                new MediaTrack
                {
                    Index = 1,
                    Type = TrackType.Audio,
                    Codec = "dts",
                    Profile = "DTS-HD MA",
                    Language = "chi",
                    Channels = 6,
                    SampleRate = 48000,
                    BitRate = 2178000,
                    BitsPerSample = 16,
                    Title = "Chinese DTS-HD MA 5.1",
                },
            ],
        };

        Assert.Equal(
            ["Track 1: Chinese / DTS-HD Master Audio / 5.1 / 48.0 kHz / 2178 kb/s / 16-bit / \"Chinese DTS-HD MA 5.1\""],
            TrackNaming.Report(source));
    }

    [Fact]
    public void NumbersOnlyTheAudioTracksAndShowsN_A_WhenSomethingIsMissing()
    {
        var source = new MediaSource
        {
            Path = "filme.mkv",
            Tracks =
            [
                new MediaTrack { Index = 0, Type = TrackType.Video, Codec = "h264" },
                Audio(1, "ac3", 6, null, "eng"),
                new MediaTrack { Index = 2, Type = TrackType.Subtitle, Codec = "subrip", Language = "por" },
                Audio(3, "flac", 2, null, "por"),
            ],
        };

        Assert.Equal(
            [
                "Track 1: English / Dolby Digital / 5.1 / N/A / N/A / N/A",
                "Track 2: Portuguese / FLAC / Stereo / N/A / N/A / N/A",
            ],
            TrackNaming.Report(source));
    }
}
