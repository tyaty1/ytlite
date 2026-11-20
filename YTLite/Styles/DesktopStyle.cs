using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace YTLite;

/// <summary>
/// Restyles m.youtube.com for desktop windows (left rail instead of the bottom bar, card grids,
/// a player that fits the window). The CSS lives in Styles\mobile-desktop.css next to the exe and is
/// re-read for every new tab, so it can be tweaked without rebuilding.
/// </summary>
static class DesktopStyle
{
    public static string CssPath => Path.Combine(AppContext.BaseDirectory, "Styles", "mobile-desktop.css");

    /// <summary>Layout-test switch (--no-style) for baseline screenshots.</summary>
    public static bool Disabled { get; set; }

    public static async Task AttachAsync(CoreWebView2 core)
    {
        if (Disabled || !AppSettings.Current.MobileDesktopLayout) return;

        string css;
        try { css = await File.ReadAllTextAsync(CssPath); }
        catch { return; }

        // Added as early as possible (no flash of the phone layout), then moved to the end of <head>
        // once YouTube's own stylesheets are in, so equal-specificity rules resolve in our favour.
        var js = $$"""
            (() => {
              if (location.hostname !== 'm.youtube.com' || window.top !== window) return;
              const css = {{JsonSerializer.Serialize(css)}};
              const place = () => {
                let s = document.getElementById('ytlite-desktop');
                if (!s) { s = document.createElement('style'); s.id = 'ytlite-desktop'; s.textContent = css; }
                (document.head || document.documentElement).appendChild(s);
              };
              if (document.documentElement) place();
              document.addEventListener('DOMContentLoaded', place, { once: true });
              window.addEventListener('load', place, { once: true });
            })();
            """;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(js);
    }
}
