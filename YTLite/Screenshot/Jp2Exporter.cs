using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ImageMagick;

namespace YTLite;

/// <summary>Saves a frame as JPEG 2000 (.jp2) through Magick.NET / OpenJPEG.</summary>
static class Jp2Exporter
{
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "YTLite");

    public static string SuggestPath(FrameGrab grab)
    {
        Directory.CreateDirectory(DefaultFolder);
        var t = TimeSpan.FromSeconds(Math.Max(0, grab.Time));
        string stamp = t.TotalHours >= 1 ? $"{(int)t.TotalHours}-{t:mm\\-ss}" : $"{t:mm\\-ss}";
        string name = $"{Sanitize(grab.Title)}_{stamp}";

        string path = Path.Combine(DefaultFolder, name + ".jp2");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(DefaultFolder, $"{name} ({n}).jp2");
        return path;
    }

    public static void Save(FrameGrab grab, string path, bool lossless, int quality)
    {
        using var image = new MagickImage(grab.Png);
        image.Alpha(AlphaAction.Off); // video frames are opaque: store RGB only
        image.Format = MagickFormat.Jp2;
        // ImageMagick's JP2 writer: quality 100 (or 0) = reversible 5/3 wavelet, i.e. lossless.
        image.Quality = lossless ? 100u : (uint)Math.Clamp(quality, 1, 99);
        image.Write(path);
        AppendSourceBox(path, grab); // ImageMagick's JP2 writer drops comments, so add our own metadata box
    }

    /// <summary>
    /// Appends a standard JP2 XML box ('xml ') with the video link, title and timestamp.
    /// Readers that don't know it skip it; the image data is untouched.
    /// </summary>
    static void AppendSourceBox(string path, FrameGrab grab)
    {
        var xml = new XElement("ytlite",
            new XElement("source", grab.WatchUrl),
            new XElement("title", grab.Title),
            new XElement("time", grab.Time.ToString("F3", CultureInfo.InvariantCulture)),
            new XElement("captured", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture)));
        byte[] payload = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + xml.ToString(SaveOptions.DisableFormatting));

        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        Span<byte> head = stackalloc byte[8];

        // Must be a JP2 container (signature box), not a raw codestream.
        if (fs.Read(head) != 8 || BinaryPrimitives.ReadUInt32BigEndian(head) != 12 || !head[4..].SequenceEqual("jP  "u8))
            return;

        // Walk the boxes; a last box with length 0 means "to end of file" and must get a real length first.
        long pos = 0;
        Span<byte> big = stackalloc byte[8];
        while (pos + 8 <= fs.Length)
        {
            fs.Position = pos;
            fs.ReadExactly(head);
            long len = BinaryPrimitives.ReadUInt32BigEndian(head);
            if (len == 1)
            {
                fs.ReadExactly(big);
                len = (long)BinaryPrimitives.ReadUInt64BigEndian(big);
            }
            else if (len == 0)
            {
                long real = fs.Length - pos;
                if (real > uint.MaxValue) return;
                fs.Position = pos;
                BinaryPrimitives.WriteUInt32BigEndian(head, (uint)real);
                fs.Write(head[..4]);
                len = real;
            }
            if (len < 8) return; // malformed; leave the file alone
            pos += len;
        }

        fs.Position = fs.Length;
        BinaryPrimitives.WriteUInt32BigEndian(head, (uint)(payload.Length + 8));
        "xml "u8.CopyTo(head[4..]);
        fs.Write(head);
        fs.Write(payload);
    }

    /// <summary>Reads the video link back from a .jp2 saved by YTLite (null if absent).</summary>
    public static string? ReadSourceUrl(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int pos = 0;
        while (pos + 8 <= bytes.Length)
        {
            long len = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos));
            if (len == 1) len = (long)BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(pos + 8));
            if (len == 0) len = bytes.Length - pos;
            if (len < 8 || pos + len > bytes.Length) return null;
            if (bytes.AsSpan(pos + 4, 4).SequenceEqual("xml "u8))
            {
                try
                {
                    var doc = XDocument.Parse(Encoding.UTF8.GetString(bytes, pos + 8, (int)len - 8));
                    if (doc.Root?.Name == "ytlite") return doc.Root.Element("source")?.Value;
                }
                catch { }
            }
            pos += (int)len;
        }
        return null;
    }

    static string Sanitize(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        foreach (var c in title) sb.Append(invalid.Contains(c) ? '_' : c);
        var s = sb.ToString().Trim().TrimEnd('.');
        if (s.Length > 80) s = s[..80].TrimEnd();
        return s.Length == 0 ? "YouTube" : s;
    }
}
