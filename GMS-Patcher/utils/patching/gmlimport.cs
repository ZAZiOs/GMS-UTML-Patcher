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
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using Underanalyzer;
using Underanalyzer.Decompiler;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

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
        switch (config.importType.ToLower())
        {
            case "basic":
            {
                string? basePath = PatchFile.GetRelativePath(config.directory);
                string modifiedPath = Path.Combine(basePath!, "modified");

                string[] dirFiles;

                if (Directory.Exists(modifiedPath) &&
                    Directory.GetFiles(modifiedPath, "*.gml", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    dirFiles = Directory.GetFiles(modifiedPath, "*.gml", SearchOption.TopDirectoryOnly);
                }
                else
                {
                    if (!Directory.Exists(basePath))
                    {
                        Out.ERROR("CODE", "yellow", 341, "The import directory does not exist");
                        return 341; 
                    }

                    dirFiles = Directory.GetFiles(basePath, "*.gml", SearchOption.TopDirectoryOnly);

                    if (dirFiles.Length == 0)
                    {
                        Out.ERROR("CODE", "yellow", 342, "The selected folder doesn't contain any GML files");
                        return 342;
                    }
                }

                return BasicGMLImport.ImportGML(Data, dirFiles, config);
            }
            case "merge":
                /*if (false Program.arguments.SkipHashCheck || Program.arguments.SkipTimestampCheck)
                {
                    return MergeGMLImport.Import(Data, config);
                    // MERGE MODE IS DISABLED BECAUSE OF ITS UNSTABILLITY
                }
                else*/
                {
                    string modifiedPath = Path.Combine(PatchFile.GetRelativePath(config.directory)!, "modified");

                    if (!Directory.Exists(modifiedPath))
                    {
                        Out.ERROR("CODE", "yellow", 343, $"Fallback failed: missing modified folder at {modifiedPath}");
                        return 343;
                    }

                    string[] modifiedFiles = Directory.GetFiles(modifiedPath, "*.gml", SearchOption.TopDirectoryOnly);

                    if (modifiedFiles.Length == 0)
                    {
                        Out.ERROR("CODE", "yellow", 7, "Fallback failed: modified folder is empty");
                        return 344;
                    }

                    return BasicGMLImport.ImportGML(Data, modifiedFiles, config);
                }
            default:
                Out.ERROR("CODE", "yellow", 346, $"Unknown import type: {config.importType} [base / merge] allowed");
                return 346;
        }
    }
}

public class BasicGMLImport
{
    public static int ImportGML(UndertaleData Data, string[] dirFiles, GMLImportConfig config)
    {
        UndertaleModLib.Compiler.CodeImportGroup importGroup = new UndertaleModLib.Compiler.CodeImportGroup(Data);
        int importedCount = 0;
        var importFolder = PatchFile.GetRelativePath(config.directory);

        Out.INFO("CODE", "yellow", "Starting import in basic mode");
        Out.INFO("CODE", "yellow", $"Import from: {importFolder} ({dirFiles.Length} files)");

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
                Out.ERROR("CODE", "yellow", 0, $"Failed to read file {Path.GetFileName(file)}: {ex.Message}");
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
            Out.ERROR("CODE", "yellow", 347, $"Import failed: {ex.Message}");
            return 347;
        }
    }
}

public class MergeConflictException : Exception
{
    public MergeConflictException(string resourceName)
        : base($"Merge conflict detected in resource: {resourceName}")
    {}
}

public static class MergeGMLImport
{
    public static int Import(UndertaleData Data, GMLImportConfig config)
    {
        string origPath = Path.Combine(PatchFile.GetRelativePath(config.directory)!, "original");
        string modPath = Path.Combine(PatchFile.GetRelativePath(config.directory)!, "modified");

        if (!Directory.Exists(origPath))
        {
            Out.ERROR("MERGE", "yellow", 345, $"Missing original folder: {origPath}");
            return 345;
        }

        if (!Directory.Exists(modPath))
        {
            Out.ERROR("MERGE", "yellow", 6, $"Missing modified folder: {modPath}");
            return 343;
        }

        return MergeGMLImport.ImportMerged(Data, origPath, modPath, config);
    }

