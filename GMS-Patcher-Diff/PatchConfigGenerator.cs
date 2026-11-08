using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using GMS_Patcher_Diff.Differs;

namespace GMS_Patcher_Diff;

public class PatchConfigGenerator
{
    public static void Generate(
        string outputPath,
        string gameName,
        bool isGms2,
        bool isYyc,
        ulong buildTimestamp,
        string sha256,
        int codeCount,
        GraphicsDifferResult graphicsResult,
        int audioCount,
        int fontCount,
        int objectCount,
        int roomCount,
        int shaderCount,
        SettingsImportConfig? settingsConfig)
    {
        var root = new Dictionary<string, object>();

        // 1. checks (always present)
        root["checks"] = new Dictionary<string, object>
        {
            ["Name"] = gameName,
            ["GMS2"] = isGms2,
            ["YYC"] = isYyc,
            ["buildTimestamp"] = buildTimestamp,
            ["SHA256"] = sha256
        };

        // 2. gml
        if (codeCount > 0)
        {
            root["gml"] = new Dictionary<string, object>
            {
                ["directory"] = "code/",
                ["linkCode"] = true
            };
        }

        // 3. graphics
        if (graphicsResult.ExportedCount > 0)
        {
            var gfx = new Dictionary<string, object>
            {
                ["directory"] = "graphics/",
                ["importUnknownAsSprite"] = true,
                ["sprFrameRegex"] = "^(.+?)(?:_(\\d+))$",
                ["tempFolder"] = "graphics-temp/",
                ["searchPattern"] = "*.png",
                ["textureSize"] = 2048,
                ["paddingBetweenImages"] = 2,
                ["verboseLevel"] = 1
            };

            if (graphicsResult.ChangeProps.Count > 0)
            {
                gfx["changeProps"] = graphicsResult.ChangeProps;
            }

            root["graphics"] = gfx;
        }

        // 4. audio
        if (audioCount > 0)
        {
            root["audio"] = new Dictionary<string, object>
            {
                ["directory"] = "audio/",
                ["replace_properties"] = true,
                ["embed"] = true,
                ["decode"] = false,
                ["use_folder_as_audiogroup"] = false,
                ["manually_configure_each"] = false
            };
        }

        // 5. fonts
        if (fontCount > 0)
        {
            root["fonts"] = new Dictionary<string, object>
            {
                ["directory"] = "fonts/",
                ["tempFolder"] = "fonts-temp/",
                ["searchPattern"] = "*.png",
                ["textureSize"] = 2048,
                ["paddingBetweenImages"] = 2
            };
        }

        // 6. objects (v1.3.0)
        if (objectCount > 0)
        {
            root["objects"] = new Dictionary<string, object>
            {
                ["directory"] = "objects/",
                ["replaceExisting"] = true,
                ["addNew"] = true
            };
        }

        // 7. rooms (v1.3.0)
        if (roomCount > 0)
        {
            root["rooms"] = new Dictionary<string, object>
            {
                ["directory"] = "rooms/",
                ["replaceExisting"] = true,
                ["addNew"] = true,
                ["compileCode"] = true
            };
        }

        // 8. shaders (v1.3.1)
        if (shaderCount > 0)
        {
            root["shaders"] = new Dictionary<string, object>
            {
                ["directory"] = "shaders/",
                ["replaceExisting"] = true,
                ["addNew"] = true
            };
        }

        // 9. settings (v1.3.1)
        if (settingsConfig != null)
        {
            root["settings"] = settingsConfig;
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        string json = JsonSerializer.Serialize(root, options);
        File.WriteAllText(outputPath, json);
    }
}
