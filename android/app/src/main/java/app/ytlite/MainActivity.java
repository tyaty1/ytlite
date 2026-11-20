package app.ytlite;

import android.annotation.SuppressLint;
import android.annotation.TargetApi;
import android.app.Activity;
import android.app.AlertDialog;
import android.app.UiModeManager;
import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ActivityInfo;
import android.content.res.Configuration;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Message;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.webkit.CookieManager;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.ArrayAdapter;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.TextView;
import android.widget.Toast;

import androidx.webkit.WebViewCompat;
import androidx.webkit.WebViewFeature;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * One activity, two modes:
 *  - TV (Android TV / Google TV): YouTube's own remote-friendly UI at youtube.com/tv, full screen.
 *  - Phone / tablet: m.youtube.com with tabs, plus the desktop layout on wide screens.
 * Both use the system WebView, the shared ad-blocking code and the same navigation lock.
 */
public class MainActivity extends Activity {

    /** Smart-TV user agent so YouTube serves its TV interface; the engine underneath is normal Chromium. */
    static final String TV_USER_AGENT =
            "Mozilla/5.0 (SMART-TV; LINUX; Tizen 6.0) AppleWebKit/537.36 (KHTML, like Gecko) 76.0.3809.146/6.0 TV Safari/537.36";
    static final String TV_HOME = "https://www.youtube.com/tv";
    static final String PHONE_HOME = "https://m.youtube.com/?app=m&persist_app=1";
    /** Below this Chromium version current YouTube is not expected to work. */
    static final int MIN_CHROMIUM = 80;
    private static final Set<String> SCRIPT_ORIGINS = Collections.singleton("https://*.youtube.com");

    private boolean tv;
    private WebScripts scripts;
    private boolean documentStartScripts;

    private final List<WebView> tabs = new ArrayList<>();
    private WebView current;
    private FrameLayout content;
    private LinearLayout bar;
    private TextView tabCount;

    private View customView;
    private WebChromeClient.CustomViewCallback customViewCallback;
    private int orientationBeforeFullscreen;
    private long lastBackPress;

