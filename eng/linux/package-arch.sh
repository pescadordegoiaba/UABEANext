#!/usr/bin/env bash
set -euo pipefail

TARBALL=""
OUTPUT_DIR=""

usage() {
  cat <<'EOF'
Usage: eng/linux/package-arch.sh [options]

Creates a pacman-installable .pkg.tar.zst from the linux-x64 tarball.

Options:
  --tarball <file>   Existing uabea-next-linux-x64.tar.gz path.
                     Default: artifacts/linux/uabea-next-linux-x64.tar.gz.
  --output <dir>     Package build/output directory. Default: artifacts/pkgbuild.
  -h, --help         Show this help.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tarball)
      TARBALL="${2:?missing value for --tarball}"
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

if ! command -v makepkg >/dev/null 2>&1; then
  echo "makepkg is required to create the pacman package." >&2
  exit 1
fi

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
TARBALL="${TARBALL:-"$ROOT_DIR/artifacts/linux/uabea-next-linux-x64.tar.gz"}"
OUTPUT_DIR="${OUTPUT_DIR:-"$ROOT_DIR/artifacts/pkgbuild"}"

if [[ ! -f "$TARBALL" ]]; then
  echo "Missing tarball: $TARBALL" >&2
  echo "Build it first with: eng/linux/package-linux.sh --rid linux-x64" >&2
  exit 1
fi

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"
cp "$ROOT_DIR/packaging/arch/PKGBUILD" "$OUTPUT_DIR/"
cp "$TARBALL" "$OUTPUT_DIR/uabea-next-linux-x64.tar.gz"

(
  cd "$OUTPUT_DIR"
  makepkg -f --noconfirm
)

pkg="$(find "$OUTPUT_DIR" -maxdepth 1 -type f -name '*.pkg.tar.zst' | head -n 1)"
echo "Created $pkg"
