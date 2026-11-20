using Microsoft.Web.WebView2.Core;

namespace YTLite;

/// <summary>Suspends tabs that have been hidden for a while and are not playing audio.</summary>
sealed class TabSuspender : IDisposable
{
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 30_000 };
    readonly Func<IReadOnlyList<BrowserTab>> _tabs;
    readonly Func<BrowserTab?> _current;
    readonly Action _changed;
    bool _busy;

    public TabSuspender(Func<IReadOnlyList<BrowserTab>> tabs, Func<BrowserTab?> current, Action changed)
    {
        _tabs = tabs;
        _current = current;
        _changed = changed;
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    async Task TickAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var limit = TimeSpan.FromMinutes(Math.Max(1, AppSettings.Current.SuspendAfterMinutes));
            bool any = false;
            foreach (var tab in _tabs().ToList())
            {
                if (tab == _current() || tab.Suspended || tab.PlayingAudio || tab.Core is null) continue;
                if (DateTime.UtcNow - tab.HiddenSince < limit) continue;
                try
                {
                    tab.Core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                    tab.Suspended = await tab.Core.TrySuspendAsync();
                    any |= tab.Suspended;
                }
                catch { /* tab closed or became visible meanwhile */ }
            }
            if (any) _changed();
        }
        finally { _busy = false; }
    }

    public static void Wake(BrowserTab tab)
    {
        if (tab.Core is { } core)
        {
            try
            {
                if (tab.Suspended) core.Resume();
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch { /* already awake */ }
        }
        tab.Suspended = false;
    }

    public void Dispose() => _timer.Dispose();
}
