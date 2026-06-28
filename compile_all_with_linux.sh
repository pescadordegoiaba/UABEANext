#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PYTHON_BIN="${PYTHON_BIN:-python3}"
CONFIGURATION="Release"
LINUX_RID="linux-x64"
WINDOWS_RID="win-x64"
SKIP_INSTALL=0
CHECK_ONLY=0
NO_ARCH_PACKAGE=0
NO_ZIP=0

usage() {
  cat <<'EOF'
Usage: ./compile_all_with_linux.sh [options]

Installs requirements on Arch/Manjaro and builds both Linux and Windows x64
packages from Linux. Windows build variables are isolated from Linux variables.

Options:
  --configuration <cfg>  Build configuration. Default: Release.
  --linux-rid <rid>      Linux RID: linux-x64 or linux-musl-x64. Default: linux-x64.
  --windows-rid <rid>    Windows RID. Default: win-x64.
  --skip-install         Do not install packages or download missing tools.
  --check-only           Validate requirements without compiling.
  --no-arch-package      Do not generate the Arch .pkg.tar.zst package.
  --no-zip               Do not generate the Windows .zip package.
  -h, --help             Show this help.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)
      CONFIGURATION="${2:?missing value for --configuration}"
      shift 2
      ;;
    --linux-rid)
      LINUX_RID="${2:?missing value for --linux-rid}"
      shift 2
      ;;
    --windows-rid)
      WINDOWS_RID="${2:?missing value for --windows-rid}"
      shift 2
      ;;
    --skip-install)
      SKIP_INSTALL=1
      shift
      ;;
    --check-only)
      CHECK_ONLY=1
      shift
      ;;
    --no-arch-package)
      NO_ARCH_PACKAGE=1
      shift
      ;;
    --no-zip)
      NO_ZIP=1
      shift
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

if ! command -v "$PYTHON_BIN" >/dev/null 2>&1; then
  if command -v pacman >/dev/null 2>&1 && [[ "$SKIP_INSTALL" -eq 0 ]]; then
    if [[ "$(id -u)" -eq 0 ]]; then
      pacman -S --needed --noconfirm python
    else
      sudo pacman -S --needed --noconfirm python
    fi
  else
    echo "python3 not found. Install python or set PYTHON_BIN." >&2
    exit 1
  fi
fi

linux_args=(
  "--rid" "$LINUX_RID"
  "--configuration" "$CONFIGURATION"
)
windows_args=(
  "--rid" "$WINDOWS_RID"
  "--configuration" "$CONFIGURATION"
)

if [[ "$SKIP_INSTALL" -eq 1 ]]; then
  linux_args+=("--skip-install")
  windows_args+=("--skip-install")
fi
if [[ "$CHECK_ONLY" -eq 1 ]]; then
  linux_args+=("--check-only")
  windows_args+=("--check-only")
fi
if [[ "$NO_ARCH_PACKAGE" -eq 1 ]]; then
  linux_args+=("--no-arch-package")
fi
if [[ "$NO_ZIP" -eq 1 ]]; then
  windows_args+=("--no-zip")
fi

cd "$ROOT_DIR"

"$PYTHON_BIN" "$ROOT_DIR/compile_linux_arch.py" "${linux_args[@]}"

env \
  -u PKG_CONFIG_PATH \
  -u PKG_CONFIG_LIBDIR \
  -u PKG_CONFIG_SYSROOT_DIR \
  -u LD_LIBRARY_PATH \
  -u CMAKE_PREFIX_PATH \
  -u QT_PLUGIN_PATH \
  -u QML2_IMPORT_PATH \
  "$PYTHON_BIN" "$ROOT_DIR/compile_windows.py" "${windows_args[@]}"

echo "All builds finished."
