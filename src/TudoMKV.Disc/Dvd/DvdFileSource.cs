using System.Buffers.Binary;
using System.Text;

namespace TudoMKV.Disc.Dvd;

/// <summary>Acesso aos arquivos da pasta VIDEO_TS, venha ela de uma pasta, de um drive ou de uma ISO.</summary>
public interface IDvdFileSource : IDisposable
{
    string? VolumeLabel { get; }

    /// <summary>Caminho que o ffmpeg deve abrir (a ISO, a pasta que contém VIDEO_TS ou a junção para uma ISO montada).</summary>
    string InputPath { get; }

    /// <summary>Lê um arquivo de VIDEO_TS (ex.: "VTS_01_0.IFO"), ou null se não existir.</summary>
    byte[]? ReadVideoTsFile(string name);
}

/// <summary>VIDEO_TS numa pasta comum ou na raiz de um drive.</summary>
public sealed class FolderDvdSource : IDvdFileSource
{
    private readonly string _videoTs;

    public FolderDvdSource(string videoTsDirectory, string rootPath, string? volumeLabel)
    {
        _videoTs = videoTsDirectory;
        InputPath = rootPath;
        VolumeLabel = volumeLabel;
    }

    public string? VolumeLabel { get; }

    public string InputPath { get; }

    public byte[]? ReadVideoTsFile(string name)
    {
        var match = Directory.EnumerateFiles(_videoTs)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : File.ReadAllBytes(match);
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Leitor mínimo de ISO 9660, suficiente para achar VIDEO_TS numa imagem de DVD (ponte UDF/ISO).
/// Também aceita imagens com VIDEO_TS.IFO, VTS_XX_Y.IFO etc. direto na raiz (sem a pasta VIDEO_TS).
/// </summary>
public sealed class IsoDvdSource : IDvdFileSource
{
    private const int SectorSize = 2048;
    private const int MaxIfoSize = 16 * 1024 * 1024;

    private readonly FileStream _stream;
    private readonly Dictionary<string, (long Sector, long Size)> _videoTsFiles;

    public IsoDvdSource(string isoPath)
    {
        _stream = File.OpenRead(isoPath);
        try
        {
            var pvd = ReadSectors(16, 1);
            if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001")
            {
                throw new InvalidDataException("A imagem não tem um sistema de arquivos ISO 9660 legível.");
            }

            VolumeLabel = Encoding.ASCII.GetString(pvd, 40, 32).Trim();
            var rootRecord = ParseRecord(pvd, 156);
            var root = ReadDirectory(rootRecord.Sector, rootRecord.Size);

            Dictionary<string, Record> files;
            if (root.TryGetValue("VIDEO_TS", out var videoTs) && videoTs.IsDirectory)
            {
                files = ReadDirectory(videoTs.Sector, videoTs.Size);
                InputPath = isoPath;
            }
            else if (root.TryGetValue("VIDEO_TS.IFO", out var ifo) && !ifo.IsDirectory)
            {
                files = root;
                InputPath = RootFilesInputPath(isoPath);
            }
            else
            {
                throw new InvalidDataException("A imagem não contém a pasta VIDEO_TS.");
            }

            _videoTsFiles = files
                .Where(e => !e.Value.IsDirectory)
                .ToDictionary(e => e.Key, e => (e.Value.Sector, e.Value.Size), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    public string? VolumeLabel { get; }

    public string InputPath { get; }

    public byte[]? ReadVideoTsFile(string name)
    {
        if (!_videoTsFiles.TryGetValue(name, out var file) || file.Size > MaxIfoSize)
        {
            return null;
        }

        var data = new byte[file.Size];
        _stream.Position = file.Sector * SectorSize;
        _stream.ReadExactly(data);
        return data;
    }

    public void Dispose() => _stream.Dispose();

    /// <summary>
    /// O demuxer dvdvideo do ffmpeg (libdvdread) só acha os arquivos numa ISO dentro de /VIDEO_TS (via UDF),
    /// mas numa pasta comum também os procura na raiz. No Windows a ISO é montada e o ffmpeg abre essa pasta.
    /// </summary>
    private static string RootFilesInputPath(string isoPath)
    {
        const string hint = "Extraia o conteúdo da ISO para uma pasta VIDEO_TS e abra essa pasta.";
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidDataException($"Esta ISO tem os arquivos do DVD na raiz, sem a pasta VIDEO_TS, e o ffmpeg não a abre. {hint}");
        }

        try
        {
            return IsoMount.Open(isoPath);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException($"Esta ISO tem os arquivos do DVD na raiz, sem a pasta VIDEO_TS, e {ex.Message}. {hint}", ex);
        }
    }

    private Dictionary<string, Record> ReadDirectory(long sector, long size)
    {
        var data = ReadSectors(sector, (int)((size + SectorSize - 1) / SectorSize));
        var entries = new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        while (offset < data.Length)
        {
            var length = data[offset];
            if (length == 0)
            {
                offset = (offset / SectorSize + 1) * SectorSize;
                continue;
            }

            var record = ParseRecord(data, offset);
            if (record.Name is not ("\0" or "\u0001"))
            {
                entries[record.Name] = record;
            }

            offset += length;
        }

        return entries;
    }

    private static Record ParseRecord(byte[] data, int offset)
    {
        var sector = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 2));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 10));
        var isDirectory = (data[offset + 25] & 0x02) != 0;
        var name = Encoding.ASCII.GetString(data, offset + 33, data[offset + 32]);
        var version = name.IndexOf(';');
        if (version >= 0)
        {
            name = name[..version];
        }

        return new Record(name.TrimEnd('.'), sector, size, isDirectory);
    }

    private byte[] ReadSectors(long sector, int count)
    {
        var buffer = new byte[count * SectorSize];
        _stream.Position = sector * SectorSize;
        _stream.ReadExactly(buffer);
        return buffer;
    }

    private readonly record struct Record(string Name, long Sector, long Size, bool IsDirectory);
}
