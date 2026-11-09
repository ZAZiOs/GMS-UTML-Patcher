using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Underanalyzer.Decompiler;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace GMS_Patcher_Diff.Differs;

public class CodeDiffer
{
    private record CodeExportItem(UndertaleCode Code, string ExportName, string? PreDecompiled);

    public static int Process(UndertaleData original, UndertaleData modded, string outputDir, bool verbose)
    {
        string codeModifiedDir = Path.Combine(outputDir, "code", "modified");
        Directory.CreateDirectory(codeModifiedDir);

        bool crossEngine = !original.IsGameMaker2() && modded.IsGameMaker2();

        // 1. Map of original code entries by exact name
        var origCodeMap = new Dictionary<string, UndertaleCode>(StringComparer.Ordinal);
        foreach (var c in original.Code)
        {
            if (c.Name?.Content != null)
                origCodeMap[c.Name.Content] = c;
        }

        // 2. Map of collision events in original: (objName, targetObjName) -> origCode
        var origCollisionMap = new Dictionary<(string objName, string targetObjName), UndertaleCode>();
        foreach (var obj in original.GameObjects)
        {
            if (obj?.Name?.Content == null || obj.Events == null) continue;
            var colEvents = obj.Events.Count > (int)EventType.Collision ? obj.Events[(int)EventType.Collision] : null;
            if (colEvents == null) continue;
            foreach (var ev in colEvents)
            {
                int targetIdx = (int)ev.EventSubtype;
                if (targetIdx >= 0 && targetIdx < original.GameObjects.Count)
                {
                    string? targetName = original.GameObjects[targetIdx]?.Name?.Content;
                    var code = ev.Actions.FirstOrDefault()?.CodeId;
                    if (targetName != null && code != null)
                    {
                        origCollisionMap[(obj.Name.Content, targetName)] = code;
                    }
                }
            }
        }

        // 3. Map of collision events in modded: modCode -> (objName, targetObjName)
        var modCodeToCollision = new Dictionary<UndertaleCode, (string objName, string targetObjName)>();
        foreach (var obj in modded.GameObjects)
        {
            if (obj?.Name?.Content == null || obj.Events == null) continue;
            var colEvents = obj.Events.Count > (int)EventType.Collision ? obj.Events[(int)EventType.Collision] : null;
            if (colEvents == null) continue;
            foreach (var ev in colEvents)
            {
                int targetIdx = (int)ev.EventSubtype;
                if (targetIdx >= 0 && targetIdx < modded.GameObjects.Count)
                {
                    string? targetName = modded.GameObjects[targetIdx]?.Name?.Content;
                    var code = ev.Actions.FirstOrDefault()?.CodeId;
                    if (targetName != null && code != null)
                    {
                        modCodeToCollision[code] = (obj.Name.Content, targetName);
                    }
                }
            }
        }

        var scriptsToExport = new List<CodeExportItem>();
        GlobalDecompileContext? origDecCtx = null;
        GlobalDecompileContext? modDecCtx = null;

        foreach (var modCode in modded.Code)
        {
            if (modCode.Name?.Content == null) continue;
            // Anonymous lambdas / sub-functions are decompiled as part of their parent
            if (modCode.ParentEntry != null) continue;

            string name = modCode.Name.Content;

            // Skip compiler-generated dummy PreCreate scripts (e.g. gml_Object_foo_PreCreate_0) if only calling event_inherited
            if (name.EndsWith("_PreCreate_0") && IsDummyPreCreateCode(modCode))
                continue;

            // When target is GMS 1.4, skip GMS2 runtime compatibility shims that cannot compile in GMS 1.4
            if (crossEngine)
            {
                if (name.StartsWith("gml_GlobalScript_") || IsGms2CompatibilityScript(name))
                    continue;
            }

            UndertaleCode? origCode = null;
            string exportName = name;

            if (modCodeToCollision.TryGetValue(modCode, out var colInfo))
            {
                // Collision event: resolve against original
                if (origCollisionMap.TryGetValue(colInfo, out var matchedOrigCode))
                {
                    origCode = matchedOrigCode;
                    exportName = matchedOrigCode.Name.Content;
                }
                else
                {
                    // New collision event
                    if (!original.IsGameMaker2())
                    {
                        int tIdx = original.GameObjects.IndexOfName(colInfo.targetObjName);
                        if (tIdx >= 0)
                            exportName = $"gml_Object_{colInfo.objName}_Collision_{tIdx}";
                        else
                            exportName = $"gml_Object_{colInfo.objName}_Collision_{colInfo.targetObjName}";
                    }
                    else
                    {
                        exportName = $"gml_Object_{colInfo.objName}_Collision_{colInfo.targetObjName}";
                    }
                }
            }
            else
            {
                origCodeMap.TryGetValue(name, out origCode);
            }

            if (origCode == null)
            {
                // New script
                scriptsToExport.Add(new CodeExportItem(modCode, exportName, null));
                if (verbose) Console.WriteLine($"  [CODE] New script: {exportName} (from {name})");
            }
            else
            {
                // Quick instruction check
                if (AreInstructionsEqual(origCode, modCode))
                {
                    continue;
                }

                // If instructions differ (e.g. jump targets, compiler optimization, bytecode version diffs),
                // decompile both and compare GML source
                origDecCtx ??= new GlobalDecompileContext(original);
                modDecCtx ??= new GlobalDecompileContext(modded);

                string? gmlOrig = null;
                string? gmlMod = null;
                try
                {
                    gmlOrig = new DecompileContext(origDecCtx, origCode, original.ToolInfo.DecompilerSettings).DecompileToString()?.Trim();
                    gmlMod = new DecompileContext(modDecCtx, modCode, modded.ToolInfo.DecompilerSettings).DecompileToString()?.Trim();
                }
                catch { }

                if (gmlOrig != null && gmlMod != null && AreDecompiledGmlEqual(gmlOrig, gmlMod, crossEngine))
                {
                    // Code logic is 100% identical
                    continue;
                }

                scriptsToExport.Add(new CodeExportItem(modCode, exportName, gmlMod));
                if (verbose) Console.WriteLine($"  [CODE] Modified script: {exportName}");
            }
        }

        if (scriptsToExport.Count == 0)
        {
            // Clean up empty folder
            try { Directory.Delete(Path.Combine(outputDir, "code"), true); } catch { }
            return 0;
        }

        Console.WriteLine($"Exporting {scriptsToExport.Count} changed/new GML scripts...");

        modDecCtx ??= new GlobalDecompileContext(modded);
        var decompilerSettings = modded.ToolInfo.DecompilerSettings;

        foreach (var item in scriptsToExport)
        {
            try
            {
                string decompiled = item.PreDecompiled ??
                                    new DecompileContext(modDecCtx, item.Code, decompilerSettings).DecompileToString();
                string destFile = Path.Combine(codeModifiedDir, $"{item.ExportName}.gml");
                File.WriteAllText(destFile, decompiled);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: Decompilation failed for {item.ExportName}: {ex.Message}");
            }
        }

        return scriptsToExport.Count;
    }

