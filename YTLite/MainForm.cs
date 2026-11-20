using System.Diagnostics;
using System.Text;

namespace YTLite;

sealed class MainForm : Form
{
    readonly List<BrowserTab> _all = new();
    readonly ToolStrip _bar = new()
    {
        GripStyle = ToolStripGripStyle.Hidden,
        Dock = DockStyle.Top,
        Padding = new Padding(4, 2, 4, 2),
        RenderMode = ToolStripRenderMode.System,
        CanOverflow = false,
    };
    readonly TabStrip _strip;
    readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 15) };
    readonly ToolStripTextBox _search = new()
    {
        AutoSize = false,
        Width = 420,
        ToolTipText = "Search YouTube or paste a YouTube link (Ctrl+L)",
    };
    readonly ToolStripDropDownButton _camera = new("📷 Frame") { ToolTipText = "Video screenshot (Ctrl+Shift+S)" };
    readonly ToolStripDropDownButton _menu = new("⋮") { ShowDropDownArrow = false, ToolTipText = "Menu" };
    readonly ToolStripMenuItem _mobileItem = new("Use lighter mobile YouTube (m.youtube.com)") { CheckOnClick = true };
    readonly Label _toast = new()
    {
        Dock = DockStyle.Bottom,
        Height = 30,
        Visible = false,
        TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Color.FromArgb(40, 40, 40),
        ForeColor = Color.White,
    };
    readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 6000 };
    readonly TabSuspender _suspender;
    readonly string? _selfTestOut;

    BrowserTab? _current;
    bool _fullScreen;
    FormWindowState _prevState;
    FormBorderStyle _prevBorder;
    bool _busyGrabbing;

    public BrowserTab? Current => _current;
    public IReadOnlyList<BrowserTab> Tabs => _all;

    readonly string? _layoutOut;

    public MainForm(string? selfTestOut, string? layoutOut = null)
    {
        _selfTestOut = selfTestOut;
        _layoutOut = layoutOut;
        Text = "YTLite";
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("YTLite.app.ico"))
            if (iconStream is not null) Icon = new Icon(iconStream);
        Size = new Size(1400, 900);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _strip = new TabStrip(_all);
        _strip.TabClicked += (_, t) => Select(t);
        _strip.CloseClicked += (_, t) => CloseTab(t);
        _strip.NewTabClicked += (_, _) => NewTab(AppSettings.Current.HomeUrl);

        BuildToolbar();

        // Dock order: Fill first, then the edges (last added docks first).
        Controls.Add(_content);
        Controls.Add(_toast);
        Controls.Add(_strip);
        Controls.Add(_bar);

        _toastTimer.Tick += (_, _) => { _toast.Visible = false; _toastTimer.Stop(); };
        _suspender = new TabSuspender(() => _all, () => _current, () => _strip.Invalidate());

        Load += async (_, _) =>
        {
            if (_selfTestOut is not null) SelfTest.Prepare();
            NewTab(_layoutOut is null ? AppSettings.Current.HomeUrl : "about:blank");
            LayoutSearchBox();
            if (_selfTestOut is not null) await SelfTest.RunAsync(this, _selfTestOut);
            if (_layoutOut is not null) await LayoutTest.RunAsync(this, _layoutOut);
        };
        Resize += (_, _) => LayoutSearchBox();
    }

    // ---------------------------------------------------------------- toolbar

    void BuildToolbar()
    {
        ToolStripButton Btn(string text, string tip, Action action)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, AutoToolTip = false, DisplayStyle = ToolStripItemDisplayStyle.Text };
            b.Click += (_, _) => action();
            return b;
        }

        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Go(_search.Text);
        };

        _camera.DropDownItems.Add("Search with Google Lens", null, async (_, _) => await ScreenshotAsync(ShotAction.Lens));
        _camera.DropDownItems.Add("Search with Bing Visual Search", null, async (_, _) => await ScreenshotAsync(ShotAction.Bing));
        _camera.DropDownItems.Add(new ToolStripSeparator());
        _camera.DropDownItems.Add("Save as JPEG 2000…", null, async (_, _) => await ScreenshotAsync(ShotAction.SaveJp2));
        _camera.DropDownItems.Add("Copy frame to clipboard", null, async (_, _) => await ScreenshotAsync(ShotAction.Copy));

        _mobileItem.Checked = AppSettings.Current.Mobile;
        _mobileItem.CheckedChanged += (_, _) =>
        {
            AppSettings.Current.UiMode = _mobileItem.Checked ? "mobile" : "desktop";
            AppSettings.Current.Save();
            // persist_app makes YouTube remember the choice in its own cookie too.
            Current?.Navigate(_mobileItem.Checked
                ? "https://m.youtube.com/?app=m&persist_app=1"
                : "https://www.youtube.com/?app=desktop&persist_app=1");
        };
        _menu.DropDownItems.Add(_mobileItem);
        var desktopLayout = new ToolStripMenuItem("Desktop layout for mobile YouTube")
        {
            CheckOnClick = true,
            Checked = AppSettings.Current.MobileDesktopLayout,
        };
        desktopLayout.CheckedChanged += (_, _) =>
        {
            AppSettings.Current.MobileDesktopLayout = desktopLayout.Checked;
            AppSettings.Current.Save();
            Toast("Desktop layout setting saved. It applies to tabs you open from now on (Ctrl+T).");
        };
        _menu.DropDownItems.Add(desktopLayout);
        _menu.DropDownItems.Add(new ToolStripSeparator());
        _menu.DropDownItems.Add("Processes and memory…", null, async (_, _) => await ShowProcessInfoAsync());
        _menu.DropDownItems.Add("Ad blocking status…", null, (_, _) => ShowAdBlockStatus());
        _menu.DropDownItems.Add("Open screenshots folder", null, (_, _) => OpenFolder(Jp2Exporter.DefaultFolder));
        _menu.DropDownItems.Add("Open settings folder", null, (_, _) => OpenFolder(AppSettings.DataDir));

        _bar.Items.AddRange(new ToolStripItem[]
        {
            Btn("◀", "Back (Alt+Left)", () => Current?.Core?.GoBack()),
            Btn("▶", "Forward (Alt+Right)", () => Current?.Core?.GoForward()),
            Btn("⟳", "Reload (F5)", () => Current?.Core?.Reload()),
            Btn("⌂", "YouTube home", () => Current?.Navigate(AppSettings.Current.HomeUrl)),
            new ToolStripSeparator(),
            _search,
            Btn("Search", "Search YouTube", () => Go(_search.Text)),
            new ToolStripSeparator(),
            _camera,
            _menu,
        });
    }

    void LayoutSearchBox()
    {
        int others = 0;
        foreach (ToolStripItem item in _bar.Items)
            if (item != _search) others += item.Width + item.Margin.Horizontal;
        _search.Width = Math.Clamp(_bar.ClientSize.Width - others - 30, 160, 900);
    }

    void Go(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;

        string url;
        if (Uri.TryCreate(text, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
            url = u.AbsoluteUri;
        else if (text.StartsWith("youtu", StringComparison.OrdinalIgnoreCase) || text.StartsWith("www.youtu", StringComparison.OrdinalIgnoreCase)
                 || text.StartsWith("m.youtu", StringComparison.OrdinalIgnoreCase))
            url = "https://" + text;
        else
            url = AppSettings.Current.SearchUrl(text);

        if (!NavigationPolicy.IsAllowed(url))
        {
            NavigationPolicy.OpenExternal(url);
            return;
        }

        if (Current is null) NewTab(url);
        else Current.Navigate(url);
        Current?.View.Focus();
    }

    // ---------------------------------------------------------------- tabs

    public BrowserTab NewTab(string url, bool select = true, BrowserTab? after = null)
    {
        var tab = new BrowserTab { ShortcutPressed = TryShortcut };
        tab.StateChanged += (_, _) =>
        {
            _strip.Invalidate();
            if (tab == _current) Text = $"{tab.Title} – YTLite";
        };
        tab.FullScreenChanged += (_, _) => { if (tab == _current) SetFullScreen(tab.IsFullScreen); };
        tab.OpenInNewTab += (_, target) => NewTab(target, select: false, after: tab);
        tab.CloseRequested += (_, _) => BeginInvoke(() => CloseTab(tab));

        // Links open right after the tab they came from; Ctrl+T tabs go at the end.
        int index = after is null ? _all.Count : _all.IndexOf(after) + 1;
        _all.Insert(Math.Clamp(index, 0, _all.Count), tab);

        tab.View.Visible = false;
        _content.Controls.Add(tab.View);
        tab.Start(url);
        tab.Ready.ContinueWith(t =>
        {
            if (t.IsFaulted) BeginInvoke(() => ShowError("Could not start the browser engine", t.Exception!.GetBaseException()));
        }, TaskScheduler.Default);

        if (select || _current is null) Select(tab);
        else _strip.Invalidate();
        return tab;
    }

    public void Select(BrowserTab tab)
    {
        if (tab == _current) return;
        if (_fullScreen) ExitPageFullScreen();

        var previous = _current;
        _current = tab;
        tab.View.Visible = true;
        tab.View.BringToFront();
        TabSuspender.Wake(tab);
        if (previous is not null)
        {
            previous.View.Visible = false; // WebView2 IsVisible=false → eligible for suspension
            previous.HiddenSince = DateTime.UtcNow;
        }

        _strip.Selected = tab;
        _strip.Invalidate();
        Text = $"{tab.Title} – YTLite";
        if (!_search.Focused) tab.View.Focus();
    }

    public void CloseTab(BrowserTab tab)
    {
        int i = _all.IndexOf(tab);
        if (i < 0) return;
        if (tab == _current && _fullScreen) SetFullScreen(false);

        _all.RemoveAt(i);
        _content.Controls.Remove(tab.View);
        bool wasCurrent = tab == _current;
        if (wasCurrent) _current = null;
        tab.Dispose();

        if (_all.Count == 0)
        {
            if (_selfTestOut is null) Close();
            return;
        }
        if (wasCurrent) Select(_all[Math.Min(i, _all.Count - 1)]);
        _strip.Invalidate();
    }

    void Cycle(int delta)
    {
        if (_all.Count < 2 || _current is null) return;
        int i = (_all.IndexOf(_current) + delta + _all.Count) % _all.Count;
        Select(_all[i]);
    }

    // ---------------------------------------------------------------- full screen

    void SetFullScreen(bool on)
    {
        if (on == _fullScreen) return;
        _fullScreen = on;
        SuspendLayout();
        if (on)
        {
            _prevState = WindowState;
            _prevBorder = FormBorderStyle;
            _bar.Visible = _strip.Visible = false;
            FormBorderStyle = FormBorderStyle.None;
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = _prevBorder;
            WindowState = _prevState;
            _bar.Visible = _strip.Visible = true;
        }
        ResumeLayout();
    }

    void ExitPageFullScreen()
    {
        _ = _current?.Core?.ExecuteScriptAsync("document.fullscreenElement && document.exitFullscreen()");
        SetFullScreen(false);
    }

    // ---------------------------------------------------------------- keyboard

    // Focus in the toolbar/tab strip → ProcessCmdKey. Focus in a page → BrowserTab.ShortcutPressed.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        TryShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>Recognizes a shortcut and runs it on the next message-loop turn (safe inside WebView2 events, e.g. Ctrl+W disposing the tab).</summary>
    public bool TryShortcut(Keys keyData)
    {
        Action? action = keyData switch
        {
            Keys.Control | Keys.T => () => NewTab(AppSettings.Current.HomeUrl),
            Keys.Control | Keys.W => () => { if (_current is not null) CloseTab(_current); },
            Keys.Control | Keys.Tab => () => Cycle(1),
            Keys.Control | Keys.Shift | Keys.Tab => () => Cycle(-1),
            Keys.Control | Keys.L => () => { _search.Focus(); _search.SelectAll(); },
            Keys.Control | Keys.Shift | Keys.S => () =>
            {
                if (_fullScreen) _camera.DropDown.Show(Cursor.Position);
                else _camera.ShowDropDown();
            },
            Keys.F5 => () => _current?.Core?.Reload(),
            Keys.Alt | Keys.Left => () => _current?.Core?.GoBack(),
            Keys.Alt | Keys.Right => () => _current?.Core?.GoForward(),
            _ => null,
        };
        if (action is null) return false;
        BeginInvoke(action);
        return true;
    }

    // ---------------------------------------------------------------- screenshots

    enum ShotAction { Lens, Bing, SaveJp2, Copy }

    async Task ScreenshotAsync(ShotAction action)
    {
        var core = _current?.Core;
        if (core is null || _busyGrabbing) return;
        _busyGrabbing = true;
        try
        {
            var grab = await FrameGrabber.GrabAsync(core);
            switch (action)
            {
                case ShotAction.Lens:
                    Toast(await VisualSearchLauncher.SearchAsync(grab, AppSettings.Current.GoogleLens));
                    break;
                case ShotAction.Bing:
                    Toast(await VisualSearchLauncher.SearchAsync(grab, AppSettings.Current.Bing));
                    break;
                case ShotAction.Copy:
                    VisualSearchLauncher.CopyToClipboard(grab);
                    Toast($"Frame copied to the clipboard ({grab.Width}×{grab.Height}).");
                    break;
                case ShotAction.SaveJp2:
                    await SaveJp2Async(grab);
                    break;
            }
        }
        catch (Exception ex)
        {
            Toast("Screenshot failed: " + ex.Message);
        }
        finally { _busyGrabbing = false; }
    }

    async Task SaveJp2Async(FrameGrab grab)
    {
        var suggested = Jp2Exporter.SuggestPath(grab);
        string path;
        if (_fullScreen)
        {
            path = suggested; // no dialog over a full-screen video
        }
        else
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Save frame as JPEG 2000",
                Filter = "JPEG 2000 image (*.jp2)|*.jp2",
                InitialDirectory = Path.GetDirectoryName(suggested),
                FileName = Path.GetFileName(suggested),
                AddExtension = true,
                DefaultExt = "jp2",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
        }

        var s = AppSettings.Current;
        Toast("Encoding JPEG 2000…");
        await Task.Run(() => Jp2Exporter.Save(grab, path, s.Jp2Lossless, s.Jp2Quality));
        Toast($"Saved {grab.Width}×{grab.Height} {(s.Jp2Lossless ? "lossless" : $"q{s.Jp2Quality}")} JPEG 2000: {path}");
    }

    // ---------------------------------------------------------------- info dialogs

    public async Task<string> DescribeProcessesAsync()
    {
        var env = await BrowserEnvironment.GetAsync();
        var infos = await env.GetProcessExtendedInfosAsync();
        var sb = new StringBuilder();
        long total = 0;
        int renderers = 0;

        long own = Process.GetCurrentProcess().PrivateMemorySize64;
        total += own;
        sb.AppendLine($"{"YTLite (host)",-14} pid {Environment.ProcessId,-6} {own / 1048576,6} MB");

        foreach (var info in infos)
        {
            var p = info.ProcessInfo;
            long mem = 0;
            try { using var proc = Process.GetProcessById(p.ProcessId); mem = proc.PrivateMemorySize64; } catch { }
            total += mem;
            if (p.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ProcessKind.Renderer) renderers++;
            sb.AppendLine($"{p.Kind,-14} pid {p.ProcessId,-6} {mem / 1048576,6} MB");
            foreach (var f in info.AssociatedFrameInfos)
                sb.AppendLine($"      frame: {Shorten(f.Source, 90)}");
        }

        sb.Insert(0, $"Tabs: {_all.Count}   Renderer processes: {renderers}   Total private memory: {total / 1048576} MB\r\n" +
                     $"Browser arguments: {BrowserEnvironment.Arguments}\r\n\r\n");
        return sb.ToString();
    }

    static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    async Task ShowProcessInfoAsync()
    {
        try { ShowText("Processes and memory", await DescribeProcessesAsync()); }
        catch (Exception ex) { ShowError("Could not read process info", ex); }
    }

    void ShowAdBlockStatus() => ShowText("Ad blocking",
        $"uBlock Origin Lite: {BrowserEnvironment.ExtensionStatus}\r\n" +
        $"Built-in blocker: on (requests blocked this session: {AdBlocker.BlockedCount})\r\n" +
        $"Rules: {AdBlocker.RulesPath}\r\nScript: {AdBlocker.ScriptPath}\r\n\r\n" +
        "Rules and script are read when a tab opens; edit them and open a new tab (or restart) to apply.");

    void ShowText(string title, string text)
    {
        using var f = new Form
        {
            Text = title,
            Size = new Size(900, 560),
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
        };
        f.Controls.Add(new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9.5f),
            Text = text,
        });
        f.ShowDialog(this);
    }

    void ShowError(string what, Exception ex) =>
        MessageBox.Show(this, $"{what}:\r\n\r\n{ex.Message}", "YTLite", MessageBoxButtons.OK, MessageBoxIcon.Error);

    public void Toast(string message)
    {
        _toast.Text = message;
        _toast.Visible = true;
        _toast.BringToFront();
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    static void OpenFolder(string dir)
    {
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _suspender.Dispose();
            _toastTimer.Dispose();
            foreach (var t in _all) t.Dispose();
        }
        base.Dispose(disposing);
    }
}
