using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace YTLite;

sealed class FrameGrab
{
    public required byte[] Png { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Time { get; init; }
    public string VideoId { get; init; } = "";
    public string Title { get; init; } = "YouTube";
    public string PageUrl { get; init; } = "";

    /// <summary>Link back to this exact moment of the video.</summary>
    public string WatchUrl => VideoId.Length > 0
        ? $"https://www.youtube.com/watch?v={VideoId}&t={(int)Time}s"
        : PageUrl;
}

/// <summary>Runs grab-frame.js on demand (nothing is injected until a screenshot is taken).</summary>
static class FrameGrabber
{
    static string ScriptPath => Path.Combine(AppContext.BaseDirectory, "Screenshot", "grab-frame.js");

    public static async Task<FrameGrab> GrabAsync(CoreWebView2 core, TimeSpan? timeout = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<FrameGrab>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("type", out var type) || type.GetString() != "ytlite-frame"
                    || !root.TryGetProperty("id", out var rid) || rid.GetString() != id)
                    return; // not ours

                if (!root.GetProperty("ok").GetBoolean())
                {
                    tcs.TrySetException(new InvalidOperationException(root.GetProperty("error").GetString()));
                    return;
                }

                tcs.TrySetResult(new FrameGrab
                {
                    Png = Convert.FromBase64String(root.GetProperty("png").GetString()!),
                    Width = root.GetProperty("width").GetInt32(),
                    Height = root.GetProperty("height").GetInt32(),
                    Time = root.GetProperty("time").GetDouble(),
                    VideoId = root.GetProperty("videoId").GetString() ?? "",
                    Title = root.GetProperty("title").GetString() ?? "YouTube",
                    PageUrl = root.GetProperty("pageUrl").GetString() ?? "",
                });
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }

        core.WebMessageReceived += OnMessage;
        try
        {
            var js = await File.ReadAllTextAsync(ScriptPath);
            await core.ExecuteScriptAsync($"({js.Trim().TrimEnd(';')})({JsonSerializer.Serialize(id)});");
            return await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("The page did not return a frame in time.");
        }
        finally
        {
            core.WebMessageReceived -= OnMessage;
        }
    }
}
