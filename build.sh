#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
SKIP_CLEAN="${SKIP_CLEAN:-0}"

if [ "$SKIP_CLEAN" != "1" ]; then
  echo "==> Cleaning solution and sample Rust target..."
  dotnet clean "$ROOT/mslx-plugin-rustbridge.sln" -c "$CONFIGURATION"
  (cd "$ROOT/samples/RustBridgeDemo/rust" && cargo clean)
fi

echo "==> [1/2] Packing RustBridge library..."
dotnet pack "$ROOT/csharp/MSLX.Plugin.RustBridge.csproj" -c "$CONFIGURATION" "$@"
echo "    Done: csharp/bin/$CONFIGURATION/MSLX.Plugin.RustBridge.*.nupkg"

echo "==> [2/2] Building RustBridge demo plugin..."
dotnet build "$ROOT/samples/RustBridgeDemo/MSLX.Plugin.RustBridge.Demo.csproj" -c "$CONFIGURATION" "$@"
echo "    Done: samples/RustBridgeDemo/bin/$CONFIGURATION/net10.0/MSLX.Plugin.RustBridge.Demo.dll"

echo ""
echo "Build complete."
echo "   Library package: $ROOT/csharp/bin/$CONFIGURATION/"
echo "   Demo deploy dir: $ROOT/samples/RustBridgeDemo/bin/$CONFIGURATION/net10.0/"
