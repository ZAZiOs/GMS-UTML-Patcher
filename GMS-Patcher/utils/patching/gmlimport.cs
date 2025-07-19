using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

public class GMLImportConfig
{
    public required string directory { get; set; }
    public bool linkCode { get; set; } = true;
    public string importType { get; set; } = "basic";
    public bool debug { get; set; } = false;
}

public class GMLImporter
{
    public static int Import(UndertaleData Data, GMLImportConfig config)
    {
        if (string.IsNullOrEmpty(config.directory))
        {
            Out.ERROR("CODE", "yellow", 1, "Import directory is not set");
            return 1;
        }
        
        string[] dirFiles = Directory.GetFiles(PatchFile.GetRelativePath(config.directory));

        if (dirFiles.Length == 0)
        {
            Out.ERROR("CODE", "yellow", 2, "The selected folder is empty");
            return 2;
        }
        else if (!dirFiles.Any(x => x.EndsWith(".gml", StringComparison.OrdinalIgnoreCase)))
        {
            Out.ERROR("CODE", "yellow", 3, "The selected folder doesn't contain any GML files");
            return 3;
        }

        switch (config.importType.ToLower())
        {
            case "basic":
                return BasicGMLImport.ImportGML(Data, dirFiles, config);
            default:
                Out.ERROR("CODE", "yellow", 4, $"Unknown import type: {config.importType}");
                return 4;
        }
    }
}

public class BasicGMLImport
{
    public static int ImportGML(UndertaleData Data, string[] dirFiles, GMLImportConfig config)
    {
        UndertaleModLib.Compiler.CodeImportGroup importGroup = new UndertaleModLib.Compiler.CodeImportGroup(Data);
        int importedCount = 0;

        Out.INFO("CODE", "yellow", $"Starting import of {dirFiles.Length} GML files...");

        foreach (string file in dirFiles.Where(f => f.EndsWith(".gml", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                string code = File.ReadAllText(file);
                string codeName = Path.GetFileNameWithoutExtension(file);
                importGroup.QueueReplace(codeName, code);
                importedCount++;
                if (config.debug)
                    Out.VERBOSE("CODE", "yellow", $"Queued for import: {codeName}");
            }
            catch (Exception ex)
            {
                Out.ERROR("CODE", "yellow", 100, $"Failed to read file {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        try
        {
            importGroup.Import();
            Out.SUCCESS("CODE", "yellow", $"Successfully imported {importedCount} GML files");
            return 0;
        }
        catch (Exception ex)
        {
            Out.ERROR("CODE", "yellow", 101, $"Import failed: {ex.Message}");
            return 101;
        }
    }
}