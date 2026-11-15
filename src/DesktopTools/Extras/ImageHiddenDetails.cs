using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

/// <summary>
/// What hidden details an image file carries. <see cref="Readable"/> is false when the metadata could not be read,
/// which is not the same as "none". <see cref="Other"/> counts details outside the four named categories.
/// </summary>
internal sealed record HiddenDetails(bool Readable, bool Location, bool Device, bool Date, bool Author, int Other)
{
    internal bool Any => Location || Device || Date || Author || Other > 0;
    internal static readonly HiddenDetails None = new(true, false, false, false, false, 0);
    internal static readonly HiddenDetails Unreadable = new(false, false, false, false, false, 0);
}

/// <summary>
/// Describes and removes embedded metadata (EXIF, GPS, XMP, IPTC, PNG text chunks and the like). Pixels, size, DPI,
/// pixel format and palette are kept, and colours look the same. Works on copies only: nothing here writes to a file.
/// </summary>
internal static class ImageHiddenDetails
{
    // TIFF/EXIF tags that describe how the pixels are stored, not who, where, when or with what.
    private static readonly HashSet<int> TechnicalTags =
    [
        254, 255, 256, 257, 258, 259, 262, 263, 266, 273, 274, 277, 278, 279, 280, 281, 282, 283, 284, 286, 287, 290, 291, 292, 293,
        296, 297, 301, 317, 318, 319, 320, 322, 323, 324, 325, 338, 339, 340, 341, 513, 514, 529, 530, 531, 532,
        34665, 40965, // pointers to the EXIF and interoperability blocks; their contents are judged on their own
        36864, 40960, 40961, 40962, 40963, 37121, 37122,
    ];
    private static readonly HashSet<int> DeviceTags = [271, 272, 305, 42033, 42034, 42035, 42036, 42037, 50708];
    private static readonly HashSet<int> DateTags = [306, 36867, 36868, 36880, 36881, 36882, 37520, 37521, 37522];
    private static readonly HashSet<int> AuthorTags = [315, 33432, 40093, 42032];
    private const int GpsPointer = 34853, InteropPointer = 40965;
    // PNG chunks and JPEG segments that only describe colour, gamma, resolution or compression.
    private static readonly string[] TechnicalBlocks = ["/srgb", "/gama", "/chrm", "/iccp", "/phys", "/sbit", "/hist", "/trns", "/bkgd", "/app0", "/luminance", "/chrominance", "/app14"];

