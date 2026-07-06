using OpenWithManager.App.Models;

namespace OpenWithManager.App.Services;

public static class FileFormatClassifier
{
    public const string OtherId = "other";

    public static readonly IReadOnlyList<FileKindDefinition> KindDefinitions =
    [
        new("images", "Photos and images", "Images", "Pictures, screenshots, and image assets."),
        new("videos", "Videos", "Videos", "Movies, clips, and screen recordings."),
        new("audio", "Music and audio", "Audio", "Songs, recordings, and sound files."),
        new("pdf", "PDF documents", "PDF", "Portable documents and forms."),
        new("office", "Office documents", "Office", "Word, Excel, PowerPoint, and similar office files."),
        new("documents", "Documents and books", "Docs", "Documents, books, and readable files."),
        new("text", "Text and notes", "Text", "Plain text, notes, logs, and Markdown files."),
        new("code", "Code files", "Code", "Developer files that usually open in an editor."),
        new("web", "Web files", "Web", "HTML, style sheets, and web component files."),
        new("data", "Data files", "Data", "Structured data, databases, and interchange files."),
        new("archives", "Compressed files", "Archives", "Zip, 7-Zip, tar, and other packaged files."),
        new("fonts", "Fonts", "Fonts", "Font files used by Windows and design tools."),
        new("executables", "Executables and scripts", "Apps", "Programs, installers, shortcuts, and runnable scripts."),
        new("disk-images", "Disk images", "Disks", "Disk image and virtual drive files."),
        new("certificates", "Certificates and keys", "Keys", "Certificates, keys, and signing files."),
        new("settings", "Settings and system files", "System", "Configuration, registry, and system support files."),
        new(OtherId, "Other formats", "Other", "Registered formats that do not match a known category.")
    ];

    private static readonly Dictionary<string, string> ExtensionCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "images",
        [".jpeg"] = "images",
        [".jpe"] = "images",
        [".jfif"] = "images",
        [".png"] = "images",
        [".gif"] = "images",
        [".bmp"] = "images",
        [".dib"] = "images",
        [".tif"] = "images",
        [".tiff"] = "images",
        [".webp"] = "images",
        [".heic"] = "images",
        [".heif"] = "images",
        [".avif"] = "images",
        [".ico"] = "images",
        [".svg"] = "images",
        [".raw"] = "images",
        [".cr2"] = "images",
        [".nef"] = "images",
        [".arw"] = "images",
        [".dng"] = "images",
        [".orf"] = "images",
        [".psd"] = "images",
        [".ai"] = "images",

        [".mp4"] = "videos",
        [".m4v"] = "videos",
        [".mov"] = "videos",
        [".mkv"] = "videos",
        [".avi"] = "videos",
        [".wmv"] = "videos",
        [".webm"] = "videos",
        [".flv"] = "videos",
        [".3gp"] = "videos",
        [".3g2"] = "videos",
        [".mpg"] = "videos",
        [".mpeg"] = "videos",
        [".mts"] = "videos",
        [".m2ts"] = "videos",
        [".vob"] = "videos",
        [".ogv"] = "videos",

        [".mp3"] = "audio",
        [".wav"] = "audio",
        [".flac"] = "audio",
        [".aac"] = "audio",
        [".m4a"] = "audio",
        [".ogg"] = "audio",
        [".opus"] = "audio",
        [".wma"] = "audio",
        [".aif"] = "audio",
        [".aiff"] = "audio",
        [".mid"] = "audio",
        [".midi"] = "audio",

        [".pdf"] = "pdf",

        [".doc"] = "office",
        [".docx"] = "office",
        [".docm"] = "office",
        [".dot"] = "office",
        [".dotx"] = "office",
        [".xls"] = "office",
        [".xlsx"] = "office",
        [".xlsm"] = "office",
        [".xlsb"] = "office",
        [".xlt"] = "office",
        [".xltx"] = "office",
        [".ppt"] = "office",
        [".pptx"] = "office",
        [".pptm"] = "office",
        [".pps"] = "office",
        [".ppsx"] = "office",
        [".pot"] = "office",
        [".potx"] = "office",
        [".vsd"] = "office",
        [".vsdx"] = "office",
        [".mpp"] = "office",
        [".one"] = "office",
        [".odt"] = "office",
        [".ods"] = "office",
        [".odp"] = "office",

        [".rtf"] = "documents",
        [".epub"] = "documents",
        [".mobi"] = "documents",
        [".azw"] = "documents",
        [".azw3"] = "documents",
        [".chm"] = "documents",
        [".xps"] = "documents",
        [".oxps"] = "documents",
        [".djvu"] = "documents",
        [".pages"] = "documents",

