namespace YTLite;

static class Program
{
    /// <summary>
    /// YTLite.exe                       normal start
    /// YTLite.exe --selftest out.txt    open test videos, check processes/ads/quality/screenshots, write a report, exit
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string? selfTestOut = null;
        int i = Array.IndexOf(args, "--selftest");
        if (i >= 0)
            selfTestOut = i + 1 < args.Length && !args[i + 1].StartsWith("--")
                ? Path.GetFullPath(args[i + 1])
                : Path.Combine(AppSettings.DataDir, "selftest.txt");
        // --stock: compare against Chromium's normal process model (self-test only).
        BrowserEnvironment.StockMode = selfTestOut is not null && args.Contains("--stock");
        // --no-adblock: both ad-blocking layers off (self-test only, to compare playback).
        AdBlocker.Disabled = selfTestOut is not null && args.Contains("--no-adblock");

        // --layouttest OUTDIR [--no-style] [--only search,watch]: screenshots of mobile YouTube at target window sizes.
        string? layoutOut = null;
        int l = Array.IndexOf(args, "--layouttest");
        if (l >= 0 && l + 1 < args.Length)
        {
            layoutOut = Path.GetFullPath(args[l + 1]);
            DesktopStyle.Disabled = args.Contains("--no-style");
            int o = Array.IndexOf(args, "--only");
            if (o >= 0 && o + 1 < args.Length) LayoutTest.Only = args[o + 1].Split(',');
            int z = Array.IndexOf(args, "--sizes");
            if (z >= 0 && z + 1 < args.Length) LayoutTest.OnlySizes = args[z + 1].Split(',');
        }

        Application.Run(new MainForm(selfTestOut, layoutOut));
    }
}
