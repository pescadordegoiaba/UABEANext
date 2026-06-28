#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="${1:-$ROOT/global-metadata-decrypted.dat}"
exec python3 "$ROOT/eng/il2cpp/frida-dump-blockstrike-metadata.py" \
  --output "$OUT" \
  --package com.rexetstudio.blockstrike \
  --size 8040192