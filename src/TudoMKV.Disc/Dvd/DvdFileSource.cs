using System.Buffers.Binary;
using System.Text;

namespace TudoMKV.Disc.Dvd;

/// <summary>Acesso aos arquivos da pasta VIDEO_TS, venha ela de uma pasta, de um drive ou de uma ISO.</summary>
public interface IDvdFileSource : IDisposable
{
    string? VolumeLabel { get; }

    /// <summary>Lê um arquivo de VIDEO_TS (ex.: "VTS_01_0.IFO"), ou null se não existir.</summary>
    byte[]? ReadVideoTsFile(string name);
}

/// <summary>VIDEO_TS numa pasta comum ou na raiz de um drive.</summary>
public sealed class FolderDvdSource : IDvdFileSource
{
    private readonly string _videoTs;

    public FolderDvdSource(string videoTsDirectory, string? volumeLabel)
    {
        _videoTs = videoTsDirectory;
        VolumeLabel = volumeLabel;
    }

    public string? VolumeLabel { get; }

    public byte[]? ReadVideoTsFile(string name)
    {
        // Discos e cópias podem ter nomes em minúsculas; no Linux isso importa.
        var match = Directory.EnumerateFiles(_videoTs)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : File.ReadAllBytes(match);
    }

    public void Dispose()
    {
    }
}

/// <summary>Leitor mínimo de ISO 9660, suficiente para achar VIDEO_TS numa imagem de DVD (ponte UDF/ISO).</summary>
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
            var root = ReadDirectory(ParseRecord(pvd, 156).Sector, ParseRecord(pvd, 156).Size);
            if (!root.TryGetValue("VIDEO_TS", out var videoTs) || !videoTs.IsDirectory)
            {
                throw new InvalidDataException("A imagem não contém a pasta VIDEO_TS.");
            }

            _videoTsFiles = ReadDirectory(videoTs.Sector, videoTs.Size)
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
                // Registros não atravessam setores; pula para o próximo.
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
