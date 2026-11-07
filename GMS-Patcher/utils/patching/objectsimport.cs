using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;

public class ObjectsImportConfig
{
    public required string directory { get; set; }
    public bool replaceExisting { get; set; } = true;
    public bool addNew { get; set; } = true;
}

public class ObjectsImporter
{
    public static int Import(UndertaleData Data, ObjectsImportConfig config)
    {
        string? folder = PatchFile.GetRelativePath(config.directory);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Out.ERROR("OBJECTS", "cyan", 361, $"The objects import directory does not exist: {config.directory}");
            return 361;
        }

        string[] objectFiles = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
        if (objectFiles.Length == 0)
        {
            Out.ERROR("OBJECTS", "cyan", 362, $"No .json files found in directory: {folder}");
            return 362;
        }

        Out.INFO("OBJECTS", "cyan", $"Found {objectFiles.Length} game object file(s) in: {folder}");

        int importedCount = 0;

        foreach (string file in objectFiles)
        {
            try
            {
                string json = File.ReadAllText(file);
                var obj = ObjectFile.DeserializeFromJson(
                    json,
                    Data,
                    config.replaceExisting,
                    config.addNew,
                    out bool isNewObject
                );

                importedCount++;
                string status = isNewObject ? "added" : "updated";
                Out.INFO("OBJECTS", "cyan", $"Object '{obj.Name.Content}' {status} from {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Out.ERROR("OBJECTS", "cyan", 363, $"Failed to import object from '{file}': {ex.Message}");
                return 363;
            }
        }

        Out.SUCCESS("OBJECTS", "cyan", $"Successfully imported {importedCount} game object(s).");
        return 0;
    }
}