    /// <summary>Reads the details of a file. Never throws: a missing, damaged or unreadable file gives <see cref="HiddenDetails.Unreadable"/>.</summary>
    internal static HiddenDetails Describe(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0) return HiddenDetails.Unreadable;
            var frame = decoder.Frames[0];
            _ = frame.PixelWidth; // a damaged header fails here rather than claiming "no details"
            var details = Describe(frame);
            // Container-level metadata (for example GIF or TIFF) adds to the first frame's.
            if (details.Readable && decoder.Metadata is BitmapMetadata global) details = Merge(details, Classify(global));
            return details;
        }
        catch (Exception) { return HiddenDetails.Unreadable; }
    }

    /// <summary>Reads the details carried by an in-memory image. Images built in memory (screenshots, edits) have none.</summary>
    internal static HiddenDetails Describe(BitmapSource image)
    {
        try { return image.Metadata is BitmapMetadata metadata ? Classify(metadata) : HiddenDetails.None; }
        catch (Exception) { return HiddenDetails.Unreadable; }
    }

    /// <summary>
    /// A copy with the same pixels, size, DPI, pixel format and palette, and no metadata of any kind. WPF has already
    /// converted a profiled image's pixels to sRGB when it decoded them, so the copy carries no colour profile:
    /// re-attaching the original would apply it a second time and shift the colours.
    /// </summary>
    internal static BitmapSource Strip(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return CopyPixels(image);
    }

    /// <summary>The pixels alone, as a frozen in-memory bitmap with the same size, DPI, format and palette.</summary>
    internal static BitmapSource CopyPixels(BitmapSource image)
    {
        int stride = checked((image.PixelWidth * image.Format.BitsPerPixel + 7) / 8);
        var pixels = new byte[checked(stride * image.PixelHeight)];
        image.CopyPixels(pixels, stride, 0);
        var copy = BitmapSource.Create(image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY, image.Format, image.Palette, pixels, stride);
        copy.Freeze(); return copy;
    }

    private static HiddenDetails Merge(HiddenDetails a, HiddenDetails b) => b.Readable
        ? new(true, a.Location || b.Location, a.Device || b.Device, a.Date || b.Date, a.Author || b.Author, a.Other + b.Other)
        : a;

    private static HiddenDetails Classify(BitmapMetadata metadata)
    {
        bool location = false, device = false, date = false, author = false; int other = 0;
        var leaves = new List<string>();
        Walk(metadata, "", leaves, 0);
        foreach (string query in leaves)
        {
            switch (Category(query.ToLowerInvariant()))
            {
                case "location": location = true; break;
                case "device": device = true; break;
                case "date": date = true; break;
                case "author": author = true; break;
                case "other": other++; break;
            }
        }
        return new(true, location, device, date, author, other);
    }

    /// <summary>Collects the full query path of every leaf value. Throws when the metadata cannot be enumerated (caught by the callers).</summary>
    private static void Walk(BitmapMetadata metadata, string prefix, List<string> leaves, int depth)
    {
        if (depth > 16 || leaves.Count > 10_000) return;
        foreach (string query in metadata)
        {
            object? value;
            try { value = metadata.GetQuery(query); }
            catch (Exception) { leaves.Add(prefix + query); continue; } // present but unreadable still counts
            if (value is BitmapMetadata nested)
            {
                int before = leaves.Count;
                Walk(nested, prefix + query, leaves, depth + 1);
                // A block that is present but gives nothing readable (for example a damaged EXIF block) still counts.
                if (leaves.Count == before) leaves.Add(prefix + query);
            }
            else leaves.Add(prefix + query);
        }
    }

    private static string? Category(string query)
    {
        if (TechnicalBlocks.Any(block => query.StartsWith(block, StringComparison.Ordinal) || query.Contains(block + "/", StringComparison.Ordinal))) return null;
        int[] tags = Tags(query);
        if (tags.Contains(GpsPointer) || query.Contains("/gps", StringComparison.Ordinal)) return "location";
        if (tags.Contains(InteropPointer)) return null;
        // XMP properties and PNG text keywords.
        if (ContainsAny(query, "gps", "latitude", "longitude", "location", "photoshop:city", "photoshop:country", "iptc4xmpcore:location")) return "location";
        if (ContainsAny(query, "tiff:make", "tiff:model", "creatortool", "aux:", ":lens", "{str=software}", "{str=source}", "{str=make}", "{str=model}", "serialnumber")) return "device";
        if (ContainsAny(query, "date", "{str=creation time}", "/time", "/tIME".ToLowerInvariant())) return "date";
        if (ContainsAny(query, "dc:creator", "dc:rights", "xmprights", "photoshop:author", "photoshop:credit", "{str=author}", "{str=artist}", "{str=copyright}", "{str=owner}")) return "author";
        if (tags.Length > 0)
        {
            int tag = tags[^1];
            if (DeviceTags.Contains(tag)) return "device";
            if (DateTags.Contains(tag)) return "date";
            if (AuthorTags.Contains(tag)) return "author";
            if (TechnicalTags.Contains(tag)) return null;
        }
        return "other";
    }

    private static bool ContainsAny(string text, params string[] parts) => parts.Any(part => text.Contains(part, StringComparison.Ordinal));

    /// <summary>The numeric tags in a query such as "/app1/{ushort=0}/{ushort=34853}/{ushort=2}".</summary>
    private static int[] Tags(string query)
    {
        var tags = new List<int>();
        int index = 0;
        while ((index = query.IndexOf("{ushort=", index, StringComparison.Ordinal)) >= 0)
        {
            index += 8;
            int end = query.IndexOf('}', index);
            if (end < 0) break;
            if (int.TryParse(query.AsSpan(index, end - index), out int tag)) tags.Add(tag);
            index = end;
        }
        // In a JPEG the first IFD is {ushort=0} and the thumbnail IFD is {ushort=1} directly under /app1; those are containers, not tags.
        if (query.StartsWith("/app1/", StringComparison.Ordinal) && tags.Count > 1) tags.RemoveAt(0);
        return tags.ToArray();
    }
}
