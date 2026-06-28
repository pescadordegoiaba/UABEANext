#!/usr/bin/env bash
set -euo pipefail

RID="linux-x64"
CONFIGURATION="Release"
OUTPUT_DIR=""

usage() {
  cat <<'EOF'
Usage: eng/linux/package-linux.sh [options]

Builds a self-contained Linux package. The generated app does not require
the dotnet runtime to be installed on the target system.

Options:
  --rid <rid>             Runtime identifier: linux-x64 or linux-musl-x64.
                          linux-x64 is the primary Manjaro/Arch build.
  --configuration <cfg>   Build configuration. Default: Release.
  --output <dir>          Artifact output directory. Default: artifacts/linux.
  -h, --help              Show this help.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid)
      RID="${2:?missing value for --rid}"
      shift 2
      ;;
    --configuration)
      CONFIGURATION="${2:?missing value for --configuration}"
      shift 2
      ;;
    --output)
      OUTPUT_DIR="${2:?missing value for --output}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

case "$RID" in
  linux-x64|linux-musl-x64) ;;
  *)
    echo "Unsupported RID '$RID'. Use linux-x64 or linux-musl-x64." >&2
    exit 2
    ;;
esac

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK is required to build packages, but not to run the generated package." >&2
  exit 1
fi

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
OUTPUT_DIR="${OUTPUT_DIR:-"$ROOT_DIR/artifacts/linux"}"
WORK_DIR="$ROOT_DIR/artifacts/obj/linux-$RID"
PACKAGE_NAME="uabea-next-$RID"
PACKAGE_DIR="$WORK_DIR/$PACKAGE_NAME"
PUBLISH_DIR="$PACKAGE_DIR/app"
PLUGIN_BUILD_DIR="$WORK_DIR/plugins"

rm -rf "$WORK_DIR"
mkdir -p "$PUBLISH_DIR" "$PLUGIN_BUILD_DIR" "$OUTPUT_DIR"

echo "Publishing UABEANext for $RID..."
dotnet restore "$ROOT_DIR/UABEANext4.sln" -r "$RID"
dotnet publish "$ROOT_DIR/UABEANext4.Desktop/UABEANext4.Desktop.csproj" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  --no-restore \
  -p:UseAppHost=true \
  -p:PublishSingleFile=false \
  -p:PublishReadyToRun=false \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$PUBLISH_DIR"

if [[ -d "$ROOT_DIR/ReleaseFiles" ]]; then
  echo "Copying release files..."
  cp -a "$ROOT_DIR/ReleaseFiles/." "$PUBLISH_DIR/"
fi

if [[ ! -f "$PUBLISH_DIR/classdata.tpk" ]]; then
  echo "Required release file classdata.tpk is missing from the published app." >&2
  exit 1
fi

mkdir -p "$PUBLISH_DIR/plugins"

PLUGIN_PROJECTS=(
  "AudioPlugin/AudioPlugin.csproj"
  "FontPlugin/FontPlugin.csproj"
  "MaterialPlugin/MaterialPlugin.csproj"
  "MeshPlugin/MeshPlugin.csproj"
  "TextAssetPlugin/TextAssetPlugin.csproj"
  "TexturePlugin/TexturePlugin.csproj"
  "UnityComponentPlugin/UnityComponentPlugin.csproj"
)

copy_plugin_outputs() {
  local plugin_out="$1"
  shift

  local file
  for file in "$@"; do
    if [[ -f "$plugin_out/$file" ]]; then
      cp -f "$plugin_out/$file" "$PUBLISH_DIR/plugins/"
    fi
  done
}