        [".txt"] = "text",
        [".md"] = "text",
        [".markdown"] = "text",
        [".log"] = "text",
        [".nfo"] = "text",
        [".me"] = "text",
        [".tex"] = "text",

        [".cs"] = "code",
        [".vb"] = "code",
        [".fs"] = "code",
        [".java"] = "code",
        [".kt"] = "code",
        [".kts"] = "code",
        [".swift"] = "code",
        [".go"] = "code",
        [".rs"] = "code",
        [".c"] = "code",
        [".cc"] = "code",
        [".cpp"] = "code",
        [".cxx"] = "code",
        [".h"] = "code",
        [".hh"] = "code",
        [".hpp"] = "code",
        [".py"] = "code",
        [".pyw"] = "code",
        [".rb"] = "code",
        [".php"] = "code",
        [".pl"] = "code",
        [".pm"] = "code",
        [".lua"] = "code",
        [".r"] = "code",
        [".dart"] = "code",
        [".scala"] = "code",
        [".groovy"] = "code",
        [".js"] = "code",
        [".jsx"] = "code",
        [".mjs"] = "code",
        [".cjs"] = "code",
        [".tsx"] = "code",

        [".html"] = "web",
        [".htm"] = "web",
        [".css"] = "web",
        [".scss"] = "web",
        [".sass"] = "web",
        [".less"] = "web",
        [".vue"] = "web",
        [".svelte"] = "web",
        [".astro"] = "web",

        [".json"] = "data",
        [".jsonl"] = "data",
        [".ndjson"] = "data",
        [".xml"] = "data",
        [".yaml"] = "data",
        [".yml"] = "data",
        [".toml"] = "data",
        [".csv"] = "data",
        [".tsv"] = "data",
        [".sql"] = "data",
        [".db"] = "data",
        [".sqlite"] = "data",
        [".sqlite3"] = "data",
        [".parquet"] = "data",
        [".avro"] = "data",

        [".zip"] = "archives",
        [".rar"] = "archives",
        [".7z"] = "archives",
        [".tar"] = "archives",
        [".gz"] = "archives",
        [".tgz"] = "archives",
        [".bz2"] = "archives",
        [".xz"] = "archives",
        [".zst"] = "archives",
        [".cab"] = "archives",

        [".ttf"] = "fonts",
        [".otf"] = "fonts",
        [".woff"] = "fonts",
        [".woff2"] = "fonts",
        [".eot"] = "fonts",
        [".fon"] = "fonts",

        [".exe"] = "executables",
        [".msi"] = "executables",
        [".msp"] = "executables",
        [".msix"] = "executables",
        [".appx"] = "executables",
        [".com"] = "executables",
        [".scr"] = "executables",
        [".lnk"] = "executables",
        [".bat"] = "executables",
        [".cmd"] = "executables",
        [".ps1"] = "executables",
        [".vbs"] = "executables",
        [".wsf"] = "executables",
        [".sh"] = "executables",
        [".bash"] = "executables",
        [".zsh"] = "executables",

        [".iso"] = "disk-images",
        [".img"] = "disk-images",
        [".vhd"] = "disk-images",
        [".vhdx"] = "disk-images",
        [".wim"] = "disk-images",
        [".esd"] = "disk-images",
        [".dmg"] = "disk-images",

        [".cer"] = "certificates",
        [".crt"] = "certificates",
        [".pem"] = "certificates",
        [".pfx"] = "certificates",
        [".p12"] = "certificates",
        [".p7b"] = "certificates",
        [".p7c"] = "certificates",
        [".key"] = "certificates",
        [".csr"] = "certificates",
        [".der"] = "certificates",

