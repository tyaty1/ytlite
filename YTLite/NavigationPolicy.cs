using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Web;

namespace YTLite;

/// <summary>Top-level pages are limited to YouTube and Google sign-in; everything else opens in the system browser.</summary>
static partial class NavigationPolicy
{
    static readonly string[] AllowedHosts =
    {
        "youtube.com", "youtu.be", "youtube-nocookie.com",
        "accounts.google.com", "accounts.youtube.com",
        "consent.youtube.com", "consent.google.com",
        "myaccount.google.com", "gds.google.com",
    };

    // Paths on www.google.com that Google's sign-in flow can pass through.
    static readonly string[] GoogleSignInPaths = { "/accounts", "/ServiceLogin", "/signin", "/sorry", "/recaptcha" };

    // Country sign-in hosts such as accounts.google.hu or accounts.google.co.uk.
    [GeneratedRegex(@"^accounts\.google\.(com?\.)?[a-z]{2,3}$")]
    private static partial Regex CountryAccountsHost();

    public static bool IsAllowed(string url)
    {
        if (url == "about:blank") return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) return false;

        var host = u.IdnHost.ToLowerInvariant();
        foreach (var allowed in AllowedHosts)
            if (host == allowed || host.EndsWith("." + allowed, StringComparison.Ordinal))
                return true;

        if (CountryAccountsHost().IsMatch(host)) return true;

        if (host is "google.com" or "www.google.com")
            foreach (var p in GoogleSignInPaths)
                if (u.AbsolutePath.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return true;

        return false;
    }

    /// <summary>youtube.com/redirect?q=TARGET → TARGET (used for links in video descriptions).</summary>
    public static string Unwrap(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u)
            && u.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase)
            && u.AbsolutePath == "/redirect")
        {
            var q = HttpUtility.ParseQueryString(u.Query)["q"];
            if (!string.IsNullOrEmpty(q)) return q;
        }
        return url;
    }

    public static void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
        if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) return;
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
        catch { /* no handler registered */ }
    }
}
