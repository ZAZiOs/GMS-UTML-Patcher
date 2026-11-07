// Script for UndertaleModTool to import shaders
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UndertaleModLib;
using UndertaleModLib.Models;

EnsureDataLoaded();

string importFolder = PromptChooseDirectory();
if (string.IsNullOrEmpty(importFolder))
    throw new ScriptException("The import folder was not set.");

string[] shaderDirs = Directory.GetDirectories(importFolder);
if (shaderDirs.Length == 0)
    throw new ScriptException("The selected folder contains no shader subdirectories.");

bool replaceExisting = ScriptQuestion("Replace existing shaders if they already exist in data.win?");
bool addNew = ScriptQuestion("Add new shaders if they don't exist yet?");

SetProgressBar(null, "Importing Shaders", 0, shaderDirs.Length);
StartProgressBarUpdater();

SyncBinding("Shaders, Strings", true);

int importedCount = 0;
await Task.Run(() =>
{
    foreach (string dir in shaderDirs)
    {
        string shaderName = Path.GetFileName(dir);
        UndertaleShader existing = Data.Shaders.ByName(shaderName);
        UndertaleShader shader;

        if (existing != null)
        {
            if (!replaceExisting)
                continue;
            shader = existing;
        }
        else
        {
            if (!addNew)
                continue;

            shader = new UndertaleShader
            {
                Name = Data.Strings.MakeString(shaderName),
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
            Data.Shaders.Add(shader);
        }

        string metaFile = Path.Combine(dir, "shader.json");
        if (File.Exists(metaFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(metaFile));
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var tProp) && Enum.TryParse<UndertaleShader.ShaderType>(tProp.GetString(), true, out var st))
                    shader.Type = st;
                if (root.TryGetProperty("version", out var vProp) && vProp.TryGetInt32(out var ver))
                    shader.Version = ver;
                if (root.TryGetProperty("attributes", out var attrProp) && attrProp.ValueKind == JsonValueKind.Array)
                {
                    shader.VertexShaderAttributes.Clear();
                    foreach (var elem in attrProp.EnumerateArray())
                    {
                        string str = elem.GetString();
                        if (!string.IsNullOrEmpty(str))
                            shader.VertexShaderAttributes.Add(new UndertaleShader.VertexShaderAttribute { Name = Data.Strings.MakeString(str) });
                    }
                }
            }
            catch { }
        }

        string vGlsl = FindFile(dir, "vertex.glsl", "vert.glsl");
        string fGlsl = FindFile(dir, "fragment.glsl", "frag.glsl", "pixel.glsl");
        string vHlsl = FindFile(dir, "vertex.hlsl");
        string fHlsl = FindFile(dir, "fragment.hlsl", "pixel.hlsl");
        string vBin = FindFile(dir, "vertex.cso", "vertex.bin");
        string pBin = FindFile(dir, "pixel.cso", "pixel.bin");

        if (vGlsl != null)
        {
            string code = File.ReadAllText(vGlsl);
            shader.GLSL_ES_Vertex = Data.Strings.MakeString(code);
            shader.GLSL_Vertex = Data.Strings.MakeString(code);
        }
        if (fGlsl != null)
        {
            string code = File.ReadAllText(fGlsl);
            shader.GLSL_ES_Fragment = Data.Strings.MakeString(code);
            shader.GLSL_Fragment = Data.Strings.MakeString(code);
        }
        if (vHlsl != null)
            shader.HLSL9_Vertex = Data.Strings.MakeString(File.ReadAllText(vHlsl));
        if (fHlsl != null)
            shader.HLSL9_Fragment = Data.Strings.MakeString(File.ReadAllText(fHlsl));
        if (vBin != null)
            shader.HLSL11_VertexData = new UndertaleShader.UndertaleRawShaderData { Data = File.ReadAllBytes(vBin), IsNull = false };
        if (pBin != null)
            shader.HLSL11_PixelData = new UndertaleShader.UndertaleRawShaderData { Data = File.ReadAllBytes(pBin), IsNull = false };

        importedCount++;
        IncrementProgress();
    }
});

DisableAllSyncBindings();

await StopProgressBarUpdater();
HideProgressBar();

ScriptMessage($"All {importedCount} shader(s) successfully imported!");

string FindFile(string dir, params string[] names)
{
    foreach (var n in names)
    {
        string p = Path.Combine(dir, n);
        if (File.Exists(p)) return p;
    }
    return null;
}