    private static bool IsGms2CompatibilityScript(string name)
    {
        return name.StartsWith("___view_") ||
               name.StartsWith("___background_") ||
               name.StartsWith("___init_") ||
               name.StartsWith("___global_") ||
               name.StartsWith("action_") ||
               name.StartsWith("gml_Object_objLayerChecker");
    }

    private static bool AreDecompiledGmlEqual(string gmlOrig, string gmlMod, bool crossEngine)
    {
        if (gmlOrig == gmlMod) return true;

        if (crossEngine)
        {
            // Normalize line endings
            string normOrig = gmlOrig.Replace("\r\n", "\n").Trim();
            string normMod = gmlMod.Replace("\r\n", "\n").Trim();

            if (normOrig == normMod) return true;

            // In GMS2 conversion, script calls foo(args) became script_execute(foo, args)
            normMod = Regex.Replace(normMod, @"script_execute\s*\(\s*([a-zA-Z0-9_]+)\s*,\s*", "$1(");
            if (normOrig == normMod) return true;
        }

        return false;
    }

    private static bool IsDummyPreCreateCode(UndertaleCode code)
    {
        if (code.Instructions.Count == 0) return true;
        if (code.Instructions.Count == 2 &&
            code.Instructions[0].Kind == UndertaleInstruction.Opcode.Call &&
            code.Instructions[0].ValueFunction?.Name?.Content == "event_inherited" &&
            code.Instructions[1].Kind == UndertaleInstruction.Opcode.Popz)
        {
            return true;
        }
        return false;
    }

    private static bool AreInstructionsEqual(UndertaleCode a, UndertaleCode b)
    {
        if (a.Instructions.Count != b.Instructions.Count) return false;

        for (int i = 0; i < a.Instructions.Count; i++)
        {
            var instA = a.Instructions[i];
            var instB = b.Instructions[i];

            if (instA.Kind != instB.Kind ||
                instA.Type1 != instB.Type1 ||
                instA.Type2 != instB.Type2 ||
                instA.ComparisonKind != instB.ComparisonKind ||
                instA.TypeInst != instB.TypeInst ||
                instA.ArgumentsCount != instB.ArgumentsCount ||
                instA.JumpOffset != instB.JumpOffset ||
                instA.ExtendedKind != instB.ExtendedKind)
            {
                return false;
            }

            if (instA.ValueFunction != null || instB.ValueFunction != null)
            {
                if (instA.ValueFunction?.Name?.Content != instB.ValueFunction?.Name?.Content)
                    return false;
                continue;
            }

            if (instA.ValueVariable != null || instB.ValueVariable != null)
            {
                if (instA.ValueVariable?.Name?.Content != instB.ValueVariable?.Name?.Content)
                    return false;
                continue;
            }

            if (instA.ValueString != null || instB.ValueString != null)
            {
                if (instA.ValueString?.Resource?.Content != instB.ValueString?.Resource?.Content)
                    return false;
                continue;
            }

            if (instA.ValueInt != instB.ValueInt ||
                instA.ValueShort != instB.ValueShort ||
                instA.ValueLong != instB.ValueLong ||
                instA.ValueDouble != instB.ValueDouble)
            {
                return false;
            }
        }

        return true;
    }
}
