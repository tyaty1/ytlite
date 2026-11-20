using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace YTLite;

/// <summary>
/// Built-in ad blocking ("layer B"): blocks ad/tracking requests, strips ad data from YouTube's
/// player responses before the player sees it, hides ad elements, and fast-forwards any ad that
/// still plays. Rules live in AdBlock\adblock-rules.json and AdBlock\adblock.js next to the exe.
/// </summary>
static class AdBlocker
{
    sealed class Rules
    {
        public List<string> BlockUrlPatterns { get; set; } = new();
        public List<string> PrunePaths { get; set; } = new();
        public List<string> HideSelectors { get; set; } = new();
    }

    static int _blocked;
    public static int BlockedCount => _blocked;

    public static string RulesPath => Path.Combine(AppContext.BaseDirectory, "AdBlock", "adblock-rules.json");
    public static string ScriptPath => Path.Combine(AppContext.BaseDirectory, "AdBlock", "adblock.js");

    static Rules LoadRules()
    {
        try
        {
            return JsonSerializer.Deserialize<Rules>(File.ReadAllText(RulesPath),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                   ?? new Rules();
        }
        catch { return new Rules(); }
    }

    /// <summary>Rules and script are re-read for every new tab, so edits apply without a rebuild.</summary>
    /// <summary>Self-test switch: no built-in blocking and no uBlock Origin Lite.</summary>
    public static bool Disabled { get; set; }

    public static async Task AttachAsync(CoreWebView2 core, CoreWebView2Environment env)
    {
        if (Disabled) return;
        var rules = LoadRules();

        foreach (var pattern in rules.BlockUrlPatterns)
            core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);

        if (rules.BlockUrlPatterns.Count > 0)
        {
            core.WebResourceRequested += (_, e) =>
            {
                // Only filtered (ad) URLs reach this handler.
                Interlocked.Increment(ref _blocked);
                e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "Content-Type: text/plain");
            };
        }

        string script;
        try { script = await File.ReadAllTextAsync(ScriptPath); }
        catch { return; }

        var config = JsonSerializer.Serialize(new { prunePaths = rules.PrunePaths, hideSelectors = rules.HideSelectors });
        await core.AddScriptToExecuteOnDocumentCreatedAsync($"window.__ytliteRules = {config};\n{script}");
    }
}
