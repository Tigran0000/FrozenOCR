# FrozenOCR Architecture (high-level)

## Responsibilities
- Hotkey service: `Input/GlobalHotkeyService.cs`
- Flow controller: `Core/OcrFlowController.cs`
- Monitor/DPI service: `Display/MonitorService.cs`
- Capture service: `Capture/ScreenCaptureService.cs`
- Overlay + selection: `Overlay/FrozenOverlayWindow.xaml` + `.cs`
- Crop/image processing: `ImageProcessing/BitmapCropService.cs` + preprocessors
- OCR service: `Ocr/*` (providers + service)
- Result UI: `UI/ResultWindow.*` (reintroduced in this refactor)
- Settings/persistence: `Settings/SettingsService.cs`
- Logging: `Core/Log.cs`

## Data flow
Hotkey → monitor resolution/DPI → capture screenshot → overlay selection → crop → preprocess → OCR → normalize → overlay + copy/search/translate → clipboard.

## Current coupling points
- `Core/OcrFlowController` drives OCR pipeline and overlay updates.
- `Overlay/FrozenOverlayWindow` binds settings + selection logic.
- OCR engine choice routed via `Ocr/OcrService` (provider manager).
