using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace GMS_Patcher_Diff.Differs;

public class ShaderDiffer
{
    public static int Process(UndertaleData original, UndertaleData modded, string outputDir, bool verbose)
    {
        string shadersDir = Path.Combine(outputDir, "shaders");
        Directory.CreateDirectory(shadersDir);

        var origShaderMap = new Dictionary<string, UndertaleShader>(StringComparer.Ordinal);
        foreach (var sh in original.Shaders)
        {
            if (sh.Name?.Content != null)
                origShaderMap[sh.Name.Content] = sh;
        }

        var shadersToExport = new List<UndertaleShader>();

        foreach (var modSh in modded.Shaders)
        {
            if (modSh.Name?.Content == null) continue;
            string name = modSh.Name.Content;

            if (!origShaderMap.TryGetValue(name, out var origSh))
            {
                shadersToExport.Add(modSh);
                if (verbose) Console.WriteLine($"  [SHADERS] New shader: {name}");
            }
            else
            {
                bool differs = false;

                if (modSh.Type != origSh.Type ||
                    modSh.Version != origSh.Version ||
                    (modSh.GLSL_ES_Vertex?.Content ?? modSh.GLSL_Vertex?.Content) != (origSh.GLSL_ES_Vertex?.Content ?? origSh.GLSL_Vertex?.Content) ||
                    (modSh.GLSL_ES_Fragment?.Content ?? modSh.GLSL_Fragment?.Content) != (origSh.GLSL_ES_Fragment?.Content ?? origSh.GLSL_Fragment?.Content) ||
                    modSh.HLSL9_Vertex?.Content != origSh.HLSL9_Vertex?.Content ||
                    modSh.HLSL9_Fragment?.Content != origSh.HLSL9_Fragment?.Content)
                {
                    differs = true;
                }
                else
                {
                    byte[]? modVBin = modSh.HLSL11_VertexData?.Data;
                    byte[]? origVBin = origSh.HLSL11_VertexData?.Data;
                    if (modVBin != null || origVBin != null)
                    {
                        if (modVBin == null || origVBin == null || !modVBin.SequenceEqual(origVBin))
                            differs = true;
                    }

                    byte[]? modPBin = modSh.HLSL11_PixelData?.Data;
                    byte[]? origPBin = origSh.HLSL11_PixelData?.Data;
                    if (modPBin != null || origPBin != null)
                    {
                        if (modPBin == null || origPBin == null || !modPBin.SequenceEqual(origPBin))
                            differs = true;
                    }
                }

                if (differs)
                {
                    shadersToExport.Add(modSh);
                    if (verbose) Console.WriteLine($"  [SHADERS] Modified shader: {name}");
                }
            }
        }

        if (shadersToExport.Count == 0)
        {
            try { Directory.Delete(shadersDir, true); } catch { }
            return 0;
        }

        Console.WriteLine($"Exporting {shadersToExport.Count} changed/new shader(s)...");

        int count = 0;
        foreach (var sh in shadersToExport)
        {
            string shDir = Path.Combine(shadersDir, sh.Name.Content);
            Directory.CreateDirectory(shDir);

            string? vGlsl = sh.GLSL_ES_Vertex?.Content ?? sh.GLSL_Vertex?.Content;
            if (!string.IsNullOrEmpty(vGlsl))
                File.WriteAllText(Path.Combine(shDir, "vertex.glsl"), vGlsl);

            string? fGlsl = sh.GLSL_ES_Fragment?.Content ?? sh.GLSL_Fragment?.Content;
            if (!string.IsNullOrEmpty(fGlsl))
                File.WriteAllText(Path.Combine(shDir, "fragment.glsl"), fGlsl);

            if (!string.IsNullOrEmpty(sh.HLSL9_Vertex?.Content))
                File.WriteAllText(Path.Combine(shDir, "vertex.hlsl"), sh.HLSL9_Vertex.Content);

            if (!string.IsNullOrEmpty(sh.HLSL9_Fragment?.Content))
                File.WriteAllText(Path.Combine(shDir, "pixel.hlsl"), sh.HLSL9_Fragment.Content);

            if (sh.HLSL11_VertexData?.Data != null && !sh.HLSL11_VertexData.IsNull)
                File.WriteAllBytes(Path.Combine(shDir, "vertex.cso"), sh.HLSL11_VertexData.Data);

            if (sh.HLSL11_PixelData?.Data != null && !sh.HLSL11_PixelData.IsNull)
                File.WriteAllBytes(Path.Combine(shDir, "pixel.cso"), sh.HLSL11_PixelData.Data);

            var meta = new
            {
                type = sh.Type.ToString(),
                version = sh.Version,
                attributes = sh.VertexShaderAttributes?.Select(a => a.Name?.Content ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>()
            };

            string metaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(shDir, "shader.json"), metaJson);

            count++;
        }

        return count;
    }
}

