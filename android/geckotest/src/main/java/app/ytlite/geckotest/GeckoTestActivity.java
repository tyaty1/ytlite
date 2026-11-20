package app.ytlite.geckotest;

import android.app.Activity;
import android.app.AlertDialog;
import android.graphics.Color;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.util.Log;
import android.view.Gravity;
import android.widget.FrameLayout;
import android.widget.TextView;

import org.mozilla.geckoview.BuildConfig;
import org.mozilla.geckoview.GeckoRuntime;
import org.mozilla.geckoview.GeckoRuntimeSettings;
import org.mozilla.geckoview.GeckoSession;
import org.mozilla.geckoview.GeckoView;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.Charset;

/**
 * Experiment: does YouTube play in GeckoView Lite 118 on Android 4.4?
 * On start you choose how video is decoded:
 *   H.264 (hardware) - Android's own decoder via MediaCodec (what YouTube normally gets)
 *   VP9 (software)   - MP4 turned off entirely, so YouTube serves WebM/VP9, decoded by Gecko itself
 * The choice is written to a GeckoView config file before the engine starts, so no computer is needed.
 * Status shows in the bar at the bottom; details go to logcat tag "GeckoTest".
 */
public class GeckoTestActivity extends Activity {
    private static final String TAG = "GeckoTest";
    private static GeckoRuntime runtime;   // one engine per process; restart the app to switch modes
    private GeckoSession session;
    private TextView status;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        if (runtime != null) {             // engine already running in this process
            start(null);
            return;
        }
        new AlertDialog.Builder(this, AlertDialog.THEME_DEVICE_DEFAULT_DARK)
                .setTitle("Video decoding (GeckoView " + BuildConfig.MOZ_APP_VERSION + ")")
                .setItems(new CharSequence[]{"H.264 (Android hardware decoder)", "VP9 (Gecko software decoder)"},
                        (d, which) -> start(which == 0 ? "h264" : "vp9"))
                .setCancelable(false)
                .show();
    }

    private void start(String mode) {
        FrameLayout root = new FrameLayout(this);
        GeckoView view = new GeckoView(this);
        root.addView(view);
        status = new TextView(this);
        status.setBackgroundColor(0xCC000000);
        status.setTextColor(Color.WHITE);
        status.setPadding(16, 8, 16, 8);
        root.addView(status, new FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM));
        setContentView(root);

        if (runtime == null) {
            File profile = writeProfile(mode);
            GeckoRuntimeSettings settings = new GeckoRuntimeSettings.Builder()
                    .consoleOutput(true)
                    // Our own profile folder, so Gecko reads the user.js written for this mode.
                    .arguments(new String[]{"-profile", profile.getAbsolutePath()})
                    .build();
            runtime = GeckoRuntime.create(this, settings);
            showStatus("Mode: " + mode + " | Android " + Build.VERSION.RELEASE + " | " + Build.CPU_ABI + " | loading...");
            Log.i(TAG, "mode " + mode + " profile " + profile);
        }

        session = new GeckoSession();
        session.setProgressDelegate(new GeckoSession.ProgressDelegate() {
            @Override
            public void onPageStart(GeckoSession s, String url) { Log.i(TAG, "page start " + url); }

            @Override
            public void onPageStop(GeckoSession s, boolean success) { Log.i(TAG, "page stop success=" + success); }
        });
        session.setContentDelegate(new GeckoSession.ContentDelegate() {
            @Override
            public void onTitleChange(GeckoSession s, String title) {
                Log.i(TAG, "title " + title);
                showStatus(statusPrefix(mode) + title);
            }

            @Override
            public void onCrash(GeckoSession s) { showStatus("Engine content process crashed"); }
        });
        session.open(runtime);
        view.setSession(session);

        Uri data = getIntent().getData();
        String url = data != null ? data.toString() : "https://m.youtube.com/watch?v=aqz-KE-bpKQ&app=m";
        session.loadUri(url);
    }

    private static String statusPrefix(String mode) {
        return mode == null ? "" : "[" + mode + "] ";
    }

    private void showStatus(final String text) {
        runOnUiThread(() -> { if (status != null) status.setText(text); });
    }

    /** Profile folder with a user.js holding the Gecko prefs for the chosen mode (read at every start). */
    private File writeProfile(String mode) {
        File dir = new File(getFilesDir(), "geckoprofile");
        dir.mkdirs();
        StringBuilder js = new StringBuilder();
        js.append("user_pref(\"media.autoplay.default\", 0);\n");              // let YouTube start playback
        js.append("user_pref(\"services.settings.server\", \"\");\n");         // no Mozilla settings sync (fails on old engines)
        boolean vp9 = "vp9".equals(mode);
        js.append("user_pref(\"media.mp4.enabled\", ").append(!vp9).append(");\n");                // vp9: no MP4 at all
        js.append("user_pref(\"media.mediasource.mp4.enabled\", ").append(!vp9).append(");\n");    //      (also blocks 360p progressive)
        js.append("user_pref(\"media.android-media-codec.enabled\", ").append(!vp9).append(");\n"); //      Gecko's own decoder
        File f = new File(dir, "user.js");
        try (FileOutputStream out = new FileOutputStream(f)) {
            out.write(js.toString().getBytes(Charset.forName("UTF-8")));
        } catch (Exception e) {
            Log.e(TAG, "could not write user.js", e);
        }
        return dir;
    }

    @Override
    protected void onDestroy() {
        if (session != null) session.close();
        super.onDestroy();
    }
}
