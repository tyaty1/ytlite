using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using ImageMagick;
using Microsoft.Win32;

namespace YTLite;

/// <summary>
/// Sends a frame to Google Lens / Bing Visual Search in the user's regular browser: a temporary
/// local page auto-submits the image to the service's upload endpoint. If the default browser
/// can't be found (or VisualSearchMode = "clipboard"), the frame is copied to the clipboard and
/// the service's page is opened for Ctrl+V.
/// </summary>
static class VisualSearchLauncher
{
    const int MaxSide = 2000;
    static string TempDir => Path.Combine(Path.GetTempPath(), "YTLite");

    public static async Task<string> SearchAsync(FrameGrab grab, VisualSearchEngine engine)
    {
        CleanupOldTempFiles();
        var (jpeg, w, h) = await Task.Run(() => MakeJpeg(grab.Png));

        if (!string.Equals(AppSettings.Current.VisualSearchMode, "clipboard", StringComparison.OrdinalIgnoreCase)
            && DefaultBrowser.TryGet(out var browser))
        {
            Directory.CreateDirectory(TempDir);
            var file = Path.Combine(TempDir, $"search-{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(file, BuildUploadPage(engine, jpeg, w, h));
            browser.Open(new Uri(file).AbsoluteUri);
            _ = DeleteLaterAsync(file);
            return $"Sending the frame to {engine.Name} in your browser…";
        }

        SetClipboardJpeg(jpeg);
        NavigationPolicy.OpenExternal(engine.PasteUrl);
        return $"Frame copied. Press Ctrl+V on the {engine.Name} page to search.";
    }

    public static void CopyToClipboard(FrameGrab grab)
    {
        using var ms = new MemoryStream(grab.Png);
        using var img = Image.FromStream(ms);
        Clipboard.SetImage(new Bitmap(img));
    }

    static void SetClipboardJpeg(byte[] jpeg)
    {
        using var ms = new MemoryStream(jpeg);
        using var img = Image.FromStream(ms);
        Clipboard.SetImage(new Bitmap(img));
    }

    internal static (byte[] Jpeg, int Width, int Height) MakeJpeg(byte[] png)
    {
        using var image = new MagickImage(png);
        image.Alpha(AlphaAction.Off);
        if (image.Width > MaxSide || image.Height > MaxSide)
            image.Resize(new MagickGeometry(MaxSide, MaxSide)); // keeps aspect ratio
        image.Quality = 90;
        return (image.ToByteArray(MagickFormat.Jpeg), (int)image.Width, (int)image.Height);
    }

    internal static string BuildUploadPage(VisualSearchEngine engine, byte[] jpeg, int w, int h)
    {
        string ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string Fill(string s) => s.Replace("{ts}", ts).Replace("{w}", w.ToString(CultureInfo.InvariantCulture))
                                  .Replace("{h}", h.ToString(CultureInfo.InvariantCulture));
        static string Enc(string s) => WebUtility.HtmlEncode(s);

        var b64 = Convert.ToBase64String(jpeg);
        bool asFile = !string.Equals(engine.FieldKind, "base64", StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder(b64.Length + 2048);
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>Sending frame to ")
          .Append(Enc(engine.Name)).Append("…</title></head>")
          .Append("<body style=\"font:16px system-ui,sans-serif;background:#111;color:#eee;padding:2em\">")
          .Append("<p>Sending the video frame to ").Append(Enc(engine.Name)).Append("…</p>")
          .Append("<form id=\"f\" method=\"post\" enctype=\"multipart/form-data\" action=\"")
          .Append(Enc(Fill(engine.UploadUrl))).Append("\">");
        foreach (var (name, value) in engine.ExtraFields)
            sb.Append("<input type=\"hidden\" name=\"").Append(Enc(name)).Append("\" value=\"").Append(Enc(Fill(value))).Append("\">");
        if (asFile)
            sb.Append("<input type=\"file\" id=\"img\" hidden name=\"").Append(Enc(engine.Field)).Append("\">");
        else
            sb.Append("<input type=\"hidden\" name=\"").Append(Enc(engine.Field)).Append("\" value=\"").Append(b64).Append("\">");
        sb.Append("</form><script>\n");
        if (asFile)
            sb.Append("const b=atob('").Append(b64).Append("');const u=new Uint8Array(b.length);")
              .Append("for(let i=0;i<b.length;i++)u[i]=b.charCodeAt(i);const dt=new DataTransfer();")
              .Append("dt.items.add(new File([u],'frame.jpg',{type:'image/jpeg'}));")
              .Append("document.getElementById('img').files=dt.files;\n");
        sb.Append("document.getElementById('f').submit();\n</script></body></html>");
        return sb.ToString();
    }

    static async Task DeleteLaterAsync(string file)
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        try { File.Delete(file); } catch { }
    }

    static void CleanupOldTempFiles()
    {
        try
        {
            if (!Directory.Exists(TempDir)) return;
            foreach (var f in Directory.EnumerateFiles(TempDir, "search-*.html"))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddMinutes(-2))
                    File.Delete(f);
        }
        catch { }
    }
}

/// <summary>The user's default https browser, read from the Windows URL association.</summary>
sealed class DefaultBrowser
{
    public string Exe { get; private init; } = "";
    public string ArgsTemplate { get; private init; } = "%1";

    public static bool TryGet([NotNullWhen(true)] out DefaultBrowser? browser)
    {
        browser = null;
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            if (choice?.GetValue("ProgId") is not string progId || progId.Length == 0) return false;

            using var command = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            var cmd = (command?.GetValue(null) as string)?.Trim();
            if (string.IsNullOrEmpty(cmd)) return false;

            string exe, rest;
            if (cmd.StartsWith('"'))
            {
                int end = cmd.IndexOf('"', 1);
                if (end < 0) return false;
                exe = cmd[1..end];
                rest = cmd[(end + 1)..].Trim();
            }
            else
            {
                int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return false;
                exe = cmd[..(i + 4)];
                rest = cmd[(i + 4)..].Trim();
            }

            exe = Environment.ExpandEnvironmentVariables(exe);
            if (!File.Exists(exe)) return false;

            browser = new DefaultBrowser
            {
                Exe = exe,
                ArgsTemplate = rest.Contains("%1") ? rest : (rest + " %1").Trim(),
            };
            return true;
        }
        catch { return false; }
    }

    public void Open(string url) =>
        Process.Start(new ProcessStartInfo(Exe, ArgsTemplate.Replace("%1", url)) { UseShellExecute = false });
}
