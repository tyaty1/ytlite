using System.Text;
using System.Text.Json;
using ImageMagick;

namespace YTLite;

/// <summary>
/// YTLite.exe --selftest report.txt
/// Opens test videos in three tabs, then checks: shared renderer process, memory, quality levels
/// (desktop and mobile), ad blocking state, frame grab, lossless/lossy JPEG 2000 round trip and
/// the Lens/Bing upload pages (generated only, nothing is sent). Writes a report and exits.
/// </summary>
static class SelfTest
{
    // Official Blender 4K/60 short and a common 4K demo clip.
    const string Video4K = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";
    const string Video2 = "https://www.youtube.com/watch?v=LXb3EKWsInQ";

    public static bool Active { get; private set; }

    public static void Prepare()
    {
        Active = true;
        BrowserEnvironment.ExtraArguments = "--autoplay-policy=no-user-gesture-required";
    }

    public static async Task RunAsync(MainForm form, string outFile)
    {
        var r = new StringBuilder();
        void Line(string s) => r.AppendLine(s);
        Line($"YTLite self-test {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        try
        {
            var home = form.Current!;
            await home.Ready;
            Line("[ok] WebView2 started, runtime " + (await BrowserEnvironment.GetAsync()).BrowserVersionString);
            Line("uBlock Origin Lite: " + BrowserEnvironment.ExtensionStatus);

            var video = form.NewTab(Video4K, select: true);
            var background = form.NewTab(Video2, select: false);
            await Task.WhenAll(video.Ready, background.Ready);
            await Task.Delay(TimeSpan.FromSeconds(20));
            await ForceQualityAsync(video);
            await Task.Delay(TimeSpan.FromSeconds(15));

            Line($"Background tab title: {background.Title}  (loaded while hidden: {background.Title != "New tab"})");
            Line("");
            Line($"== Processes ({(BrowserEnvironment.StockMode ? "STOCK: site isolation ON, no memory flags" : "YTLite flags")}) ==");
            Line(await form.DescribeProcessesAsync());

            Line("== Desktop player (after requesting hd2160) ==");
            Line(await PlayerStateAsync(video));

            Line("== Ad blocking ==");
            Line($"Requests blocked so far: {AdBlocker.BlockedCount}");

            Line("== Frame grab ==");
            FrameGrab? grab = null;
            try
            {
                grab = await FrameGrabber.GrabAsync(video.Core!);
                Line($"[ok] {grab.Width}x{grab.Height} PNG {grab.Png.Length / 1024} KB at t={grab.Time:F1}s, title \"{grab.Title}\", {grab.WatchUrl}");
            }
            catch (Exception ex) { Line("[FAIL] " + ex.Message); }

            Line("");
            Line("== JPEG 2000 ==");
            Line(Jp2RoundTrip(grab));

            Line("== Visual search pages (generated, not sent) ==");
            if (grab is not null)
            {
                var (jpeg, w, h) = VisualSearchLauncher.MakeJpeg(grab.Png);
                foreach (var engine in new[] { AppSettings.Current.GoogleLens, AppSettings.Current.Bing })
                {
                    var html = VisualSearchLauncher.BuildUploadPage(engine, jpeg, w, h);
                    Line($"[ok] {engine.Name}: {html.Length / 1024} KB page, JPEG {w}x{h} {jpeg.Length / 1024} KB, posts to {engine.UploadUrl}");
                }
            }
            Line("Default browser found: " + (DefaultBrowser.TryGet(out var b) ? b.Exe : "no (would use clipboard fallback)"));

            Line("");
            Line("== Keyboard shortcuts (real keystrokes into the focused page) ==");
            Line("Controller hook: " + BrowserTab.ShortcutHookStatus);
            // Keystrokes go to whatever window is in front, so only send them when that is us.
            async Task<bool> SendIfForeground(string keys)
            {
                form.Activate();
                NativeMethods.SetForegroundWindow(form.Handle);
                video.View.Focus();
                await Task.Delay(800);
                if (NativeMethods.GetForegroundWindow() != form.Handle) return false;
                SendKeys.SendWait(keys);
                return true;
            }

            int before = form.Tabs.Count;
            if (!await SendIfForeground("^t"))
            {
                Line("[SKIPPED] YTLite could not get the foreground (another window is active); no keys were sent.");
            }
            else
            {
                await Task.Delay(2500);
                int afterT = form.Tabs.Count;
                bool sentW = await SendIfForeground("^w");
                await Task.Delay(1500);
                int afterW = form.Tabs.Count;
                form.Select(video);
                bool sentTab = await SendIfForeground("^{TAB}");
                await Task.Delay(1500);
                bool switched = form.Current != video;
                form.Select(video);
                Line($"[{(afterT == before + 1 ? "ok" : "FAIL")}] Ctrl+T: tabs {before} -> {afterT}");
                Line(sentW ? $"[{(afterW == afterT - 1 ? "ok" : "FAIL")}] Ctrl+W: tabs {afterT} -> {afterW}" : "[SKIPPED] Ctrl+W (lost foreground)");
                Line(sentTab ? $"[{(switched ? "ok" : "FAIL")}] Ctrl+Tab switched tab: {switched}" : "[SKIPPED] Ctrl+Tab (lost foreground)");
            }
            Line($"Accelerator events received from WebView2: {BrowserTab.AcceleratorEvents}, last: {BrowserTab.LastAccelerator}");

            Line("");
            Line("== Mobile player (m.youtube.com) ==");
            video.Navigate(Video4K.Replace("www.", "m.") + "&app=m");
            await Task.Delay(TimeSpan.FromSeconds(15));
            await ForceQualityAsync(video);
            await Task.Delay(TimeSpan.FromSeconds(15));
            Line(await PlayerStateAsync(video));
        }
        catch (Exception ex)
        {
            Line("[FAIL] self-test crashed: " + ex);
        }

        await File.WriteAllTextAsync(outFile, r.ToString());
        form.Close();
    }

    /// <summary>Start playback and ask the player for its top quality (what a user picking 2160p does).</summary>
    static async Task ForceQualityAsync(BrowserTab tab)
    {
        const string js = """
            (() => {
              const p = document.querySelector('#movie_player') || document.querySelector('.html5-video-player');
              if (!p) return 'no player';
              const levels = p.getAvailableQualityLevels ? p.getAvailableQualityLevels() : [];
              const top = levels.find(l => l !== 'auto');
              if (top && p.setPlaybackQualityRange) p.setPlaybackQualityRange(top, top);
              if (p.playVideo) p.playVideo(); else p.querySelector('video')?.play();
              return 'requested ' + top;
            })()
            """;
        try { await tab.Core!.ExecuteScriptAsync(js); } catch { }
    }

    static async Task<string> PlayerStateAsync(BrowserTab tab)
    {
        const string js = """
            (() => {
              const p = document.querySelector('#movie_player') || document.querySelector('.html5-video-player');
              const v = p && p.querySelector('video');
              const ipr = window.ytInitialPlayerResponse;
              return JSON.stringify({
                url: location.href,
                player: !!p,
                qualities: p && p.getAvailableQualityLevels ? p.getAvailableQualityLevels() : null,
                playing: v ? !v.paused : null,
                currentTime: v ? Math.round(v.currentTime * 10) / 10 : null,
                readyState: v ? v.readyState : null,
                playerState: p && p.getPlayerState ? p.getPlayerState() : null,
                playbackQuality: p && p.getPlaybackQuality ? p.getPlaybackQuality() : null,
                videoSize: v ? v.videoWidth + 'x' + v.videoHeight : null,
                adShowingNow: p ? p.classList.contains('ad-showing') : null,
                adsThatGotThrough: window.__ytliteAdsSeen || 0,
                initialResponseHasAdPlacements: !!(ipr && ipr.adPlacements),
                initialResponseHasPlayerAds: !!(ipr && ipr.playerAds),
                adblockScriptActive: !!window.__ytliteAdblock,
                antiAdblockDialog: !!document.querySelector('ytd-enforcement-message-view-model')
              });
            })()
            """;
        try
        {
            var json = await tab.Core!.ExecuteScriptAsync(js);
            var inner = JsonSerializer.Deserialize<string>(json) ?? "null";
            using var doc = JsonDocument.Parse(inner);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true }) + "\r\n";
        }
        catch (Exception ex) { return "[FAIL] " + ex.Message + "\r\n"; }
    }

    static string Jp2RoundTrip(FrameGrab? grab)
    {
        var r = new StringBuilder();
        // Use the real frame if we have one, otherwise a synthetic gradient.
        byte[] png;
        if (grab is not null) png = grab.Png;
        else
        {
            using var synth = new MagickImage("gradient:red-blue", 640, 360);
            png = synth.ToByteArray(MagickFormat.Png);
        }
        var g = grab ?? new FrameGrab { Png = png, Width = 640, Height = 360, Title = "synthetic", Time = 61.5 };

        var dir = Path.Combine(Path.GetTempPath(), "YTLite", "selftest");
        Directory.CreateDirectory(dir);
        foreach (var (lossless, q) in new[] { (true, 0), (false, 85), (false, 40) })
        {
            var label = lossless ? "lossless" : $"lossy q{q}";
            var path = Path.Combine(dir, $"frame-{label.Replace(' ', '-')}.jp2");
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Jp2Exporter.Save(g, path, lossless, q);
                sw.Stop();

                using var original = new MagickImage(png);
                original.Alpha(AlphaAction.Off);
                using var back = new MagickImage(path);
                double diff = original.Compare(back, ErrorMetric.Absolute);
                var info = new MagickImageInfo(path);
                var source = Jp2Exporter.ReadSourceUrl(path);
                bool ok = (!lossless || diff == 0) && source == g.WatchUrl;
                r.AppendLine($"[{(ok ? "ok" : "FAIL")}] {label}: {info.Format} {back.Width}x{back.Height}, " +
                             $"{new FileInfo(path).Length / 1024} KB (PNG {png.Length / 1024} KB), {sw.ElapsedMilliseconds} ms, " +
                             $"difference vs PNG: {diff}, embedded source: {source ?? "(missing)"}");
            }
            catch (Exception ex) { r.AppendLine($"[FAIL] {label}: {ex.Message}"); }
        }
        r.AppendLine($"Files: {dir}");
        return r.ToString();
    }
}
