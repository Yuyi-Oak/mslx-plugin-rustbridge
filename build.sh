#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"

echo "==> [1/2] Building Rust cdylib..."
cd "$ROOT/rust"
cargo clean && cargo build --release
echo "    Done: target/release/libmslx_plugin_rust.so"

echo "==> [2/2] Building C# plugin..."
cd "$ROOT/csharp"
dotnet clean && dotnet build -c Release
echo "    Done: bin/Release/net10.0/MSLX.Plugin.RustBridge.dll"

echo ""
echo "✅ Build complete."
echo "   Deploy contents of: $ROOT/csharp/bin/Release/net10.0/"
echo "   (Contains both the C# dll and the Rust .so)"
