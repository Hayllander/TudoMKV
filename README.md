# TudoMKV

Aplicativo em C# com interface gráfica (Avalonia) para gravar vídeos em MKV, no estilo do MakeMKV.

**Estado atual: etapa 1, remux de arquivos.** Arraste arquivos de vídeo (MP4, M2TS, TS, VOB, AVI, MKV…), escolha as faixas, ajuste idioma, nome e as marcas de padrão/forçada, e grave em MKV sem recodificar. Leitura de DVD e Blu-ray vem nas próximas etapas.

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- `ffmpeg` e `ffprobe` (versão 7 ou mais nova). O TudoMKV procura nesta ordem:
  1. a pasta indicada na variável de ambiente `TUDOMKV_FFMPEG_DIR`;
  2. `runtimes/ffmpeg/` ao lado do executável;
  3. a pasta do executável;
  4. o `PATH`.

No Windows, uma build pronta do FFmpeg pode ser baixada em <https://www.gyan.dev/ffmpeg/builds/> ou <https://github.com/BtbN/FFmpeg-Builds/releases>.

## Como rodar

```sh
dotnet run --project src/TudoMKV.App
# ou já abrindo arquivos:
dotnet run --project src/TudoMKV.App -- "C:\Videos\filme.mp4"
```

## Testes

```sh
dotnet test
```

Os testes de integração geram um MP4 de amostra com o ffmpeg e o remuxam; eles são pulados se o ffmpeg não estiver disponível.

## Estrutura

| Projeto | Papel |
|---|---|
| `src/TudoMKV.Core` | Modelos (`MediaSource`, `MediaTrack`, `MuxJob`) e contratos (`IMediaInspector`, `IMuxer`), sem dependências nativas. |
| `src/TudoMKV.Muxing` | `FfprobeInspector` (lê faixas via ffprobe) e `FfmpegMuxer` (grava MKV via ffmpeg com `-c copy`). |
| `src/TudoMKV.App` | Interface Avalonia em MVVM (CommunityToolkit.Mvvm): lista de arquivos, faixas editáveis, fila com progresso e log. |
| `tests/TudoMKV.Tests` | Testes xUnit v3. |

## Plano

1. **Remux de arquivos** (feito).
2. DVD: leitura com libdvdread/libdvdnav, gravação pelo demuxer `dvdvideo` do FFmpeg.
3. Blu-ray: leitura com libbluray, gravação pelo protocolo `bluray:` do FFmpeg.
4. Metadados: modelos de nome de arquivo, perfis de idioma, mkvpropedit opcional.
5. Detecção de bibliotecas opcionais instaladas pelo usuário.
6. Instalador e builds para Linux e macOS.

## Aviso legal

O TudoMKV não contém nem distribui código para contornar proteções contra cópia (CSS, AACS, BD+). Ele funciona com discos sem proteção e com arquivos que você já tem. Contornar proteções técnicas pode ser ilegal no seu país (no Brasil, Lei 9.610/98, art. 107).

## Licença

GPLv3. Veja [LICENSE](LICENSE).
