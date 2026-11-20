using System.Text.Json;
using System.Text.Json.Serialization;

namespace YTLite;

/// <summary>An image-search service the frame can be sent to.</summary>
sealed class VisualSearchEngine
{
    public string Name { get; set; } = "";
    /// <summary>Multipart POST target. Placeholders: {ts} = Unix ms timestamp.</summary>
    public string UploadUrl { get; set; } = "";
    /// <summary>Form field that carries the image.</summary>
    public string Field { get; set; } = "";
    /// <summary>"file" = JPEG file upload, "base64" = JPEG as base64 text.</summary>
    public string FieldKind { get; set; } = "file";
    /// <summary>Extra form fields. Placeholders: {w}, {h} = image size, {ts}.</summary>
    public Dictionary<string, string> ExtraFields { get; set; } = new();
    /// <summary>Page opened in clipboard fallback mode, where the user presses Ctrl+V.</summary>
    public string PasteUrl { get; set; } = "";
}

/// <summary>User settings, stored in %LOCALAPPDATA%\YTLite\settings.json (created on first run).</summary>
sealed class AppSettings
{
    /// <summary>"desktop" (www.youtube.com) or "mobile" (m.youtube.com, lighter).</summary>
    public string UiMode { get; set; } = "desktop";
    /// <summary>Restyle m.youtube.com for desktop windows (Styles\mobile-desktop.css).</summary>
    public bool MobileDesktopLayout { get; set; } = true;
    public int SuspendAfterMinutes { get; set; } = 5;
    public int RendererProcessLimit { get; set; } = 2;
    public bool UseUblockLite { get; set; } = true;
    public bool Jp2Lossless { get; set; } = true;
    /// <summary>1–99, used when Jp2Lossless is false.</summary>
    public int Jp2Quality { get; set; } = 85;
    /// <summary>"auto" = auto-upload through the default browser, "clipboard" = copy and open the paste page.</summary>
    public string VisualSearchMode { get; set; } = "auto";

    public VisualSearchEngine GoogleLens { get; set; } = new()
    {
        Name = "Google Lens",
        UploadUrl = "https://lens.google.com/v3/upload?ep=ccm&s=&st={ts}",
        Field = "encoded_image",
        FieldKind = "file",
        ExtraFields = new() { ["processed_image_dimensions"] = "{w},{h}" },
        PasteUrl = "https://lens.google.com/",
    };

    public VisualSearchEngine Bing { get; set; } = new()
    {
        Name = "Bing Visual Search",
        UploadUrl = "https://www.bing.com/images/search?view=detailv2&iss=sbiupload&FORM=SBIIDP",
        Field = "imageBin",
        FieldKind = "base64",
        ExtraFields = new() { ["cbir"] = "sbi" },
        PasteUrl = "https://www.bing.com/visualsearch",
    };

    [JsonIgnore] public bool Mobile => string.Equals(UiMode, "mobile", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public string Origin => Mobile ? "https://m.youtube.com" : "https://www.youtube.com";
    [JsonIgnore] public string HomeUrl => Mobile ? "https://m.youtube.com/?app=m&persist_app=1" : "https://www.youtube.com/";
    public string SearchUrl(string query) => $"{Origin}/results?search_query={Uri.EscapeDataString(query)}";

    /// <summary>Profile + settings folder. YTLITE_DATA_DIR overrides it (lets a self-test run beside a normal window).</summary>
    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("YTLITE_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YTLite");

    static string FilePath => Path.Combine(DataDir, "settings.json");

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static AppSettings Current { get; } = Load();

    static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings();
        }
        catch { /* corrupt file: fall back to defaults */ }

        var s = new AppSettings();
        s.Save();
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { /* settings are a convenience; never crash over them */ }
    }
}
