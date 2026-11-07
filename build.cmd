@echo off
setlocal

dotnet publish src\RazerTrayStatus\RazerTrayStatus.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=none ^
  -o dist

if errorlevel 1 (
  echo.
  echo Build failed.
  exit /b 1
)

echo.
echo Built dist\RazerTrayStatus.exe
endlocal
