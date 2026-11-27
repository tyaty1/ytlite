# YTLite

A small YouTube-only browser for Windows, built on WebView2 (Edge's engine, which updates
itself). It has tabs, every quality YouTube offers (up to 2160p), no ads, and video-frame
screenshots that you can send to Google Lens or Bing, or save as JPEG 2000.

Run: `publish\YTLite.exe`. You need the WebView2 runtime (already on Windows 10/11 with Edge) and
the .NET 10 Desktop Runtime. The SDK is installed per user in `%LOCALAPPDATA%\Microsoft\dotnet`.

## Mobile mode with a desktop layout
⋮ → "Use lighter mobile YouTube" switches to m.youtube.com, and "Desktop layout for mobile
YouTube" (on by default) restyles it for desktop windows:
- **Navigation:** a left rail instead of the phone's bottom tab bar, and a centered search box.
- **Feeds** (search, home, channels): card grids, from 3 columns at about 960 px wide up to large
  cards on 4K and ultrawide screens.
- **Watch page:** the player fills the window as far as its height allows, with the title still
  visible, and related videos run in a column beside it. In quarter windows they become stacked
  cards; in portrait (half) windows they go below the player.

All sizes are computed from the window, so the layout works for full, half and quarter windows
on 1080p–2160p, 16:9, 16:10 and ultrawide monitors.

The CSS is `Styles\mobile-desktop.css` next to the exe; edit it and open a new tab to see the
change. To check every target size:
`YTLite.exe --layouttest OUTDIR [--no-style] [--only search,watch,channel] [--sizes 1080p-quarter,...]`
saves a screenshot and layout measurements per size. Run it with `YTLITE_DATA_DIR` set to a
spare folder so it doesn't share your profile.

## How it saves RAM
- **Site isolation is off**, with `--process-per-site`, so every YouTube tab shares **one**
  renderer process. Google's sign-in page keeps its own process.
  The trade-off: a malicious page could read data from other sites in the same process. Only
  YouTube pages load here, so that's mostly YouTube itself.
- **Hidden tabs get suspended.** A tab that has been hidden for 5 minutes and isn't playing
  audio is suspended (💤 on the tab). Clicking it wakes it up.
- **Only YouTube and Google sign-in load.** Every other link opens in your normal browser.
- **No ads.** Ad players and trackers never load.

Measured on this machine with 3 tabs (home + two 4K videos, paused): **953 MB** with YTLite
versus **1,062 MB** with Chromium's normal process model. That's 3 renderer processes instead
of 5. The video decoder (GPU process, about 300 MB at 4K) is the same either way.

## Shortcuts
Ctrl+T new tab · Ctrl+W close · Ctrl+Tab / Ctrl+Shift+Tab switch · Ctrl+L search ·
Ctrl+Shift+S screenshot menu · F5 reload · Alt+←/→ back/forward · middle-click a tab to close it · drag a tab to move it

## Screenshots (📷 Frame)
The frame is taken at the video's native resolution, without player controls.
- **Google Lens / Bing Visual Search**: a temporary page opens in your default browser and
  uploads the frame, scaled to at most 2000 px on its longest side. These upload endpoints are
  not official APIs, so they may change. If one breaks, fix the URL in `settings.json`, or set
  `"VisualSearchMode": "clipboard"` to copy the frame and paste it on the page with Ctrl+V.
- **Save as JPEG 2000**: saved to `Pictures\YTLite` (lossless by default). The video link with
  its timestamp is stored inside the file in a standard JP2 XML box.
  - At 4K: PNG 11.4 MB → lossless JP2 5.2 MB.
  - With `"Jp2Lossless": false`, quality 85 is about the same size as lossless; around
    40–60 gives real savings (quality 40 = 0.7 MB).

## Ad blocking
Two layers run together:
1. **uBlock Origin Lite**, bundled in `extensions\ubol\` and maintained upstream.
   To update it, replace that folder with the newest Edge zip from
   https://github.com/uBlockOrigin/uBOL-home/releases
2. **The built-in blocker** (`AdBlock\adblock-rules.json` + `AdBlock\adblock.js`): blocks ad
   requests, removes ad data from YouTube's player responses, hides ad elements, and
   fast-forwards and skips any ad that still gets through.
   Both files are read whenever a tab opens, so you can edit them without rebuilding.

YouTube changes its ads and anti-adblock checks often, so expect to update one of these now and
then. Blocking ads goes against YouTube's terms.

## Settings
`%LOCALAPPDATA%\YTLite\settings.json` (⋮ → Open settings folder):
- UI mode: `desktop` or `mobile`
- `SuspendAfterMinutes`
- `RendererProcessLimit`
- `UseUblockLite`
- JPEG 2000 options
- Lens/Bing endpoints

## Self-test
`YTLite.exe --selftest report.txt [--stock] [--no-adblock]` opens test videos (muted) and
checks the process layout, memory, quality levels, ad-block state, frame grab, the JPEG 2000
round trip and the Lens/Bing pages (the pages are generated, not sent). It writes a report, then
exits. `--stock` runs Chromium's normal process model for comparison.

## Build
```
%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe publish YTLite\YTLite.csproj -c Release -o publish -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

