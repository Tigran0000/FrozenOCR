# FrozenOCR Regression Checklist

## Startup and tray
- Launch the app twice. The second launch should show the single-instance message and the first instance should remain responsive.
- Confirm the tray icon appears, the tooltip includes the current hotkey, and the first-run balloon only appears once on a fresh profile.
- Open settings from the tray twice. The existing settings window should be reused and brought to the front instead of opening duplicates.
- Verify tray actions still work after changing theme, hotkey, and screenshot folder.

## Capture and overlay
- Trigger capture with the global hotkey.
- If mouse chord is enabled, trigger capture with the chord.
- Confirm the overlay opens on the monitor under the cursor.
- Create a selection, move it, resize it from edges and corners, then confirm OCR with Enter and the action button.
- Press Esc from these states: fresh overlay, search panel open, settings modal open, OCR-ready selection. Each path should close only the active surface.
- Verify crop bounds still match the selected region on 100%, 125%, and 150% DPI displays.
- Repeat the flow on mixed-DPI multi-monitor setups.

## OCR
- Test on dark terminal content, light editor content, and mixed UI.
- Test English text, Cyrillic text, and mixed code/symbol content.
- Confirm provider auto mode prefers Windows AI when available and falls back cleanly to Windows Media when it is not.
- Confirm OCR language auto mode works without changing settings.
- Change OCR provider/language settings, save, reopen settings, and verify they persist.

## Selection actions
- Copy a text selection and verify the clipboard contains text.
- Save a screenshot and verify a PNG is written to the configured folder and the image is copied to the clipboard.
- Click the toast action after saving and confirm the folder opens.
- Set a custom screenshot folder from both the overlay settings and the tray settings window, then relaunch and confirm persistence.
- Use the tray settings window Open button to confirm the configured screenshot folder is reachable.

## Search and translate panel
- Open search from a text selection, then close and reopen it repeatedly. Memory should stabilize and the panel should still reinitialize correctly.
- Open translate from a text selection and confirm navigation stays inside the panel.
- Test Google, Bing, and DuckDuckGo templates.
- Click several results, including links that would normally open a new window. They should stay inside the existing panel.
- Verify back/forward button enabled states track page history.
- Try a blocked non-HTTP link and confirm it is rejected with a toast.
- Close the search panel and then the whole overlay while media-heavy pages are open. Audio/video should stop.

## Settings and theme
- Change theme in the tray settings window and in the overlay settings panel. Confirm both update immediately and persist after restart.
- Change the hotkey in the overlay settings panel and confirm the tray tooltip updates right away.
- Toggle mouse chord on and off in both settings surfaces and verify the new state persists.
- Enter an invalid screenshot folder and confirm the app shows a warning instead of silently accepting it.

## Publish and packaging
- Publish while the app is not running.
- Attempt publish while `dist/FrozenOCR.exe` is running and confirm the failure mode is still the expected file-in-use error.
- Verify single-file publish output still launches, registers the hotkey, opens WebView2, and performs OCR.
- Confirm publish output no longer reports the known `WFAC010` warning.

