@echo off
setlocal

set ROOT=%~dp0
if "%CONFIGURATION%"=="" set CONFIGURATION=Release
if "%SKIP_CLEAN%"=="" set SKIP_CLEAN=0

if not "%SKIP_CLEAN%"=="1" (
  echo =^> Cleaning solution and sample Rust target...
  dotnet clean "%ROOT%mslx-plugin-rustbridge.sln" -c "%CONFIGURATION%"
  if errorlevel 1 goto :err
  cd /d "%ROOT%samples\RustBridgeDemo\rust"
  cargo clean
  if errorlevel 1 goto :err
)

echo =^> [1/2] Packing RustBridge library...
dotnet pack "%ROOT%csharp\MSLX.Plugin.RustBridge.csproj" -c "%CONFIGURATION%" %*
if errorlevel 1 goto :err
echo     Done: csharp\bin\%CONFIGURATION%\MSLX.Plugin.RustBridge.*.nupkg

echo =^> [2/2] Building RustBridge demo plugin...
dotnet build "%ROOT%samples\RustBridgeDemo\MSLX.Plugin.RustBridge.Demo.csproj" -c "%CONFIGURATION%" %*
if errorlevel 1 goto :err
echo     Done: samples\RustBridgeDemo\bin\%CONFIGURATION%\net10.0\MSLX.Plugin.RustBridge.Demo.dll

echo.
echo [OK] Build complete.
echo      Library package: %ROOT%csharp\bin\%CONFIGURATION%\
echo      Demo deploy dir: %ROOT%samples\RustBridgeDemo\bin\%CONFIGURATION%\net10.0\
goto :eof

:err
echo [ERROR] Build failed.
exit /b 1
