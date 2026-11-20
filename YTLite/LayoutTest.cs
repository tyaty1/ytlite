using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace YTLite;

/// <summary>
/// YTLite.exe --layouttest OUTDIR [--no-style] [--only NAME,...]
/// Loads mobile YouTube pages and, for each target window size, emulates that viewport through
/// DevTools, saves a screenshot and records layout measurements (OUTDIR\layout.txt).
/// Sizes are the CSS-pixel content area of full / half / quarter windows on common monitors.
/// </summary>
static class LayoutTest
{
    public static readonly (string Name, int W, int H)[] Sizes =
    {
        ("1080p-quarter",      960,  470),
        ("1080p-half",         960,  980),
        ("1080p-full",        1920,  980),
        ("1200p-quarter",      960,  530),
        ("1200p-full",        1920, 1100),
        ("1440p-quarter",     1280,  650),
        ("1440p-half",        1280, 1340),
        ("1440p-full",        2560, 1340), // also 2160p at 150 % scaling
        ("1600p-quarter",     1280,  730),
        ("1600p-full",        2560, 1500),
        ("uw1440-quarter",    1720,  650),
        ("uw1440-half",       1720, 1340),
        ("uw1440-full",       3440, 1340),
        ("2160p-quarter",     1920, 1010),
        ("2160p-full",        3840, 2060),
        ("uw2160-full",       5120, 2060),
    };

    static readonly (string Name, string Url)[] Pages =
    {
        ("search", "https://m.youtube.com/results?search_query=blender+open+movie&app=m"),
        ("watch",  "https://m.youtube.com/watch?v=aqz-KE-bpKQ&app=m"),
        ("channel", "https://m.youtube.com/@BlenderStudio/videos?app=m"),
    };

    public static string[]? Only { get; set; }
    public static string[]? OnlySizes { get; set; }

    const string MetricsJs = """
        (() => {
          const r = sel => { const e = document.querySelector(sel); if (!e) return null;
            const b = e.getBoundingClientRect(); const cs = getComputedStyle(e);
            if (cs.display === 'none' || (b.width === 0 && b.height === 0)) return 'hidden';
            return `${Math.round(b.left)},${Math.round(b.top)} ${Math.round(b.width)}x${Math.round(b.height)} ${cs.position}`; };
          const grid = document.querySelector('ytm-item-section-renderer > lazy-list, ytm-rich-grid-renderer');
          const cols = grid ? getComputedStyle(grid).gridTemplateColumns.split(' ').filter(x => x.endsWith('px')).length : 0;
          return JSON.stringify({
            viewport: innerWidth + 'x' + innerHeight,
            hOverflow: document.documentElement.scrollWidth - innerWidth,
            player: r('#player-container-id'),
            belowPlayer: r('.watch-below-the-player'),
            related: r('.related-items-container'),
            topbar: r('ytm-mobile-topbar-renderer'),
            rail: r('ytm-pivot-bar-renderer'),
            gridCols: cols,
            cardChain: (() => { let e = document.querySelector('ytm-media-item'); const a = []; for (let i = 0; e && i < 7; i++, e = e.parentElement) { const cs = getComputedStyle(e); a.push(e.tagName.toLowerCase() + (typeof e.className === 'string' && e.className.trim() ? '.' + e.className.trim().split(/\s+/).slice(0, 2).join('.') : '') + '{' + cs.display + (cs.display === 'grid' ? ' cols=' + cs.gridTemplateColumns.split(' ').length : '') + ' w=' + Math.round(e.getBoundingClientRect().width) + '}'); } return a.join(' < '); })(),
            placeholder: r('.player-size'),
            richChain: (() => { let e = document.querySelector('ytm-rich-item-renderer'); if (!e) return ''; const desc = []; const walk = (n, d) => { if (d > 4) return; for (const c of n.children) { const cs = getComputedStyle(c); const b = c.getBoundingClientRect(); desc.push('  '.repeat(d) + c.tagName.toLowerCase() + (typeof c.className === 'string' && c.className.trim() ? '.' + c.className.trim().split(/\s+/).slice(0, 3).join('.') : '') + '{' + cs.display + ' ' + cs.flexDirection + ' ' + Math.round(b.width) + 'x' + Math.round(b.height) + '}'); walk(c, d + 1); } }; walk(e, 0); const up = []; for (let i = 0, x = e.parentElement; x && i < 5; i++, x = x.parentElement) { const cs = getComputedStyle(x); up.push(x.tagName.toLowerCase() + (typeof x.className === 'string' && x.className.trim() ? '.' + x.className.trim().split(/\s+/).slice(0, 2).join('.') : '') + '{' + cs.display + (cs.display === 'grid' ? ' cols=' + cs.gridTemplateColumns : '') + ' w=' + Math.round(x.getBoundingClientRect().width) + '}'); } return up.join(' < ') + '\n' + desc.slice(0, 40).join('\n'); })(),
            firstCard: r('ytm-item-section-renderer ytm-media-item, ytm-rich-item-renderer'),
            styleInjected: !!document.getElementById('ytlite-desktop')
          });
        })()
        """;

    public static async Task RunAsync(MainForm form, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder($"YTLite layout test {DateTime.Now:yyyy-MM-dd HH:mm:ss}, style {(DesktopStyle.Disabled ? "OFF" : "ON")}\r\n\r\n");
        try
        {
            var tab = form.Current!;
            await tab.Ready;
            var core = tab.Core!;
            core.IsMuted = true;

            foreach (var (pageName, url) in Pages)
            {
                if (Only is not null && !Only.Contains(pageName)) continue;
                await NavigateAsync(core, url);
                await Task.Delay(6000);
                await DismissConsentAsync(core);
                report.AppendLine($"== {pageName}  {url}");

                foreach (var (name, w, h) in Sizes)
                {
                    if (OnlySizes is not null && !OnlySizes.Contains(name)) continue;
                    await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
                        JsonSerializer.Serialize(new { width = w, height = h, deviceScaleFactor = 1, mobile = false }));
                    await Task.Delay(1800);
                    var metrics = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(MetricsJs));
                    report.AppendLine($"{name,-15} {metrics}");

                    var shot = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",
                        """{"format":"jpeg","quality":70}""");
                    using var doc = JsonDocument.Parse(shot);
                    await File.WriteAllBytesAsync(Path.Combine(outDir, $"{pageName}-{name}.jpg"),
                        Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!));
                }
                await core.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}");
                report.AppendLine();
            }
        }
        catch (Exception ex)
        {
            report.AppendLine("[FAIL] " + ex);
        }

        await File.WriteAllTextAsync(Path.Combine(outDir, "layout.txt"), report.ToString());
        form.Close();
    }

    static async Task NavigateAsync(CoreWebView2 core, string url)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Done(object? s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult();
        core.NavigationCompleted += Done;
        try
        {
            core.Navigate(url);
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException) { }
        finally { core.NavigationCompleted -= Done; }
    }

    /// <summary>Signed-out test profile: decline the cookie dialog (the privacy-preserving choice).</summary>
    static async Task DismissConsentAsync(CoreWebView2 core)
    {
        const string js = """
            (() => {
              const b = [...document.querySelectorAll('button, ytm-button-renderer button')]
                .find(x => /^\s*Reject all\s*$/i.test(x.textContent));
              if (b) { b.click(); return true; }
              return false;
            })()
            """;
        if (await core.ExecuteScriptAsync(js) == "true") await Task.Delay(4000);
    }
}
