using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ImageMagick;

namespace GMS_Patcher.UI.Services;

public class StringsFileModel
{
    public Dictionary<string, string>? Variables { get; set; }
    public Dictionary<string, string>? Strings { get; set; }
    public Dictionary<string, string>? Links { get; set; }
    public Dictionary<string, string>? Images { get; set; }
}

public static class ThemeManager
{
    private static readonly Assembly CurrentAssembly = Assembly.GetExecutingAssembly();
    private static string? _resourcesDirectory;
    private static readonly Dictionary<string, Bitmap> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, string> Strings { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, string> Links { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, string> Images { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static string ResourcesDirectory => _resourcesDirectory ?? string.Empty;

    static ThemeManager()
    {
        Initialize();
    }

    public static void Initialize(string? customResourcePath = null)
    {
        _resourcesDirectory = FindResourcesDirectory(customResourcePath);
        LoadDefaultStrings();
        LoadStringsJson();
    }

    private static string? FindResourcesDirectory(string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath) && Directory.Exists(customPath))
            return Path.GetFullPath(customPath);

        string baseDir = AppContext.BaseDirectory;

        string path1 = Path.Combine(baseDir, "data", "resources");
        if (Directory.Exists(path1)) return Path.GetFullPath(path1);

        string path2 = Path.Combine(Directory.GetCurrentDirectory(), "data", "resources");
        if (Directory.Exists(path2)) return Path.GetFullPath(path2);

        string path3 = Path.Combine(baseDir, "resources");
        if (Directory.Exists(path3)) return Path.GetFullPath(path3);

        DirectoryInfo? current = new DirectoryInfo(baseDir);
        for (int i = 0; i < 5 && current != null; i++)
        {
            string upPath = Path.Combine(current.FullName, "data", "resources");
            if (Directory.Exists(upPath)) return Path.GetFullPath(upPath);

            upPath = Path.Combine(current.FullName, "resources");
            if (Directory.Exists(upPath)) return Path.GetFullPath(upPath);

            current = current.Parent;
        }

        return null;
    }

    private static void LoadDefaultStrings()
    {
        Variables["GAMENAME"] = "Game Title";
        Variables["VERSION"] = "v1.0.0";
        Variables["AUTHOR"] = "Mod Author";
        Variables["GAMEEXE"] = "Game.exe";

        Links["issues"] = "https://example.com/";
        Links["community"] = "https://example.com/";

        Strings["windowTitle"] = "{GAMENAME} Patch Setup";
        Strings["introTitle"] = "Welcome to the {GAMENAME} Setup Wizard by {AUTHOR}!";
        Strings["introSubtitle"] = "This tool will guide you through installing the patch for {GAMENAME}.";
        Strings["introIssuesLink"] = "Having issues? Contact the developers";
        Strings["introVersionLabel"] = "Patch version:";
        Strings["introNextPrompt"] = "Click \"Next\" to continue.";

        Strings["dropHeaderTitle"] = "Select Game Folder";
        Strings["dropHeaderSubtitle"] = "Drag and drop the game executable or select manually";
        Strings["dropDefaultLabel"] = "Drag and drop the game executable or shortcut here";
        Strings["dropClassicLink"] = "Having trouble? Classic Mode";
        Strings["dropFoundClassicLink"] = "Wrong folder? Classic Mode";
        Strings["dropErrorLabel"] = "Could not find the game. Please try again";
        Strings["dropGroupBoxTitle"] = "Installation Directory";
        Strings["copyright"] = "GMS-UTML-Patcher v2.0";

        Strings["spaceRequiredFormat"] = "Space required: {SPACE}";
        Strings["spaceAvailableFormat"] = "Space available: {SPACE}";
        Strings["spaceNoDisk"] = "*No disk selected*";

        Strings["adminWarning"] = "⚠️ Running as Administrator. Drag-and-drop may not work due to Windows UIPI. Classic Mode is recommended.";

        Strings["pathHeaderTitle"] = "Classic Folder Selection";
        Strings["pathHeaderSubtitle"] = "Specify the directory where {GAMENAME} is installed";
        Strings["pathLabel"] = "{GAMENAME} game directory:";
        Strings["pathBrowseBtn"] = "Browse...";
        Strings["pathBackToDropLink"] = "← Return to Drag and Drop mode";

        Strings["installHeaderTitle"] = "Installing...";
        Strings["installHeaderSubtitle"] = "Please wait while files are being patched";
        Strings["installInitialStatus"] = "Initializing...";
        Strings["installFinishedStatus"] = "Patching finished!";
        Strings["detailsBtnShow"] = "Details";
        Strings["detailsBtnHide"] = "Hide Details";

        Strings["successTitle"] = "Setup has successfully installed the patch!";
        Strings["successSubtitle"] = "🪄 Queen Install Wizard 🪄\nthanks you for installing.";
        Strings["communityLink"] = "Telegram Channel";
        Strings["successFinishPrompt"] = "Click \"Finish\" to close this window.";
        Strings["startGameCheckbox"] = "Launch {GAMENAME}";

        Strings["failTitle"] = "Installation Error";
        Strings["failIssuesLink"] = "Report an issue on GitHub";
        Strings["errorCodeFormat"] = "Error code: {CODE}";

        Strings["btnNext"] = "Next";
        Strings["btnBack"] = "Back";
        Strings["btnInstall"] = "Install";
        Strings["btnExit"] = "Exit";
        Strings["btnCancel"] = "Cancel";
        Strings["btnFinish"] = "Finish";

        Strings["warningTitleMod"] = "Modification Detected!";
        Strings["warningTitleVersion"] = "Game and patch versions do not match!";
        Strings["warningMessageMod"] = "Несовпадение контрольной суммы (возможно, игра уже модифицирована).\nВсё равно продолжить?";
        Strings["warningMessageNewerVersion"] = "Версия игры новее версии перевода. Патч может работать некорректно.\nПродолжить?";
        Strings["warningMessageVersionMismatch"] = "Версии игры и перевода не совпадают.\nВсё равно продолжить?";
        Strings["warningMessageGeneric"] = "Обнаружено несоответствие версий.\nВсё равно продолжить?";
        Strings["warningContinue"] = "Continue";
        Strings["warningCancel"] = "Cancel";
    }

    private static void LoadStringsJson()
    {
        if (string.IsNullOrEmpty(_resourcesDirectory))
            return;

        string stringsJsonPath = Path.Combine(_resourcesDirectory, "strings.json");
        if (!File.Exists(stringsJsonPath))
            return;

        try
        {
            string json = File.ReadAllText(stringsJsonPath);
            var model = JsonSerializer.Deserialize<StringsFileModel>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });

            if (model?.Variables != null)
            {
                foreach (var (k, v) in model.Variables)
                    Variables[k] = v;
            }

            if (model?.Links != null)
            {
                foreach (var (k, v) in model.Links)
                    Links[k] = v;
            }

            if (model?.Strings != null)
            {
                foreach (var (k, v) in model.Strings)
                    Strings[k] = v;
            }

            if (model?.Images != null)
            {
                foreach (var (k, v) in model.Images)
                    Images[k] = v;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load strings.json: {ex.Message}");
        }
    }

