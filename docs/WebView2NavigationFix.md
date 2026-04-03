# WebView2 Navigation Fix - Current State

## Current search panel flow
- Search panel opens via `OpenSearchPanelAsync()` -> `OpenSearchPanel()` -> `NavigateSearch()` in `Overlay/FrozenOverlayWindow.xaml.cs`.
- Search/translate mode is controlled by `SearchPanelMode` (Search/Translate).
- Panel state is tracked by `_isSearchPanelOpen` and visibility toggles.

## URL building for providers
- `Settings/SettingsService.cs` builds search URLs with templates:
  - `BuildSearchUrl()` replaces `{q}` with `Uri.EscapeDataString(query)`.
  - Default providers: Google, Bing, DuckDuckGo.
- Translate URLs:
  - `BuildTranslateUrl()` replaces `{from}`, `{to}`, `{q}`.
  - Default provider: Google Translate.
- `NavigateSearch()` uses settings to build the URL and assigns `WebView2.Source`.

## WebView2 creation and hosting
- Host container: `SearchWebViewHost` (`Grid`) in `Overlay/FrozenOverlayWindow.xaml`.
- WebView2 is created lazily in `EnsureWebViewCreated()` in code-behind.
- User data folder is set in `InitializeWebViewAsync()`:
  - `%LocalAppData%\FrozenOCR\WebView2`
  - `CreationProperties.UserDataFolder` is set before use.

## Current navigation handling
- Handlers present: only `PreviewKeyDown` (Esc closes panel).
- Missing handlers:
  - `NavigationStarting`, `NavigationCompleted`
  - `CoreWebView2.NewWindowRequested`
  - `CoreWebView2.HistoryChanged` (or `SourceChanged`)
- No centralized navigation policy (http/https vs non-http).

## External browser launch
- No explicit `Process.Start` or external browser opens are used for WebView2 navigation.
- Because `NewWindowRequested` is not handled, some providers can open external windows/popups.

## Back/Forward UI
- Back/Forward buttons exist in `Overlay/FrozenOverlayWindow.xaml`.
- Click handlers are stubs in `Overlay/FrozenOverlayWindow.xaml.cs`.
- `UpdateSearchNavButtons()` hard-disables both buttons.
- No usage of `CanGoBack` / `CanGoForward`.

## Root causes (likely)
- No `NewWindowRequested` handler: providers that open new windows (Bing, some result links) may escape the WebView.
- Back/Forward history state not tracked: no `HistoryChanged` or navigation events.
- Buttons are not wired to `GoBack/GoForward`.

