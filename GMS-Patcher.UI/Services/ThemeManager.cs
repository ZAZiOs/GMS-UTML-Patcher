using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using ImageMagick;

namespace GMS_Patcher.UI.Services;

public class ThemeLinks
{
    [JsonPropertyName("issues")]
    public string Issues { get; set; } = "https://github.com/";

    [JsonPropertyName("community")]
    public string Community { get; set; } = "https://t.me/deltarune_ru";

    [JsonPropertyName("communityText")]
    public string CommunityText { get; set; } = "Наш Telegram-канал";
}

public class ThemeTexts
{
    [JsonPropertyName("windowTitle")]
    public string WindowTitle { get; set; } = "Установщик перевода DELTARUNE";

    [JsonPropertyName("introTitle")]
    public string IntroTitle { get; set; } = "Мастер установки перевода DELTARUNE";

    [JsonPropertyName("introSubtitle")]
    public string IntroSubtitle { get; set; } = "Программа установит перевод для DELTARUNE.";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "v2.0.0";

    [JsonPropertyName("dropLabel")]
    public string DropLabel { get; set; } = "Перетащите иконку игры, чтобы указать путь к папке";

    [JsonPropertyName("successTitle")]
    public string SuccessTitle { get; set; } = "Установка успешно завершена!";

    [JsonPropertyName("successSubtitle")]
    public string SuccessSubtitle { get; set; } = "Патч успешно применён к файлам игры.";

    [JsonPropertyName("startGameCheckbox")]
    public string StartGameCheckbox { get; set; } = "Запустить DELTARUNE";

    [JsonPropertyName("gameExecutable")]
    public string GameExecutable { get; set; } = "DELTARUNE.exe";

    [JsonPropertyName("macApp")]
    public string MacApp { get; set; } = "DELTARUNE.app";
}

public class ThemeConfig
{
    [JsonPropertyName("texts")]
    public ThemeTexts Texts { get; set; } = new();

    [JsonPropertyName("links")]
    public ThemeLinks Links { get; set; } = new();

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#5865F2";
}

public static class ThemeManager
{
    private static readonly Assembly CurrentAssembly = Assembly.GetExecutingAssembly();
    private static string? _externalResourcesDir;
    private static readonly Dictionary<string, Bitmap> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);

    public static ThemeConfig Config { get; private set; } = new();

    static ThemeManager()
    {
        Initialize();
    }

    public static void Initialize(string? customResourcePath = null)
    {
        string baseDir = AppContext.BaseDirectory;
        string candidate = customResourcePath ?? Path.Combine(baseDir, "resources");

        if (Directory.Exists(candidate))
        {
            _externalResourcesDir = candidate;
        }
        else
        {
            string parentCandidate = Path.Combine(Directory.GetParent(baseDir)?.FullName ?? "", "resources");
            if (Directory.Exists(parentCandidate))
                _externalResourcesDir = parentCandidate;
        }

        // Try load theme.json
        if (!string.IsNullOrEmpty(_externalResourcesDir))
        {
            string themeJson = Path.Combine(_externalResourcesDir, "theme.json");
            if (File.Exists(themeJson))
            {
                try
                {
                    string json = File.ReadAllText(themeJson);
                    var cfg = JsonSerializer.Deserialize<ThemeConfig>(json);
                    if (cfg != null)
                        Config = cfg;
                }
                catch { }
            }
        }
    }

    public static Bitmap? GetBitmap(string assetName)
    {
        if (_bitmapCache.TryGetValue(assetName, out var cached))
            return cached;

        // 1. Try from external resources folder
        if (!string.IsNullOrEmpty(_externalResourcesDir))
        {
            string extFile = Path.Combine(_externalResourcesDir, assetName);
            if (File.Exists(extFile))
            {
                try
                {
                    using var stream = File.OpenRead(extFile);
                    var bmp = new Bitmap(stream);
                    _bitmapCache[assetName] = bmp;
                    return bmp;
                }
                catch { }
            }
        }

        // 2. Try from embedded resources
        try
        {
            string[] manifestNames = CurrentAssembly.GetManifestResourceNames();
            foreach (var name in manifestNames)
            {
                if (name.EndsWith(assetName, StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = CurrentAssembly.GetManifestResourceStream(name);
                    if (stream != null)
                    {
                        var bmp = new Bitmap(stream);
                        _bitmapCache[assetName] = bmp;
                        return bmp;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public static List<(Bitmap frame, int delayMs)> GetGifFrames(string assetName)
    {
        var frames = new List<(Bitmap frame, int delayMs)>();

        byte[]? gifBytes = null;

        // Try external
        if (!string.IsNullOrEmpty(_externalResourcesDir))
        {
            string extFile = Path.Combine(_externalResourcesDir, assetName);
            if (File.Exists(extFile))
            {
                try { gifBytes = File.ReadAllBytes(extFile); } catch { }
            }
        }

        // Try embedded
        if (gifBytes == null)
        {
            foreach (var name in CurrentAssembly.GetManifestResourceNames())
            {
                if (name.EndsWith(assetName, StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = CurrentAssembly.GetManifestResourceStream(name);
                    if (stream != null)
                    {
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        gifBytes = ms.ToArray();
                        break;
                    }
                }
            }
        }

        if (gifBytes == null || gifBytes.Length == 0)
            return frames;

        try
        {
            using var collection = new MagickImageCollection(gifBytes);
            collection.Coalesce();

            foreach (var frame in collection)
            {
                int delayMs = (int)(frame.AnimationDelay * 10);
                if (delayMs <= 0) delayMs = 100;

                using var ms = new MemoryStream();
                frame.Format = MagickFormat.Png;
                frame.Write(ms);
                ms.Position = 0;

                var avaloniaBitmap = new Bitmap(ms);
                frames.Add((avaloniaBitmap, delayMs));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to decode GIF {assetName}: {ex.Message}");
        }

        return frames;
    }
}