    // ------------------------------------------------------------------ lifecycle

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        UiModeManager ui = (UiModeManager) getSystemService(Context.UI_MODE_SERVICE);
        tv = ui != null && ui.getCurrentModeType() == Configuration.UI_MODE_TYPE_TELEVISION;
        scripts = new WebScripts(this);
        documentStartScripts = WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT);
        // Debug builds: allow inspecting pages over adb (chrome://inspect or the test scripts).
        if ((getApplicationInfo().flags & android.content.pm.ApplicationInfo.FLAG_DEBUGGABLE) != 0)
            WebView.setWebContentsDebuggingEnabled(true);

        buildLayout();
        if (tv) getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        String start = tv ? TV_HOME : PHONE_HOME;
        String linked = linkFromIntent(getIntent());
        if (linked != null) start = linked;
        newTab(start, true);

        warnIfWebViewTooOld();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        String linked = linkFromIntent(intent);
        if (linked == null) return;
        if (tv) current.loadUrl(linked);
        else newTab(linked, true);
    }

    private static String linkFromIntent(Intent intent) {
        Uri data = intent == null ? null : intent.getData();
        if (data == null) return null;
        String url = data.toString();
        return NavigationPolicy.isAllowed(url) ? url : null;
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (current != null) current.onResume();
    }

    @Override
    protected void onPause() {
        if (current != null) current.onPause();
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        for (WebView w : tabs) {
            content.removeView(w);
            w.destroy();
        }
        tabs.clear();
        super.onDestroy();
    }

    // ------------------------------------------------------------------ layout

    private void buildLayout() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.BLACK);

        bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        bar.setBackgroundColor(Color.rgb(15, 15, 15));
        bar.addView(barButton("‹", v -> { if (current != null && current.canGoBack()) current.goBack(); }));
        bar.addView(barButton("↻", v -> { if (current != null) current.reload(); }));
        View spacer = new View(this);
        bar.addView(spacer, new LinearLayout.LayoutParams(0, 1, 1f));
        tabCount = barButton("1", v -> showTabSwitcher());
        GradientDrawable box = new GradientDrawable();
        box.setStroke(dp(2), Color.WHITE);
        box.setCornerRadius(dp(4));
        tabCount.setBackground(box);
        tabCount.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        LinearLayout.LayoutParams boxLp = new LinearLayout.LayoutParams(dp(28), dp(26));
        boxLp.setMargins(dp(10), 0, dp(10), 0);
        bar.addView(tabCount, boxLp);
        bar.addView(barButton("+", v -> newTab(PHONE_HOME, true)));
        root.addView(bar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(44)));
        if (tv) bar.setVisibility(View.GONE);

        content = new FrameLayout(this);
        root.addView(content, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        setContentView(root);
    }

    private TextView barButton(String text, View.OnClickListener click) {
        TextView t = new TextView(this);
        t.setText(text);
        t.setTextColor(Color.WHITE);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22);
        t.setGravity(Gravity.CENTER);
        t.setMinWidth(dp(48));
        t.setOnClickListener(click);
        return t;
    }

    private int dp(int v) {
        return Math.round(TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v, getResources().getDisplayMetrics()));
    }

    // ------------------------------------------------------------------ tabs

    private WebView newTab(String url, boolean select) {
        WebView w = createWebView();
        tabs.add(w);
        content.addView(w, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        w.setVisibility(View.GONE);
        if (select || current == null) select(w);
        if (url != null) w.loadUrl(url);
        updateTabCount();
        return w;
    }

    private void select(WebView w) {
        if (current == w) return;
        if (current != null) {
            current.onPause();          // hidden tabs pause animations etc.; they share one renderer process
            current.setVisibility(View.GONE);
        }
        current = w;
        w.setVisibility(View.VISIBLE);
        w.onResume();
        w.requestFocus();
        updateTabCount();
    }

    private void closeTab(WebView w) {
        int i = tabs.indexOf(w);
        if (i < 0) return;
        tabs.remove(i);
        content.removeView(w);
        boolean wasCurrent = w == current;
        if (wasCurrent) current = null;
        w.destroy();
        if (tabs.isEmpty()) {
            finish();
            return;
        }
        if (wasCurrent) select(tabs.get(Math.min(i, tabs.size() - 1)));
        updateTabCount();
    }

    private void updateTabCount() {
        if (tabCount != null) tabCount.setText(String.valueOf(tabs.size()));
    }

    private void showTabSwitcher() {
        final List<String> titles = new ArrayList<>();
        for (WebView w : tabs) {
            String t = w.getTitle();
            if (t == null || t.isEmpty()) t = w.getUrl() == null ? "New tab" : w.getUrl();
            t = t.replaceFirst(" - YouTube$", "");
            titles.add((w == current ? "▶ " : "    ") + t);
        }
        ListView list = new ListView(this);
        list.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_list_item_1, titles));
        final AlertDialog dialog = new AlertDialog.Builder(this, AlertDialog.THEME_DEVICE_DEFAULT_DARK)
                .setTitle("Tabs (long-press to close)")
                .setView(list)
                .setPositiveButton("New tab", (d, which) -> newTab(PHONE_HOME, true))
                .setNegativeButton("Close current", (d, which) -> { if (current != null) closeTab(current); })
                .create();
        list.setOnItemClickListener((parent, view, pos, id) -> { select(tabs.get(pos)); dialog.dismiss(); });
        list.setOnItemLongClickListener((parent, view, pos, id) -> { closeTab(tabs.get(pos)); dialog.dismiss(); return true; });
        dialog.show();
    }

    // ------------------------------------------------------------------ WebView

    @SuppressLint("SetJavaScriptEnabled")
    private WebView createWebView() {
        final WebView w = new WebView(this);
        w.setBackgroundColor(Color.BLACK);
        w.setFocusable(true);
        w.setFocusableInTouchMode(true);

        WebSettings s = w.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setDatabaseEnabled(true);
        s.setMediaPlaybackRequiresUserGesture(false);
        s.setLoadWithOverviewMode(true);
        s.setUseWideViewPort(true);
        s.setBuiltInZoomControls(false);
        s.setSupportMultipleWindows(!tv);
        s.setJavaScriptCanOpenWindowsAutomatically(false);
        if (tv) s.setUserAgentString(TV_USER_AGENT);
        if (Build.VERSION.SDK_INT >= 21) {
            CookieManager.getInstance().setAcceptThirdPartyCookies(w, true); // Google sign-in
            s.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        }

        // Scripts that must run before YouTube's own (ad data stripping, desktop CSS).
        if (documentStartScripts) {
            if (!scripts.adblockScript.isEmpty())
                WebViewCompat.addDocumentStartJavaScript(w, scripts.adblockScript, SCRIPT_ORIGINS);
            if (!scripts.desktopStyleScript.isEmpty())
                WebViewCompat.addDocumentStartJavaScript(w, scripts.desktopStyleScript, SCRIPT_ORIGINS);
        }

        w.setWebViewClient(new Client());
        w.setWebChromeClient(new Chrome());
        if (!tv) w.setOnLongClickListener(v -> onLongPress(w));
        return w;
    }

    private class Client extends WebViewClient {
        @Override
        @SuppressWarnings("deprecation")
        public boolean shouldOverrideUrlLoading(WebView view, String url) {
            return handleNavigation(url);
        }

        @Override
        @TargetApi(24)
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            return handleNavigation(request.getUrl().toString());
        }

        @Override
        @SuppressWarnings("deprecation")
        public WebResourceResponse shouldInterceptRequest(WebView view, String url) {
            return scripts.shouldBlock(url) ? WebScripts.emptyResponse() : null;
        }

        @Override
        @TargetApi(21)
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
            return scripts.shouldBlock(request.getUrl().toString()) ? WebScripts.emptyResponse() : null;
        }

        @Override
        public void onPageStarted(WebView view, String url, android.graphics.Bitmap favicon) {
            // Old WebViews (Android 4.4): no document-start scripts, so inject as early as we can.
            if (!documentStartScripts) {
                if (!scripts.adblockScript.isEmpty()) view.evaluateJavascript(scripts.adblockScript, null);
                if (!scripts.desktopStyleScript.isEmpty()) view.evaluateJavascript(scripts.desktopStyleScript, null);
            }
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            if (!documentStartScripts && !scripts.desktopStyleScript.isEmpty())
                view.evaluateJavascript(scripts.desktopStyleScript, null);
            updateTabCount();
        }
    }

    /** true = handled here (blocked or opened elsewhere); false = let the WebView load it. */
    private boolean handleNavigation(String url) {
        if (NavigationPolicy.isAllowed(url)) return false;
        String target = NavigationPolicy.unwrap(url);
        if (NavigationPolicy.isWebUrl(target)) openExternally(target);
        return true; // intent://, market:// etc. are ignored
    }

    private void openExternally(String url) {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)).addCategory(Intent.CATEGORY_BROWSABLE));
        } catch (ActivityNotFoundException e) {
            Toast.makeText(this, "No app can open this link", Toast.LENGTH_SHORT).show();
        }
    }

    private class Chrome extends WebChromeClient {
        @Override
        public boolean onCreateWindow(WebView view, boolean isDialog, boolean isUserGesture, Message resultMsg) {
            if (tv) return false;
            WebView popup = newTab(null, true);
            ((WebView.WebViewTransport) resultMsg.obj).setWebView(popup);
            resultMsg.sendToTarget();
            return true;
        }

        @Override
        public void onCloseWindow(WebView window) {
            // YouTube TV's own "Exit" button calls window.close().
            if (tv) finish();
            else closeTab(window);
        }

        @Override
        public void onReceivedTitle(WebView view, String title) {
            updateTabCount();
        }

        @Override
        public void onShowCustomView(View view, CustomViewCallback callback) {
            enterFullscreen(view, callback);
        }

        @Override
        public void onHideCustomView() {
            exitFullscreen();
        }
    }

    /** Long-press a link or thumbnail on a phone: open it in a new tab. */
    private boolean onLongPress(final WebView w) {
        WebView.HitTestResult hit = w.getHitTestResult();
        int type = hit.getType();
        if (type == WebView.HitTestResult.SRC_ANCHOR_TYPE) {
            offerNewTab(hit.getExtra());
            return true;
        }
        if (type == WebView.HitTestResult.SRC_IMAGE_ANCHOR_TYPE) {
            // The hit result holds the image URL; ask the page for the link around it.
            Handler h = new Handler(Looper.getMainLooper(), msg -> {
                Object href = msg.getData().get("url");
                if (href != null) offerNewTab(href.toString());
                return true;
            });
            w.requestFocusNodeHref(h.obtainMessage());
            return true;
        }
        return false;
    }

    private void offerNewTab(final String url) {
        if (url == null || !NavigationPolicy.isAllowed(url)) return;
        new AlertDialog.Builder(this, AlertDialog.THEME_DEVICE_DEFAULT_DARK)
                .setItems(new CharSequence[]{"Open in new tab", "Open in background tab"}, (d, which) -> {
                    newTab(url, which == 0);
                    if (which == 1) Toast.makeText(this, "Opened in background", Toast.LENGTH_SHORT).show();
                })
                .show();
    }

    // ------------------------------------------------------------------ full screen video

    private void enterFullscreen(View view, WebChromeClient.CustomViewCallback callback) {
        if (customView != null) {
            callback.onCustomViewHidden();
            return;
        }
        customView = view;
        customViewCallback = callback;
        ((FrameLayout) getWindow().getDecorView()).addView(view,
                new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        view.setKeepScreenOn(true);
        getWindow().getDecorView().setSystemUiVisibility(
                View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                        | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY | View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
        if (!tv) {
            orientationBeforeFullscreen = getRequestedOrientation();
            setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE);
        }
    }

    private void exitFullscreen() {
        if (customView == null) return;
        ((FrameLayout) getWindow().getDecorView()).removeView(customView);
        customView = null;
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_VISIBLE);
        if (!tv) setRequestedOrientation(orientationBeforeFullscreen);
        if (customViewCallback != null) customViewCallback.onCustomViewHidden();
        customViewCallback = null;
    }

    // ------------------------------------------------------------------ keys

    @Override
    public void onBackPressed() {
        if (customView != null) {
            exitFullscreen();
            return;
        }
        if (tv) {
            // YouTube TV handles "back" itself (Escape). Two quick presses leave the app.
            long now = System.currentTimeMillis();
            if (now - lastBackPress < 600) {
                finish();
                return;
            }
            lastBackPress = now;
            current.dispatchKeyEvent(new KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_ESCAPE));
            current.dispatchKeyEvent(new KeyEvent(KeyEvent.ACTION_UP, KeyEvent.KEYCODE_ESCAPE));
            return;
        }
        if (current != null && current.canGoBack()) current.goBack();
        else if (tabs.size() > 1) closeTab(current);
        else super.onBackPressed();
    }

    // ------------------------------------------------------------------ old WebView warning

    private void warnIfWebViewTooOld() {
        int version = chromiumVersion();
        if (version == 0 || version >= MIN_CHROMIUM) return;

        SharedPreferences prefs = getSharedPreferences("ytlite", MODE_PRIVATE);
        String key = "warned-chromium-" + version;
        if (prefs.getBoolean(key, false)) return;
        prefs.edit().putBoolean(key, true).apply();

        String fix = Build.VERSION.SDK_INT >= 21
                ? "Update \"Android System WebView\" (or Chrome) from the Play Store to fix this."
                : "On Android " + Build.VERSION.RELEASE + " the built-in web engine cannot be updated.";
        new AlertDialog.Builder(this, AlertDialog.THEME_DEVICE_DEFAULT_DARK)
                .setTitle("Web engine too old")
                .setMessage("This device's web engine is Chromium " + version + ". Current YouTube needs Chromium "
                        + MIN_CHROMIUM + " or newer, so pages may stay blank or show an \"unsupported browser\" notice. "
                        + "YTLite will still try.\n\n" + fix)
                .setPositiveButton("OK", null)
                .show();
    }

    private int chromiumVersion() {
        try {
            Matcher m = Pattern.compile("Chrome/(\\d+)").matcher(WebSettings.getDefaultUserAgent(this));
            return m.find() ? Integer.parseInt(m.group(1)) : 0;
        } catch (Exception e) {
            return 0;
        }
    }
}
