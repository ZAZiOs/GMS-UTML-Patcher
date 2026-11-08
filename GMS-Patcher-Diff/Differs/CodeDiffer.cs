using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Underanalyzer.Decompiler;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace GMS_Patcher_Diff.Differs;

public class CodeDiffer
{
    public static int Process(UndertaleData original, UndertaleData modded, string outputDir, bool verbose)
    {
        string codeModifiedDir = Path.Combine(outputDir, "code", "modified");
        Directory.CreateDirectory(codeModifiedDir);

        var origCodeMap = new Dictionary<string, UndertaleCode>(StringComparer.Ordinal);
        foreach (var c in original.Code)
        {
            if (c.Name?.Content != null)
                origCodeMap[c.Name.Content] = c;
        }

        var scriptsToExport = new List<(UndertaleCode code, string? preDecompiled)>();
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

            if (!origCodeMap.TryGetValue(name, out var origCode))
            {
                // New script
                scriptsToExport.Add((modCode, null));
                if (verbose) Console.WriteLine($"  [CODE] New script: {name}");
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

                if (gmlOrig != null && gmlMod != null && gmlOrig == gmlMod)
                {
                    // Code logic is 100% identical
                    continue;
                }

                scriptsToExport.Add((modCode, gmlMod));
                if (verbose) Console.WriteLine($"  [CODE] Modified script: {name}");
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

        foreach (var (code, preDecompiled) in scriptsToExport)
        {
            try
            {
                string decompiled = preDecompiled ??
                                    new DecompileContext(modDecCtx, code, decompilerSettings).DecompileToString();
                string destFile = Path.Combine(codeModifiedDir, $"{code.Name.Content}.gml");
                File.WriteAllText(destFile, decompiled);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: Decompilation failed for {code.Name.Content}: {ex.Message}");
            }
        }

        return scriptsToExport.Count;
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
