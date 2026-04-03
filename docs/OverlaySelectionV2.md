# Overlay Selection v2 — Project Summary

## Current flow (existing)
Hotkey → monitor detection → capture → overlay → selection → crop → OCR → result → clipboard

## File/module mapping
- Hotkey: `Input/GlobalHotkeyService.cs`
- Flow controller: `Core/OcrFlowController.cs`
- Monitor + DPI: `Display/MonitorService.cs`, `Display/MonitorInfo.cs`, `Native/NativeMethods.cs`
- Capture: `Capture/ScreenCaptureService.cs`
- Overlay UI + interaction: `Overlay/FrozenOverlayWindow.xaml(.cs)`
- Selection rectangle logic: `Overlay/FrozenOverlayWindow.xaml.cs` (mouse events, selection state)
- Crop/image processing: `ImageProcessing/BitmapCropService.cs`, `Imaging/BitmapSourceHelper.cs`
- OCR service/provider: `Ocr/OcrService.cs`, `Ocr/Providers/*`
- Result UI: (Web search panel inside overlay), `Overlay/FrozenOverlayWindow.xaml(.cs)`
- Settings/logging: `Settings/SettingsService.cs`, `Core/Log.cs`

## Integration points (updated for v2)
- Selection created/finished: `OnMouseLeftButtonDown/Move/Up` in `FrozenOverlayWindow.xaml.cs`
- Selection stored: `_selectionPixelRect` / `_selectionDipRect`
- OCR trigger: `ConfirmOcrSelection()` → `ConfirmOcrRequested` event
- Overlay close: `CloseSafely()`; Esc closes modal first, then overlay
- New post‑selection state: `OverlayState.OcrAreaSelected` enables resize/move + action bar

## New v2 behaviors
- After initial mouse‑up, selection enters **Selected** state (handles + action bar).
- OCR triggers only on **Confirm** button or **Enter** key.
- Save / Cancel buttons expose hook events for future features.