        [".ini"] = "settings",
        [".inf"] = "settings",
        [".reg"] = "settings",
        [".cfg"] = "settings",
        [".conf"] = "settings",
        [".config"] = "settings",
        [".theme"] = "settings",
        [".themepack"] = "settings",
        [".deskthemepack"] = "settings",
        [".url"] = "settings"
    };

    public static IReadOnlyCollection<string> KnownExtensions { get; } = ExtensionCategories.Keys.ToArray();

    public static string Classify(FileAssociationItem item)
    {
        var extension = NormalizeExtension(item.Extension);
        if (extension == ".ts")
        {
            return ClassifyTs(item);
        }

        if (ExtensionCategories.TryGetValue(extension, out var extensionCategory))
        {
            return extensionCategory;
        }

        return ClassifyPerceivedType(item.PerceivedType)
            ?? ClassifyContentType(item.ContentType)
            ?? ClassifyBySignals(item)
            ?? OtherId;
    }

    private static string ClassifyTs(FileAssociationItem item)
    {
        var signals = GetSignals(item);
        if (ContainsAny(signals, "typescript", "javascript", "source", "script", "vscode", "visualstudio"))
        {
            return "code";
        }

        if (IsVideoContent(item.ContentType) || ContainsAny(signals, "mpeg", "transportstream", "video"))
        {
            return "videos";
        }

        return "code";
    }

    private static string? ClassifyPerceivedType(string? perceivedType)
    {
        return NormalizeSignal(perceivedType) switch
        {
            "image" => "images",
            "video" => "videos",
            "audio" => "audio",
            "compressed" => "archives",
            "document" => "documents",
            "text" => "text",
            "system" => "settings",
            _ => null
        };
    }

    private static string? ClassifyContentType(string? contentType)
    {
        var content = contentType?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        if (content.StartsWith("image/"))
        {
            return "images";
        }

        if (content.StartsWith("video/"))
        {
            return "videos";
        }

        if (content.StartsWith("audio/"))
        {
            return "audio";
        }

        if (content is "application/pdf")
        {
            return "pdf";
        }

        if (ContainsAny(content, "officedocument", "msword", "vnd.ms-", "opendocument"))
        {
            return "office";
        }

        if (ContainsAny(content, "zip", "compressed", "gzip", "x-7z", "x-rar", "x-tar"))
        {
            return "archives";
        }

        if (content.StartsWith("font/") || ContainsAny(content, "font-woff", "opentype", "truetype"))
        {
            return "fonts";
        }

        if (ContainsAny(content, "json", "xml", "yaml", "csv", "sqlite"))
        {
            return "data";
        }

        if (ContainsAny(content, "html", "css", "javascript", "typescript"))
        {
            return "web";
        }

        return content.StartsWith("text/") ? "text" : null;
    }

    private static string? ClassifyBySignals(FileAssociationItem item)
    {
        var signals = GetSignals(item);
        if (ContainsAny(signals, "photoshop", "illustrator", "bitmap", "jpeg", "png", "image", "picture", "photo"))
        {
            return "images";
        }

        if (ContainsAny(signals, "video", "movie", "mpeg", "matroska", "quicktime"))
        {
            return "videos";
        }

        if (ContainsAny(signals, "audio", "music", "sound", "wave", "flac", "mp3"))
        {
            return "audio";
        }

        if (ContainsAny(signals, "pdf"))
        {
            return "pdf";
        }

        if (ContainsAny(signals, "word", "excel", "powerpoint", "office", "spreadsheet", "presentation", "opendocument"))
        {
            return "office";
        }

        if (ContainsAny(signals, "archive", "compressed", "zip", "rar", "7zip", "tar"))
        {
            return "archives";
        }

        if (ContainsAny(signals, "font", "truetype", "opentype", "woff"))
        {
            return "fonts";
        }

        if (ContainsAny(signals, "html", "webpage", "stylesheet", "css"))
        {
            return "web";
        }

        if (ContainsAny(signals, "source", "code", "script", "python", "javascript", "typescript", "csharp", "java"))
        {
            return "code";
        }

        if (ContainsAny(signals, "database", "json", "xml", "yaml", "csv", "data"))
        {
            return "data";
        }

        if (ContainsAny(signals, "certificate", "privatekey", "publickey", "cryptographic"))
        {
            return "certificates";
        }

        if (ContainsAny(signals, "diskimage", "virtualdisk", "isoimage"))
        {
            return "disk-images";
        }

        if (ContainsAny(signals, "configuration", "settings", "registry", "shortcut", "system"))
        {
            return "settings";
        }

        return null;
    }

    private static bool IsVideoContent(string? contentType)
    {
        var content = contentType?.Trim().ToLowerInvariant() ?? "";
        return content.StartsWith("video/") || ContainsAny(content, "mpeg", "mp2t");
    }

    private static string GetSignals(FileAssociationItem item)
    {
        return NormalizeSignal(string.Join(" ", new[]
        {
            item.Extension,
            item.Description,
            item.ProgId,
            item.FriendlyName,
            item.ContentType,
            item.PerceivedType
        }));
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeExtension(string extension)
    {
        var value = extension.Trim();
        return value.StartsWith('.') ? value.ToLowerInvariant() : $".{value.ToLowerInvariant()}";
    }

    private static string NormalizeSignal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
