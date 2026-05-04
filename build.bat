@echo off
setlocal

set ROOT=%~dp0

echo =^> [1/2] Building Rust cdylib...
cd /d "%ROOT%rust"
cargo clean
cargo build --release
if errorlevel 1 goto :err
echo     Done: target\release\mslx_plugin_rust.dll

echo =^> [2/2] Building C# plugin...
cd /d "%ROOT%csharp"
dotnet clean
dotnet build -c Release
if errorlevel 1 goto :err
echo     Done: bin\Release\net10.0\MSLX.Plugin.RustBridge.dll

echo.
echo [OK] Build complete.
echo      Deploy contents of: %ROOT%csharp\bin\Release\net10.0\
goto :eof

:err
echo [ERROR] Build failed.
exit /b 1
