using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace YTLite;

/// <summary>One tab = one WebView2 control. All tabs share the same environment and renderer process.</summary>
sealed partial class BrowserTab : IDisposable
{
    public WebView2 View { get; } = new()
    {
        Dock = DockStyle.Fill,
        DefaultBackgroundColor = Color.FromArgb(15, 15, 15),
    };

    public string Title { get; private set; } = "New tab";
    public Image? Favicon { get; private set; }
    public bool PlayingAudio { get; private set; }
    public bool IsFullScreen { get; private set; }
    public bool Suspended { get; set; }
    public DateTime HiddenSince { get; set; } = DateTime.UtcNow;
    public Task Ready { get; private set; } = Task.CompletedTask;
    public CoreWebView2? Core => View.CoreWebView2;

    public event EventHandler? StateChanged;
    public event EventHandler? FullScreenChanged;
    public event EventHandler<string>? OpenInNewTab;
    public event EventHandler? CloseRequested;

    /// <summary>Call after <see cref="View"/> has been added to a parent control.</summary>
    public void Start(string url) => Ready = InitAsync(url);

    public void Navigate(string url)
    {
        if (Core is { } core) core.Navigate(url);
        else Ready = Ready.ContinueWith(_ => Core?.Navigate(url), TaskScheduler.FromCurrentSynchronizationContext());
    }

    async Task InitAsync(string url)
    {
        var env = await BrowserEnvironment.GetAsync();
        await View.EnsureCoreWebView2Async(env);
        var core = View.CoreWebView2;
        HookShortcutKeys();

        var s = core.Settings;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsStatusBarEnabled = false;
#if DEBUG
        s.AreDevToolsEnabled = true;
#else
        s.AreDevToolsEnabled = false;
#endif

        await BrowserEnvironment.EnsureProfileAsync(core.Profile);
        await AdBlocker.AttachAsync(core, env);
        await DesktopStyle.AttachAsync(core);

        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.DocumentTitleChanged += (_, _) => { Title = CleanTitle(core.DocumentTitle); Raise(); };
        core.FaviconChanged += async (_, _) => await UpdateFaviconAsync();
        core.IsDocumentPlayingAudioChanged += (_, _) => { PlayingAudio = core.IsDocumentPlayingAudio; Raise(); };
        core.ContainsFullScreenElementChanged += (_, _) =>
        {
            IsFullScreen = core.ContainsFullScreenElement;
            FullScreenChanged?.Invoke(this, EventArgs.Empty);
        };

        if (SelfTest.Active) core.IsMuted = true; // don't blast audio during automated checks
        core.Navigate(url);
    }

    /// <summary>
    /// Keys pressed while the page has focus go to the browser process, not to WinForms, so the
    /// form's ProcessCmdKey never sees Ctrl+T etc. The controller's AcceleratorKeyPressed event does.
    /// The WinForms control keeps its controller private, so it is looked up by type.
    /// </summary>
    void HookShortcutKeys()
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        CoreWebView2Controller? controller = null;
        for (var t = View.GetType(); t is not null && controller is null; t = t.BaseType)
        {
            controller = t.GetFields(Flags).Where(f => f.FieldType == typeof(CoreWebView2Controller))
                             .Select(f => f.GetValue(View)).OfType<CoreWebView2Controller>().FirstOrDefault()
                         ?? t.GetProperties(Flags).Where(p => p.PropertyType == typeof(CoreWebView2Controller) && p.GetIndexParameters().Length == 0)
                             .Select(p => { try { return p.GetValue(View); } catch { return null; } })
                             .OfType<CoreWebView2Controller>().FirstOrDefault();
        }
        ShortcutHookStatus = controller is null ? "controller not found" : "hooked";
        if (controller is null) return;

        controller.AcceleratorKeyPressed += (_, e) =>
        {
            if (e.KeyEventKind is not (CoreWebView2KeyEventKind.KeyDown or CoreWebView2KeyEventKind.SystemKeyDown)) return;
            if (e.PhysicalKeyStatus.WasKeyDown != 0) return; // ignore auto-repeat
            var vk = (Keys)e.VirtualKey;
            if (vk is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return; // modifier on its own
            var keys = vk | NativeMethods.Modifiers;
            AcceleratorEvents++;
            LastAccelerator = keys.ToString();
            if (ShortcutPressed?.Invoke(keys) == true) e.Handled = true;
        };
    }

    public static string ShortcutHookStatus { get; private set; } = "not initialized";
    public static int AcceleratorEvents { get; private set; }
    public static string LastAccelerator { get; private set; } = "";

    /// <summary>Returns true if the key was a YTLite shortcut (the page then doesn't get it).</summary>
    public Func<Keys, bool>? ShortcutPressed { get; set; }

    void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (NavigationPolicy.IsAllowed(e.Uri)) return;

        e.Cancel = true;
        if (e.IsUserInitiated || e.IsRedirected)
            NavigationPolicy.OpenExternal(NavigationPolicy.Unwrap(e.Uri));

        // A tab that only existed to show that page has nothing left to show.
        if (Core?.Source is null or "" or "about:blank")
            CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true; // never open real popup windows
        var target = NavigationPolicy.Unwrap(e.Uri);
        if (NavigationPolicy.IsAllowed(target)) OpenInNewTab?.Invoke(this, target);
        else NavigationPolicy.OpenExternal(target);
    }

    async Task UpdateFaviconAsync()
    {
        try
        {
            if (Core is null) return;
            using var stream = await Core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream is null) return;
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            if (ms.Length == 0) return;
            ms.Position = 0;
            using var img = Image.FromStream(ms);
            var old = Favicon;
            Favicon = new Bitmap(img, 16, 16);
            old?.Dispose();
            Raise();
        }
        catch { /* favicon is cosmetic */ }
    }

    [GeneratedRegex(@"^\(\d+\)\s*")]
    private static partial Regex NotificationCount();

    static string CleanTitle(string title)
    {
        title = NotificationCount().Replace(title ?? "", "");
        if (title.EndsWith(" - YouTube", StringComparison.Ordinal)) title = title[..^10];
        return string.IsNullOrWhiteSpace(title) ? "YouTube" : title;
    }

    void Raise() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        View.Dispose();
        Favicon?.Dispose();
    }
}
