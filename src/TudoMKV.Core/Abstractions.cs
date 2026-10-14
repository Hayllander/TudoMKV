using TudoMKV.Core.Models;

namespace TudoMKV.Core;

/// <summary>Lê um arquivo e descreve suas faixas.</summary>
public interface IMediaInspector
{
    Task<MediaSource> InspectAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Grava um <see cref="MuxJob"/> como MKV, informando o progresso de 0 a 1.</summary>
public interface IMuxer
{
    Task MuxAsync(MuxJob job, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class MuxException(string message, string? log = null) : Exception(message)
{
    /// <summary>Saída de erro da ferramenta externa, para mostrar no log.</summary>
    public string? Log { get; } = log;
}