    public static string Get(string key, params (string placeholder, string value)[] runtimeArgs)
    {
        if (!Strings.TryGetValue(key, out var rawText))
            rawText = key;

        foreach (var (varKey, varVal) in Variables)
        {
            rawText = rawText.Replace($"{{{varKey}}}", varVal, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var (placeholder, val) in runtimeArgs)
        {
            rawText = rawText.Replace(placeholder, val, StringComparison.OrdinalIgnoreCase);
        }

        return rawText;
    }

    public static string GetLink(string key, string fallback = "")
    {
        return Links.TryGetValue(key, out var link) ? link : fallback;
    }

    #region Strongly Typed String Accessors

    public static string WindowTitle => Get("windowTitle");
    public static string IntroTitle => Get("introTitle");
    public static string IntroSubtitle => Get("introSubtitle");
    public static string IntroIssuesLink => Get("introIssuesLink");
    public static string IntroVersionLabel => Get("introVersionLabel");
    public static string IntroNextPrompt => Get("introNextPrompt");

    public static string DropHeaderTitle => Get("dropHeaderTitle");
    public static string DropHeaderSubtitle => Get("dropHeaderSubtitle");
    public static string DropDefaultLabel => Get("dropDefaultLabel");
    public static string DropClassicLink => Get("dropClassicLink");
    public static string DropFoundClassicLink => Get("dropFoundClassicLink");
    public static string DropErrorLabel => Get("dropErrorLabel");
    public static string DropGroupBoxTitle => Get("dropGroupBoxTitle");
    public static string Copyright => Get("copyright");

    public static string SpaceRequired(string space) => Get("spaceRequiredFormat", ("{SPACE}", space));
    public static string SpaceAvailable(string space) => Get("spaceAvailableFormat", ("{SPACE}", space));
    public static string SpaceNoDisk => Get("spaceNoDisk");
    public static string AdminWarning => Get("adminWarning");

    public static string PathHeaderTitle => Get("pathHeaderTitle");
    public static string PathHeaderSubtitle => Get("pathHeaderSubtitle");
    public static string PathLabel => Get("pathLabel");
    public static string PathBrowseBtn => Get("pathBrowseBtn");
    public static string PathBackToDropLink => Get("pathBackToDropLink");

    public static string InstallHeaderTitle => Get("installHeaderTitle");
    public static string InstallHeaderSubtitle => Get("installHeaderSubtitle");
    public static string InstallInitialStatus => Get("installInitialStatus");
    public static string InstallFinishedStatus => Get("installFinishedStatus");
    public static string DetailsBtnShow => Get("detailsBtnShow");
    public static string DetailsBtnHide => Get("detailsBtnHide");

    public static string SuccessTitle => Get("successTitle");
    public static string SuccessSubtitle => Get("successSubtitle");
    public static string CommunityLink => Get("communityLink");
    public static string SuccessFinishPrompt => Get("successFinishPrompt");
    public static string StartGameCheckbox => Get("startGameCheckbox");

    public static string FailTitle => Get("failTitle");
    public static string FailIssuesLink => Get("failIssuesLink");

    public static string BtnNext => Get("btnNext");
    public static string BtnBack => Get("btnBack");
    public static string BtnInstall => Get("btnInstall");
    public static string BtnExit => Get("btnExit");
    public static string BtnCancel => Get("btnCancel");
    public static string BtnFinish => Get("btnFinish");

    public static string WarningTitleMod => Get("warningTitleMod");
    public static string WarningTitleVersion => Get("warningTitleVersion");
    public static string WarningMessageMod => Get("warningMessageMod");
    public static string WarningMessageNewerVersion => Get("warningMessageNewerVersion");
    public static string WarningMessageVersionMismatch => Get("warningMessageVersionMismatch");
    public static string WarningMessageGeneric => Get("warningMessageGeneric");
    public static string WarningContinue => Get("warningContinue");
    public static string WarningCancel => Get("warningCancel");

    public static string IssuesUrl => GetLink("issues", "https://github.com/");
    public static string CommunityUrl => GetLink("community", "https://t.me/deltarune_ru");
    public static string GameName => Variables.TryGetValue("GAMENAME", out var g) ? g : "Game";
    public static string Version => Variables.TryGetValue("VERSION", out var v) ? v : "v1.0.0";
    public static string GameExe => Variables.TryGetValue("GAMEEXE", out var ex) && !string.IsNullOrWhiteSpace(ex)
        ? ex
        : $"{GameName}.exe";

    #endregion

    #region Image & Asset Loading

    public static Bitmap? GetImageBitmap(string role, params string[] fallbackAliases)
    {
        var candidates = new List<string>();

        void TryAddFromImages(string key)
        {
            if (Images.TryGetValue(key, out var customFileName) && !string.IsNullOrWhiteSpace(customFileName))
            {
                candidates.Add(customFileName);
            }
        }

        TryAddFromImages(role);

        if (role.Equals("exe-icon", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("exeIcon", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("main.ico", StringComparison.OrdinalIgnoreCase))
        {
            TryAddFromImages("exe-icon");
            TryAddFromImages("exeIcon");
            TryAddFromImages("main.ico");
        }
        else if (role.Equals("headerBanner", StringComparison.OrdinalIgnoreCase) ||
                 role.Equals("headerIcon", StringComparison.OrdinalIgnoreCase))
        {
            TryAddFromImages("headerBanner");
            TryAddFromImages("headerIcon");
            TryAddFromImages("header");
        }

        if (fallbackAliases != null)
        {
            foreach (var alias in fallbackAliases)
            {
                TryAddFromImages(alias);
                candidates.Add(alias);
            }
        }
        candidates.Add(role);

        return GetBitmap(candidates.ToArray());
    }

    public static WindowIcon? GetWindowIcon(string role, params string[] fallbackAliases)
    {
        var candidates = new List<string>();

        void TryAddFromImages(string key)
        {
            if (Images.TryGetValue(key, out var customFileName) && !string.IsNullOrWhiteSpace(customFileName))
            {
                candidates.Add(customFileName);
            }
        }

        TryAddFromImages(role);
        if (role.Equals("exe-icon", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("exeIcon", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("main.ico", StringComparison.OrdinalIgnoreCase))
        {
            TryAddFromImages("exe-icon");
            TryAddFromImages("exeIcon");
            TryAddFromImages("main.ico");
        }

        if (fallbackAliases != null)
        {
            foreach (var alias in fallbackAliases)
            {
                TryAddFromImages(alias);
                candidates.Add(alias);
            }
        }
        candidates.Add(role);

        if (!string.IsNullOrEmpty(_resourcesDirectory))
        {
            foreach (var candidate in candidates)
            {
                string extFile = Path.Combine(_resourcesDirectory, candidate);
                if (File.Exists(extFile))
                {
                    try
                    {
                        return new WindowIcon(extFile);
                    }
                    catch { }
                }
            }
        }

        var bmp = GetBitmap(candidates.ToArray());
        if (bmp != null)
        {
            try
            {
                return new WindowIcon(bmp);
            }
            catch { }
        }

        return null;
    }

    public static List<(Bitmap frame, int delayMs)> GetGifAnimation(string role, params string[] fallbackAliases)
    {
        var candidates = new List<string>();
        if (Images.TryGetValue(role, out var customFileName) && !string.IsNullOrWhiteSpace(customFileName))
        {
            candidates.Add(customFileName);
        }
        if (fallbackAliases != null)
        {
            candidates.AddRange(fallbackAliases);
        }
        candidates.Add(role);

        return GetGifFrames(candidates.ToArray());
    }

    public static Bitmap? GetBitmap(params string[] candidates)
    {
        if (candidates == null || candidates.Length == 0)
            return null;

        foreach (var candidate in candidates)
        {
            if (_bitmapCache.TryGetValue(candidate, out var cached))
                return cached;
        }

        if (!string.IsNullOrEmpty(_resourcesDirectory))
        {
            foreach (var candidate in candidates)
            {
                string extFile = Path.Combine(_resourcesDirectory, candidate);
                if (File.Exists(extFile))
                {
                    try
                    {
                        using var stream = File.OpenRead(extFile);
                        var bmp = new Bitmap(stream);
                        foreach (var c in candidates)
                            _bitmapCache[c] = bmp;
                        return bmp;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load bitmap {extFile}: {ex.Message}");
                    }
                }
            }
        }

        try
        {
            string[] manifestNames = CurrentAssembly.GetManifestResourceNames();
            foreach (var candidate in candidates)
            {
                foreach (var name in manifestNames)
                {
                    if (name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        using var stream = CurrentAssembly.GetManifestResourceStream(name);
                        if (stream != null)
                        {
                            var bmp = new Bitmap(stream);
                            foreach (var c in candidates)
                                _bitmapCache[c] = bmp;
                            return bmp;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load embedded resource: {ex.Message}");
        }

        return null;
    }

    public static List<(Bitmap frame, int delayMs)> GetGifFrames(params string[] candidates)
    {
        var frames = new List<(Bitmap frame, int delayMs)>();
        if (candidates == null || candidates.Length == 0)
            return frames;

        byte[]? gifBytes = null;

        if (!string.IsNullOrEmpty(_resourcesDirectory))
        {
            foreach (var candidate in candidates)
            {
                string extFile = Path.Combine(_resourcesDirectory, candidate);
                if (File.Exists(extFile))
                {
                    try
                    {
                        gifBytes = File.ReadAllBytes(extFile);
                        break;
                    }
                    catch { }
                }
            }
        }

        if (gifBytes == null)
        {
            string[] manifestNames = CurrentAssembly.GetManifestResourceNames();
            foreach (var candidate in candidates)
            {
                foreach (var name in manifestNames)
                {
                    if (name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
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
                if (gifBytes != null) break;
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
            System.Diagnostics.Debug.WriteLine($"Failed to decode GIF: {ex.Message}");
        }

        return frames;
    }

    #endregion
}