# YTLite for Android (`android\`)

One APK (`app.ytlite`) for phones, tablets and Android TV. It uses the system WebView and
installs on Android 4.4 and newer.

| Device | What it opens | Notes |
|---|---|---|
| **Android TV / Google TV** (10–12 tested on 11) | `youtube.com/tv`, YouTube's own remote-friendly interface (smart-TV user agent) | Listed in the TV launcher with a banner. Use the remote's D-pad; Back goes back, pressing Back twice quickly exits. Sign in by entering a code at yt.be/activate on your phone. |
| **Phones / tablets** (5.0+) | `m.youtube.com` with tabs | Tab button shows the count; tap it to switch, long-press an entry to close it. Long-press a link or thumbnail to open it in a new or background tab. Full-screen video rotates to landscape. Wide tablets get the desktop layout. |
| **Android 4.4** | same as phones | Its built-in WebView (Chromium 30–33) can't be updated, and current YouTube can't play in it. The app warns once with the engine version; titles and comments load, video playback fails. |

Shared with Windows:
- **Navigation lock:** the same list of allowed YouTube and Google sign-in pages.
- **Ad blocking:** `AdBlock\adblock.js` and `adblock-rules.json`, with ad requests blocked inside
  the WebView. uBlock Origin Lite isn't available, because Android WebView can't run extensions.
- **Desktop layout:** `Styles\mobile-desktop.css`.

These files are packed into the APK from the Windows project's folders, so there is one copy of each.

Not on Android yet: screenshots, Lens/Bing and JPEG 2000.

**Build** (JDK 17 and the Android SDK are installed per user in `%LOCALAPPDATA%\Android`):
```
$env:JAVA_HOME="$env:LOCALAPPDATA\Android\jdk-17"; $env:ANDROID_HOME="$env:LOCALAPPDATA\Android\Sdk"
cd android; .\gradlew.bat assembleRelease      # -> app\build\outputs\apk\release\app-release.apk
```
The release APK is signed with the debug key so it can be sideloaded. Use your own key before
distributing it.

## Android 4.4 experiment: GeckoView (`android\geckotest`)
GeckoView Lite 118 (Gecko from Sept 2023) is the newest browser engine that still supports
Android 4.4.
- **What runs:** all of YouTube's mobile site works in it, unlike 4.4's built-in WebView.
- **Video:** in the 4.4 emulator, YouTube plays when MP4 is turned off and Gecko decodes VP9 with
  its own software decoder. The default H.264 route fails there, at Android 4.4's own decoder.
- **Cost:** about 65 MB of APK for ARM, and an engine with unpatched security holes.

Test APK for real phones: `publish-android\YTLite-GeckoTest-armv7.apk`. It asks for H.264
(Android hardware decoder) or VP9 (Gecko software decoder) at start; force-stop the app to switch.
The choice is written into Gecko's `user.js` in the app's own profile folder.
Build: `.\gradlew.bat -Pgeckotest :geckotest:assembleDebug` (add `-Pgeckoabi=x86` for the emulator).

**Test tools** (`android\tools`):
- `cdp-eval.ps1 -Serial <device> -Expr "<js>"` runs JavaScript in the app's WebView (debug builds).
- `DevProxy.cs` is a localhost HTTP proxy for emulators on networks that block their DNS:
  `dotnet run android\tools\DevProxy.cs`, then
  `adb shell settings put global http_proxy 10.0.2.2:8888`.

# License
YTLite's own code is under the MIT License (`LICENSE`).

Bundled third-party parts keep their own licenses:
- **uBlock Origin Lite** (`YTLite/extensions/ubol/`): GPL-3.0, see `YTLite/extensions/ubol/LICENSE.txt`.
  Its filter lists come from their own projects under their own licenses.
- **NuGet and Maven packages** (WebView2 SDK, Magick.NET, AndroidX WebKit, GeckoView) are
  downloaded at build time under their own licenses.
  GeckoView is MPL-2.0 and is only used by the `geckotest` experiment.
