#!/usr/bin/env bash
# Gera dist/TudoMKV-<versão>-win-x64.zip: TudoMKV.exe autocontido + ffmpeg/ffprobe em runtimes/ffmpeg.
# Uso: build/package-windows.sh [versão] [pasta-bin-do-ffmpeg-windows]
# Sem a pasta do ffmpeg, baixa a build GPL "shared" mais recente da BtbN (inclui libdvdnav e libbluray).
set -euo pipefail

VERSION="${1:-0.0.0-dev}"
FFMPEG_BIN="${2:-}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DIST="$ROOT/dist"
STAGE="$DIST/TudoMKV"
FFMPEG_URL="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip"

rm -rf "$STAGE"
mkdir -p "$STAGE/runtimes/ffmpeg"

dotnet publish "$ROOT/src/TudoMKV.App/TudoMKV.App.csproj" \
  -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none \
  -p:Version="${VERSION%%-*}" \
  -p:InformationalVersion="$VERSION" \
  -o "$STAGE"

# Símbolos de depuração das bibliotecas nativas (Skia, HarfBuzz) não servem ao usuário.
find "$STAGE" -maxdepth 1 -name '*.pdb' -delete

if [[ -z "$FFMPEG_BIN" ]]; then
  DOWNLOAD="$DIST/ffmpeg-download"
  mkdir -p "$DOWNLOAD"
  curl -fsSL -o "$DOWNLOAD/ffmpeg.zip" "$FFMPEG_URL"
  unzip -q -o "$DOWNLOAD/ffmpeg.zip" -d "$DOWNLOAD"
  FFMPEG_BIN="$(dirname "$(find "$DOWNLOAD" -name ffmpeg.exe | head -1)")"
fi

if [[ -f "$FFMPEG_BIN/../LICENSE.txt" ]]; then
  cp "$FFMPEG_BIN/../LICENSE.txt" "$STAGE/runtimes/ffmpeg/LICENSE-ffmpeg.txt"
fi

cp "$FFMPEG_BIN"/ffmpeg.exe "$FFMPEG_BIN"/ffprobe.exe "$FFMPEG_BIN"/*.dll "$STAGE/runtimes/ffmpeg/"
cp "$ROOT/LICENSE" "$STAGE/LICENSE.txt"
cp "$ROOT/build/LEIA-ME.txt" "$STAGE/LEIA-ME.txt"

ZIP="$DIST/TudoMKV-$VERSION-win-x64.zip"
rm -f "$ZIP"
(cd "$DIST" && zip -qr -9 "$ZIP" TudoMKV)
echo "$ZIP"
