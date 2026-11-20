using Microsoft.Web.WebView2.Core;

namespace YTLite;

/// <summary>One WebView2 environment (browser process + profile) shared by every tab.</summary>
static class BrowserEnvironment
{
    static Task<CoreWebView2Environment>? _env;
    static Task? _profileSetup;

    /// <summary>Extra Chromium switches (the self-test adds an autoplay switch). Set before the first tab.</summary>
    public static string ExtraArguments { get; set; } = "";

    public static string ExtensionStatus { get; private set; } = "not loaded yet";

    /// <summary>Self-test comparison mode: stock Chromium process model (site isolation on).</summary>
    public static bool StockMode { get; set; }

    public static string Arguments
    {
        get
        {
            if (StockMode) return ExtraArguments;
            var s = AppSettings.Current;
            return string.Join(' ', new[]
            {
                // Site isolation off: sites share renderer processes again.
                "--disable-site-isolation-trials",
                "--disable-features=SitePerProcess,IsolateOrigins,OriginKeyedProcessesByDefault",
                // Every youtube.com tab reuses one renderer process.
                "--process-per-site",
                $"--renderer-process-limit={Math.Max(1, s.RendererProcessLimit)}",
                // Keep the Google sign-in page in its own process anyway (cheap, protects credentials).
                "--isolate-origins=https://accounts.google.com",
                ExtraArguments,
            }.Where(a => a.Length > 0));
        }
    }

    public static Task<CoreWebView2Environment> GetAsync() => _env ??= CreateAsync();

    static Task<CoreWebView2Environment> CreateAsync()
    {
        var options = new CoreWebView2EnvironmentOptions(Arguments)
        {
            AreBrowserExtensionsEnabled = AppSettings.Current.UseUblockLite,
        };
        return CoreWebView2Environment.CreateAsync(null, Path.Combine(AppSettings.DataDir, "WebView2"), options);
    }

    /// <summary>Runs once per app start (all tabs share the profile): loads uBlock Origin Lite.</summary>
    public static Task EnsureProfileAsync(CoreWebView2Profile profile) => _profileSetup ??= SetupProfileAsync(profile);

    static async Task SetupProfileAsync(CoreWebView2Profile profile)
    {
        if (AdBlocker.Disabled)
        {
            foreach (var ext in await profile.GetBrowserExtensionsAsync())
                if (ext.IsEnabled) await ext.EnableAsync(false);
            ExtensionStatus = "disabled for this run (--no-adblock)";
            return;
        }
        if (!AppSettings.Current.UseUblockLite)
        {
            ExtensionStatus = "off (UseUblockLite = false in settings.json)";
            return;
        }

        var dir = Path.Combine(AppContext.BaseDirectory, "extensions", "ubol");
        if (!File.Exists(Path.Combine(dir, "manifest.json")))
        {
            ExtensionStatus = "not bundled (extensions\\ubol missing); built-in blocker only";
            return;
        }

        try
        {
            // Unpacked extensions are re-read from their folder on start, so replacing the folder updates it.
            foreach (var ext in await profile.GetBrowserExtensionsAsync())
            {
                if (ext.Name.Contains("uBlock", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ext.IsEnabled) await ext.EnableAsync(true);
                    ExtensionStatus = $"loaded: {ext.Name} ({ext.Id})";
                    return;
                }
            }

            var added = await profile.AddBrowserExtensionAsync(dir);
            ExtensionStatus = $"loaded: {added.Name} ({added.Id})";
        }
        catch (Exception ex)
        {
            ExtensionStatus = $"failed to load ({ex.Message}); built-in blocker only";
        }
    }
}
