# FrozenOCR

FrozenOCR is a Windows desktop capture + OCR utility built with WPF and .NET 8.

It lets you press a global hotkey, capture a region on the monitor under your cursor, run OCR, and quickly copy, search, translate, or save the result.

## Features

- Global hotkey capture workflow
- Optional mouse chord launcher
- Multi-monitor support
- DPI-aware region selection
- OCR with Windows AI OCR when available
- Automatic fallback to Windows Media OCR
- Copy recognized text to clipboard
- Search selected text inside the app
- Translate selected text inside the app
- Save selected screenshot to a folder of your choice
- Tray app behavior with settings window

## How It Works

1. Press the hotkey.
2. FrozenOCR captures the monitor under your cursor.
3. Select the area you want.
4. OCR runs on the selected region.
5. Copy, search, translate, or save the result.

Default hotkey:

`Ctrl + Alt + Space`

## Screenshots / Demo

You can add screenshots or a short GIF here later.

Suggested items:

- tray icon + settings
- capture overlay
- OCR result selection
- search / translate side panel

## Requirements

- Windows 10/11 x64
- .NET 8 SDK for building from source
- WebView2 runtime available on the system

## Build From Source

Open PowerShell in the project folder:

```powershell
dotnet restore .\FrozenOCR.csproj
dotnet build .\FrozenOCR.csproj -c Debug
dotnet run .\FrozenOCR.csproj -c Debug
```

## Publish

```powershell
Stop-Process -Name FrozenOCR -Force -ErrorAction SilentlyContinue
dotnet publish .\FrozenOCR.csproj -c Release -r win-x64 -o .\dist
```

Published executable:

`dist\FrozenOCR.exe`

To make a shareable zip:

```powershell
Compress-Archive -Path .\dist\* -DestinationPath .\FrozenOCR-win-x64.zip -Force
```

## Manual Testing

Useful commands:

```powershell
dotnet run .\FrozenOCR.csproj -c Debug
Get-Content "$env:LOCALAPPDATA\FrozenOCR\log.txt" -Wait
```

Before release, verify:

- app starts correctly
- tray icon appears
- hotkey opens the overlay
- OCR returns text
- copy works
- save screenshot works
- search / translate works
- app closes cleanly

For a broader manual checklist, see [TESTING.md](./TESTING.md).

## Project Structure

- `App.xaml.cs` - app lifecycle, tray behavior, hotkey bootstrap
- `Core/OcrFlowController.cs` - capture-to-OCR orchestration
- `Overlay/` - selection overlay and OCR interaction UI
- `Ocr/` - OCR services, models, providers
- `Settings/` - settings persistence and data model
- `Display/` - monitor and DPI logic
- `Capture/` - screen capture services

## Tech Stack

- .NET 8
- WPF
- Win32 interop
- WebView2
- Windows AI OCR
- Windows Media OCR fallback

## Notes

- The app is designed as a tray-style utility and uses explicit shutdown behavior.
- OCR provider availability depends on the Windows environment.
- Publish output may fail if `dist\FrozenOCR.exe` is already running. Close it before publishing.

## License

This project is licensed under the MIT License. See [LICENSE](./LICENSE).
