#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REFS="${REFS:-$ROOT/refs}"
CSC="${CSC:-$(ls -d /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | tail -n 1)}"
mkdir -p "$ROOT/build"
ARGS=()
for f in "$REFS"/*.dll; do
  case "$(basename "$f")" in Launch_android.dll|Survivalcraft.dll) continue;; esac
  ARGS+=("-r:$f")
done
cd "$ROOT/Survivalcraft.Windows"
find . -name "*.cs" -not -path "./obj/*" > "$ROOT/build/sources.txt"
dotnet "$CSC" -nostdlib -noconfig -nowarn:CS8981,CS1591,CS0168,CS0219,CS0649,CS0169,CS0414 -langversion:preview -unsafe -nullable:disable -define:ANDROID -define:TRACE -target:library -out:"$ROOT/build/Survivalcraft.check.dll" "${ARGS[@]}" @"$ROOT/build/sources.txt" || true
