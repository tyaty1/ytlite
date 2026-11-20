package app.ytlite;

import android.net.Uri;

import java.util.Locale;
import java.util.regex.Pattern;

/** Same rules as the Windows app: top-level pages are YouTube and Google sign-in only. */
final class NavigationPolicy {
    private static final String[] ALLOWED_HOSTS = {
            "youtube.com", "youtu.be", "youtube-nocookie.com",
            "accounts.google.com", "accounts.youtube.com",
            "consent.youtube.com", "consent.google.com",
            "myaccount.google.com", "gds.google.com",
    };
    private static final String[] GOOGLE_SIGN_IN_PATHS = {"/accounts", "/ServiceLogin", "/signin", "/sorry", "/recaptcha"};
    private static final Pattern COUNTRY_ACCOUNTS_HOST = Pattern.compile("^accounts\\.google\\.(com?\\.)?[a-z]{2,3}$");

    private NavigationPolicy() { }

    static boolean isAllowed(String url) {
        if (url == null) return false;
        if (url.equals("about:blank")) return true;
        Uri u = Uri.parse(url);
        String scheme = u.getScheme();
        if (!"https".equals(scheme) && !"http".equals(scheme)) return false;
        String host = u.getHost() == null ? "" : u.getHost().toLowerCase(Locale.ROOT);

        for (String allowed : ALLOWED_HOSTS)
            if (host.equals(allowed) || host.endsWith("." + allowed)) return true;
        if (COUNTRY_ACCOUNTS_HOST.matcher(host).matches()) return true;

        if (host.equals("google.com") || host.equals("www.google.com")) {
            String path = u.getPath() == null ? "" : u.getPath();
            for (String p : GOOGLE_SIGN_IN_PATHS)
                if (path.regionMatches(true, 0, p, 0, p.length())) return true;
        }
        return false;
    }

    /** youtube.com/redirect?q=TARGET → TARGET (links in video descriptions). */
    static String unwrap(String url) {
        Uri u = Uri.parse(url);
        String host = u.getHost() == null ? "" : u.getHost().toLowerCase(Locale.ROOT);
        if (host.endsWith("youtube.com") && "/redirect".equals(u.getPath())) {
            String q = u.getQueryParameter("q");
            if (q != null && !q.isEmpty()) return q;
        }
        return url;
    }

    static boolean isWebUrl(String url) {
        String s = Uri.parse(url).getScheme();
        return "https".equals(s) || "http".equals(s);
    }
}
