# TudoMKV

Aplicativo em C# com interface gráfica (Avalonia) para gravar vídeos em MKV, no estilo do MakeMKV.

**Estado atual: etapa 2, arquivos e DVD.**

- **Arquivos:** arraste vídeos (MP4, M2TS, TS, VOB, AVI, MKV…), escolha as faixas, ajuste idioma, nome e as marcas de padrão/forçada, e grave em MKV sem recodificar.
- **DVD:** em "Abrir DVD", escolha um drive, uma pasta VIDEO_TS ou uma imagem ISO (também dá para arrastar). Cada título vira um item da lista com suas faixas de vídeo, áudio e legenda; títulos com menos de 2 minutos ficam ocultos. O MKV sai com capítulos, idiomas e legendas DVD (VobSub) com a paleta original.

Blu-ray vem na próxima etapa.

## Download (Windows)

Baixe o `TudoMKV-<versão>-win-x64.zip` em [Releases](../../releases), extraia e abra `TudoMKV.exe`. O .NET e o FFmpeg já vêm dentro.

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- `ffmpeg` e `ffprobe` 7 ou mais novos, compilados com libdvdnav/libdvdread para ler DVD (as builds da [BtbN](https://github.com/BtbN/FFmpeg-Builds/releases) servem). O TudoMKV procura nesta ordem:
  1. a pasta indicada na variável de ambiente `TUDOMKV_FFMPEG_DIR`;
  2. `runtimes/ffmpeg/` ao lado do executável;
  3. a pasta do executável;
  4. o `PATH`.

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

Os testes de integração geram um MP4 de amostra e um DVD de teste (com `dvdauthor`, `spumux` e `genisoimage`) e gravam MKVs a partir deles. São pulados quando falta alguma dessas ferramentas.

## Gerar o executável para Windows

```sh
build/package-windows.sh 0.2.0
```

Gera `dist/TudoMKV-0.2.0-win-x64.zip` com o app autocontido e o FFmpeg da BtbN. Roda em Linux ou no Git Bash do Windows. Ao enviar uma tag `v*`, o workflow `Release` faz isso e publica a release.

## Estrutura

| Projeto | Papel |
|---|---|
| `src/TudoMKV.Core` | Modelos (`MediaSource`, `MediaTrack`, `MuxJob`) e contratos (`IMediaInspector`, `IMuxer`), sem dependências nativas. |
| `src/TudoMKV.Disc` | Leitura de DVD em C# puro: arquivos IFO (títulos, capítulos, duração) e ISO 9660. |
| `src/TudoMKV.Muxing` | `FfprobeInspector` (lê faixas via ffprobe), `DvdReader` (títulos de DVD pelo demuxer `dvdvideo`) e `FfmpegMuxer` (grava MKV com `-c copy`). |
| `src/TudoMKV.App` | Interface Avalonia em MVVM (CommunityToolkit.Mvvm): lista de arquivos, faixas editáveis, fila com progresso e log. |
| `tests/TudoMKV.Tests` | Testes xUnit v3. |

## Plano

1. **Remux de arquivos** (feito).
2. **DVD** (feito): IFO lidos em C#, gravação pelo demuxer `dvdvideo` do FFmpeg.
3. Blu-ray: leitura com libbluray, gravação pelo protocolo `bluray:` do FFmpeg.
4. Metadados: modelos de nome de arquivo, perfis de idioma, mkvpropedit opcional.
5. Detecção de bibliotecas opcionais instaladas pelo usuário.
6. Instalador e builds para Linux e macOS.

## Aviso legal

O TudoMKV não contém nem distribui código para contornar proteções contra cópia (CSS, AACS, BD+). Ele funciona com discos sem proteção e com arquivos que você já tem. Contornar proteções técnicas pode ser ilegal no seu país (no Brasil, Lei 9.610/98, art. 107).

## Licença

GPLv3. Veja [LICENSE](LICENSE).