for project in "${PLUGIN_PROJECTS[@]}"; do
  plugin_name="$(basename "$(dirname "$project")")"
  plugin_out="$PLUGIN_BUILD_DIR/$plugin_name"
  echo "Publishing plugin $plugin_name..."
  dotnet publish "$ROOT_DIR/$project" \
    -c "$CONFIGURATION" \
    -r "$RID" \
    --self-contained false \
    --no-restore \
    -p:UABEACopyPluginToDesktop=false \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o "$plugin_out"

  case "$plugin_name" in
    AudioPlugin)
      copy_plugin_outputs "$plugin_out" AudioPlugin.dll Fmod5Sharp.dll NAudio.Core.dll OggVorbisEncoder.dll
      ;;
    FontPlugin)
      copy_plugin_outputs "$plugin_out" FontPlugin.dll
      ;;
    MaterialPlugin)
      copy_plugin_outputs "$plugin_out" MaterialPlugin.dll
      ;;
    MeshPlugin)
      copy_plugin_outputs "$plugin_out" MeshPlugin.dll
      ;;
    TextAssetPlugin)
      copy_plugin_outputs "$plugin_out" TextAssetPlugin.dll
      ;;
    TexturePlugin)
      copy_plugin_outputs "$plugin_out" TexturePlugin.dll
      ;;
    UnityComponentPlugin)
      copy_plugin_outputs "$plugin_out" UnityComponentPlugin.dll
      ;;
  esac
done

if [[ "$RID" == "linux-x64" ]]; then
  mkdir -p "$PUBLISH_DIR/runtimes/linux-x64/native"
  cp -f "$ROOT_DIR"/NativeLibs/linux-x64/* "$PUBLISH_DIR/runtimes/linux-x64/native/"
  chmod +x "$PUBLISH_DIR"/runtimes/linux-x64/native/*.so* || true
else
  cat > "$PUBLISH_DIR/README-linux-musl.txt" <<'EOF'
This is the linux-musl-x64 fallback package. It is intended for systems where
glibc compatibility is a problem.

The bundled glibc-only texture encoder native libraries are intentionally not
included in this package. Texture preview/export remains available, but texture
import/re-encode paths that require textureencoder/cuttlefish/PVRTexLib may be
unavailable in this fallback build.
EOF
fi

cat > "$PACKAGE_DIR/uabea-next" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
APP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/app" && pwd)"
exec "$APP_DIR/UABEANext4.Desktop" "$@"
EOF
chmod +x "$PACKAGE_DIR/uabea-next"
chmod +x "$PUBLISH_DIR/UABEANext4.Desktop" || true

mkdir -p "$PACKAGE_DIR/share/applications"
cp "$ROOT_DIR/packaging/linux/uabea-next.desktop" "$PACKAGE_DIR/share/applications/uabea-next.desktop"

cat > "$PACKAGE_DIR/README-linux.txt" <<EOF
UABEANext Linux package ($RID)

Run:
  ./uabea-next

This package is self-contained. It does not require dotnet or the dotnet runtime
to be installed on the target system.

Recommended Manjaro/Arch runtime packages:
  sudo pacman -S --needed fontconfig freetype2 libx11 libxext libxrandr libxrender libxcursor libxi libxinerama libglvnd mesa gtk3 xdg-desktop-portal

Recommended Debian/Ubuntu runtime packages:
  sudo apt install libfontconfig1 libfreetype6 libx11-6 libxext6 libxrandr2 libxrender1 libxcursor1 libxi6 libxinerama1 libgl1 libgtk-3-0 xdg-desktop-portal

Use linux-x64 first on Manjaro/Arch. Use linux-musl-x64 only as a fallback when
glibc compatibility blocks startup on an older distribution.
EOF

if command -v ldd >/dev/null 2>&1 && [[ "$RID" == "linux-x64" ]]; then
  {
    echo "ldd check for UABEANext4.Desktop"
    ldd "$PUBLISH_DIR/UABEANext4.Desktop" || true
    echo
    echo "ldd check for libtextureencoder.so"
    ldd "$PUBLISH_DIR/runtimes/linux-x64/native/libtextureencoder.so" || true
  } > "$PACKAGE_DIR/ldd-report.txt"
fi

TARBALL="$OUTPUT_DIR/$PACKAGE_NAME.tar.gz"
tar -C "$WORK_DIR" -czf "$TARBALL" "$PACKAGE_NAME"
echo "Created $TARBALL"
