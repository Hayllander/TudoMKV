# TudoMKV

Aplicativo em C# com interface gráfica (Avalonia) para gravar vídeos em MKV, no estilo do MakeMKV.

Funciona como o MakeMKV: abra a origem, marque os títulos e faixas na árvore e clique em **Make MKV**.

- **DVD:** abra o `VIDEO_TS.IFO` (ou a pasta VIDEO_TS, um drive ou uma ISO).
- **Blu-ray:** abra o `index.bdmv` ou o `MovieObject.bdmv` dentro da pasta BDMV (ou a própria pasta, ou um drive).
- **Arquivos:** MP4, M2TS, TS, VOB, AVI, MKV… são remuxados para MKV.

Títulos com menos de 2 minutos são pulados e o log diz por quê. O MKV sai sem recodificar, com capítulos, idiomas e legendas (VobSub no DVD, PGS no Blu-ray). Discos com proteção contra cópia (CSS, AACS) não são suportados.

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

Os testes de integração geram um MP4 de amostra, um DVD de teste (com `dvdauthor`, `spumux` e `genisoimage`) e um Blu-ray de teste (com `tsMuxeR`) e gravam MKVs a partir deles. São pulados quando falta alguma dessas ferramentas.

## Gerar o executável para Windows

```sh
build/package-windows.sh 0.4.0
```

Gera `dist/TudoMKV-0.4.0-win-x64.zip` com o app autocontido e o FFmpeg da BtbN. Roda em Linux ou no Git Bash do Windows. Ao enviar uma tag `v*`, o workflow `Release` faz isso e publica a release.

## Estrutura

| Projeto | Papel |
|---|---|
| `src/TudoMKV.Core` | Modelos (`MediaSource`, `MediaTrack`, `MuxJob`) e contratos (`IMediaInspector`, `IMuxer`), sem dependências nativas. |
| `src/TudoMKV.Disc` | Leitura de discos em C# puro: IFO e ISO 9660 do DVD; MPLS e CLPI do Blu-ray (títulos, capítulos, idiomas, tamanho). |
| `src/TudoMKV.Muxing` | `SourceOpener` decide o que foi aberto; `DvdReader` (demuxer `dvdvideo`), `BlurayReader` (concatenação dos .m2ts da playlist + capítulos), `FfprobeInspector` e `FfmpegMuxer` (grava MKV com `-c copy`). |
| `src/TudoMKV.App` | Interface Avalonia em MVVM no estilo MakeMKV: árvore Disco > Título > Faixas, pasta de saída, Make MKV, informações e log. |
| `tests/TudoMKV.Tests` | Testes xUnit v3. |

## Plano

1. **Remux de arquivos** (feito).
2. **DVD** (feito): IFO lidos em C#, gravação pelo demuxer `dvdvideo` do FFmpeg.
3. **Blu-ray** (feito): playlists lidas em C#, gravação pela concatenação dos .m2ts (o protocolo `bluray:` do FFmpeg recusa playlists com menos de 3 minutos).
4. Metadados: modelos de nome de arquivo, perfis de idioma, mkvpropedit opcional.
5. Detecção de bibliotecas opcionais instaladas pelo usuário.
6. Instalador e builds para Linux e macOS.

## Aviso legal

O TudoMKV não contém nem distribui código para contornar proteções contra cópia (CSS, AACS, BD+). Ele funciona com discos sem proteção e com arquivos que você já tem. Contornar proteções técnicas pode ser ilegal no seu país (no Brasil, Lei 9.610/98, art. 107).

## Licença

GPLv3. Veja [LICENSE](LICENSE).
