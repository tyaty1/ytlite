package app.ytlite;

import android.content.Context;
import android.webkit.WebResourceResponse;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.regex.Pattern;

/**
 * Loads the web code shared with the Windows app (bundled as assets from YTLite/AdBlock and
 * YTLite/Styles): ad-request patterns, the ad-block page script and the desktop CSS for
 * m.youtube.com, which only takes effect on wide screens such as tablets in landscape.
 */
final class WebScripts {
    private final List<Pattern> blockPatterns = new ArrayList<>();
    final String adblockScript;
    final String desktopStyleScript;
    volatile int blockedCount;

    WebScripts(Context ctx) {
        String rulesText = readAsset(ctx, "adblock-rules.json");
        String adblockJs = readAsset(ctx, "adblock.js");
        String css = readAsset(ctx, "mobile-desktop.css");

        JSONArray prune = new JSONArray(), hide = new JSONArray();
        try {
            // The rules file allows // comment lines (the Windows parser skips them); org.json doesn't.
            JSONObject rules = new JSONObject(rulesText.replaceAll("(?m)^\\s*//.*$", ""));
            JSONArray patterns = rules.optJSONArray("blockUrlPatterns");
            if (patterns != null)
                for (int i = 0; i < patterns.length(); i++)
                    blockPatterns.add(wildcardToRegex(patterns.getString(i)));
            if (rules.optJSONArray("prunePaths") != null) prune = rules.getJSONArray("prunePaths");
            if (rules.optJSONArray("hideSelectors") != null) hide = rules.getJSONArray("hideSelectors");
        } catch (Exception ignored) {
            // Broken rules file: no request blocking, but the app still works.
        }

        String config;
        try {
            config = new JSONObject().put("prunePaths", prune).put("hideSelectors", hide).toString();
        } catch (Exception e) {
            config = "{}";
        }
        adblockScript = adblockJs.isEmpty() ? "" : "window.__ytliteRules = " + config + ";\n" + adblockJs;

        desktopStyleScript = css.isEmpty() ? "" :
                "(function () {\n" +
                "  if (location.hostname !== 'm.youtube.com' || window.top !== window) return;\n" +
                "  var css = " + JSONObject.quote(css) + ";\n" +
                "  var place = function () {\n" +
                "    var s = document.getElementById('ytlite-desktop');\n" +
                "    if (!s) { s = document.createElement('style'); s.id = 'ytlite-desktop'; s.textContent = css; }\n" +
                "    (document.head || document.documentElement).appendChild(s);\n" +
                "  };\n" +
                "  if (document.documentElement) place();\n" +
                "  document.addEventListener('DOMContentLoaded', place);\n" +
                "  window.addEventListener('load', place);\n" +
                "})();";
    }

    /** "*://*.doubleclick.net/*" → regex over the whole URL (same wildcard semantics as WebView2 filters). */
    private static Pattern wildcardToRegex(String wildcard) {
        StringBuilder sb = new StringBuilder("^");
        for (String part : wildcard.split("\\*", -1)) {
            if (sb.length() > 1) sb.append(".*");
            sb.append(Pattern.quote(part));
        }
        return Pattern.compile(sb.append('$').toString());
    }

    boolean shouldBlock(String url) {
        for (Pattern p : blockPatterns)
            if (p.matcher(url).matches()) {
                blockedCount++;
                return true;
            }
        return false;
    }

    static WebResourceResponse emptyResponse() {
        return new WebResourceResponse("text/plain", "utf-8", new ByteArrayInputStream(new byte[0]));
    }

    private static String readAsset(Context ctx, String name) {
        try (InputStream in = ctx.getAssets().open(name)) {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int n;
            while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
            return out.toString("UTF-8");
        } catch (IOException e) {
            return "";
        }
    }
}