    public static int ImportMerged(UndertaleData Data, string originalDir, string modifiedDir, GMLImportConfig config)
    {
        string debugPath = Path.Combine(PatchFile.GetRelativePath(config.directory)!, "debug");

        var importGroup = new CodeImportGroup(Data);
        int mergedCount = 0;

        var files = Directory.GetFiles(modifiedDir, "*.gml");

        foreach (var modFile in files)
        {
            string scriptName = Path.GetFileNameWithoutExtension(modFile);
            var codeEntry = Data.Code.FirstOrDefault(c => c.Name.Content == scriptName);
            if (codeEntry == null)
            {
                if (config.debug)
                    Out.VERBOSE("MERGE", "yellow", $"Skipping '{scriptName}': not found in Data");
                continue;
            }

            string modCode = File.ReadAllText(modFile);
            string origFile = Path.Combine(originalDir, scriptName + ".gml");
            if (!File.Exists(origFile))
            {
                if (config.debug)
                    Out.VERBOSE("MERGE", "yellow", $"Skipping '{scriptName}': not found in original");
                continue;
            }

            string baseCode = File.ReadAllText(origFile);
            string currentCode = DecompileCodeEntry(codeEntry, Data);

            string mergedCode = TryMerge(baseCode, currentCode, modCode);

            if (config.debug)
            {
                string debugFile = Path.Combine(debugPath, scriptName + ".gml");
                Directory.CreateDirectory(debugPath);
                File.WriteAllText(debugFile, mergedCode);
                Out.VERBOSE("MERGE", "yellow", $"Debug file written: {debugFile}");
            }

            if (mergedCode.Contains("<<<<<<<") && !config.debug) throw new MergeConflictException(scriptName);

            importGroup.QueueReplace(scriptName, mergedCode);
            mergedCount++;

            if (config.debug)
                Out.VERBOSE("MERGE", "yellow", $"Merged '{scriptName}'");
        }

        try
        {
            importGroup.Import();
            Out.SUCCESS("MERGE", "yellow", $"Successfully merged {mergedCount} GML files.");
            return 0;
        }
        catch (Exception ex)
        {
            Out.ERROR("MERGE", "yellow", 347, $"Import failed: {ex.Message}");
            return 347;
        }
    }

    private static string DecompileCodeEntry(UndertaleCode code, UndertaleData Data)
    {
        var globalContext = new GlobalDecompileContext(Data);
        var settings = Data.ToolInfo.DecompilerSettings;
        var ctx = new Underanalyzer.Decompiler.DecompileContext(globalContext, code, settings);
        return ctx.DecompileToString();
    }


    public static string TryMerge(string baseCode, string currentCode, string modifiedCode)
    {
        var differ = new SideBySideDiffBuilder(new Differ());

        var diffCurrent = differ.BuildDiffModel(baseCode, currentCode);
        var diffModified = differ.BuildDiffModel(baseCode, modifiedCode);

        var result = new List<string>();
        int lineCount = Math.Max(
            diffCurrent.NewText.Lines.Count,
            diffModified.NewText.Lines.Count);

        for (int i = 0; i < lineCount; i++)
        {
            string baseLine = GetLine(diffCurrent.OldText.Lines, i);
            string currLine = GetLine(diffCurrent.NewText.Lines, i);
            string modLine  = GetLine(diffModified.NewText.Lines, i);

            // No changes
            if (currLine == baseLine && modLine == baseLine)
            {
                result.Add(baseLine);
            }
            // Only modified changed
            else if (currLine == baseLine && modLine != baseLine)
            {
                result.Add(modLine);
            }
            // Only current changed
            else if (modLine == baseLine && currLine != baseLine)
            {
                result.Add(currLine);
            }
            // Both changed the same way
            else if (modLine == currLine)
            {
                result.Add(modLine);
            }
            // Conflict
            else
            {
                result.Add("<<<<<<< CURRENT");
                result.Add(currLine);
                result.Add("=======");
                result.Add(modLine);
                result.Add(">>>>>>> MODIFIED");
            }
        }

        return string.Join("\n", result);
    }

    private static string GetLine(IReadOnlyList<DiffPiece> lines, int index)
    {
        return (index < lines.Count) ? lines[index].Text ?? "" : "";
    }
}
