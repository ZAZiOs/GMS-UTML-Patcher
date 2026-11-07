using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;

public class ShadersImportConfig
{
    public required string directory { get; set; }
    public bool replaceExisting { get; set; } = true;
    public bool addNew { get; set; } = true;
}

public class ShaderMetadata
{
    public string Type { get; set; } = "GLSL_ES";
    public int Version { get; set; } = 2;
    public List<string> Attributes { get; set; } = new();
}

public class ShadersImporter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static int Import(UndertaleData Data, ShadersImportConfig config)
    {
        string? folder = PatchFile.GetRelativePath(config.directory);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Out.ERROR("SHADERS", "magenta", 371, $"The shaders import directory does not exist: {config.directory}");
            return 371;
        }

        string[] shaderDirs = Directory.GetDirectories(folder);
        if (shaderDirs.Length == 0)
        {
            Out.ERROR("SHADERS", "magenta", 372, $"No shader folders found in directory: {folder}");
            return 372;
        }

        Out.INFO("SHADERS", "magenta", $"Found {shaderDirs.Length} shader folder(s) in: {folder}");

        int importedCount = 0;

        foreach (string shaderDir in shaderDirs)
        {
            string shaderName = Path.GetFileName(shaderDir);
            try
            {
                var shader = ImportShaderFromDirectory(Data, shaderName, shaderDir, config.replaceExisting, config.addNew, out bool isNew);
                if (shader != null)
                {
                    importedCount++;
                    string status = isNew ? "added" : "updated";
                    Out.INFO("SHADERS", "magenta", $"Shader '{shaderName}' {status}");
                }
            }
            catch (Exception ex)
            {
                Out.ERROR("SHADERS", "magenta", 373, $"Failed to import shader '{shaderName}': {ex.Message}");
                return 373;
            }
        }

        Out.SUCCESS("SHADERS", "magenta", $"Successfully imported {importedCount} shader(s).");
        return 0;
    }

    public static UndertaleShader? ImportShaderFromDirectory(
        UndertaleData data,
        string shaderName,
        string dir,
        bool replaceExisting,
        bool addNew,
        out bool isNew)
    {
        isNew = false;
        UndertaleShader? existing = data.Shaders.ByName(shaderName);
        UndertaleShader shader;

        if (existing != null)
        {
            if (!replaceExisting)
                return null;
            shader = existing;
        }
        else
        {
            if (!addNew)
                throw new InvalidOperationException($"Shader '{shaderName}' does not exist and addNew is disabled.");

            shader = new UndertaleShader
            {
                Name = data.Strings.MakeString(shaderName),
                Type = UndertaleShader.ShaderType.GLSL_ES,
                HLSL11_VertexData = new UndertaleShader.UndertaleRawShaderData { IsNull = true },
                HLSL11_PixelData = new UndertaleShader.UndertaleRawShaderData { IsNull = true },
                PSSL_VertexData = new UndertaleShader.UndertaleRawShaderData { IsNull = true },
                PSSL_PixelData = new UndertaleShader.UndertaleRawShaderData { IsNull = true },
                Cg_PSVita_VertexData = new UndertaleShader.UndertaleRawShaderData { IsNull = true },
                Cg_PSVita_PixelData = new UndertaleShader.UndertaleRawShaderData { IsNull = true },
                Cg_PS3_VertexData = new UndertaleShader.UndertaleRawShaderData { IsNull = true },
                Cg_PS3_PixelData = new UndertaleShader.UndertaleRawShaderData { IsNull = true }
            };
            data.Shaders.Add(shader);
            isNew = true;
        }

        string metaFile = Path.Combine(dir, "shader.json");
        ShaderMetadata? meta = null;
        if (File.Exists(metaFile))
        {
            try
            {
                string json = File.ReadAllText(metaFile);
                meta = JsonSerializer.Deserialize<ShaderMetadata>(json, _jsonOptions);
            }
            catch { }
        }

        if (meta != null)
        {
            if (Enum.TryParse<UndertaleShader.ShaderType>(meta.Type, true, out var st))
                shader.Type = st;
            shader.Version = meta.Version;

            if (meta.Attributes != null && meta.Attributes.Count > 0)
            {
                shader.VertexShaderAttributes.Clear();
                foreach (var attr in meta.Attributes)
                {
                    shader.VertexShaderAttributes.Add(new UndertaleShader.VertexShaderAttribute
                    {
                        Name = data.Strings.MakeString(attr)
                    });
                }
            }
        }

        // Look for vertex and fragment / pixel sources
        string? vGlsl = FindFile(dir, "vertex.glsl", "vert.glsl", "shader.vert");
        string? fGlsl = FindFile(dir, "fragment.glsl", "frag.glsl", "pixel.glsl", "shader.frag");
        string? vHlsl = FindFile(dir, "vertex.hlsl", "shader.vhlsl");
        string? fHlsl = FindFile(dir, "fragment.hlsl", "pixel.hlsl", "shader.fhlsl");
        string? vBin = FindFile(dir, "vertex.cso", "vertex.bin");
        string? pBin = FindFile(dir, "pixel.cso", "pixel.bin");

        if (vGlsl != null)
        {
            string code = File.ReadAllText(vGlsl);
            shader.GLSL_ES_Vertex = data.Strings.MakeString(code);
            shader.GLSL_Vertex = data.Strings.MakeString(code);
        }

        if (fGlsl != null)
        {
            string code = File.ReadAllText(fGlsl);
            shader.GLSL_ES_Fragment = data.Strings.MakeString(code);
            shader.GLSL_Fragment = data.Strings.MakeString(code);
        }

        if (vHlsl != null)
        {
            shader.HLSL9_Vertex = data.Strings.MakeString(File.ReadAllText(vHlsl));
        }

        if (fHlsl != null)
        {
            shader.HLSL9_Fragment = data.Strings.MakeString(File.ReadAllText(fHlsl));
        }

        if (vBin != null)
        {
            byte[] bytes = File.ReadAllBytes(vBin);
            shader.HLSL11_VertexData = new UndertaleShader.UndertaleRawShaderData
            {
                Data = bytes,
                IsNull = false
            };
        }

        if (pBin != null)
        {
            byte[] bytes = File.ReadAllBytes(pBin);
            shader.HLSL11_PixelData = new UndertaleShader.UndertaleRawShaderData
            {
                Data = bytes,
                IsNull = false
            };
        }

        return shader;
    }

    private static string? FindFile(string dir, params string[] filenames)
    {
        foreach (var fn in filenames)
        {
            string p = Path.Combine(dir, fn);
            if (File.Exists(p))
                return p;
        }
        return null;
    }
}

