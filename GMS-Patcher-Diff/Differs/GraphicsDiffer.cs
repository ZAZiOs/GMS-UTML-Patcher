using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace GMS_Patcher_Diff.Differs;

public class GraphicsDifferResult
{
    public int ExportedCount { get; set; }
    public Dictionary<string, object> ChangeProps { get; set; } = new();
}

public class GraphicsDiffer
{
    public static GraphicsDifferResult Process(UndertaleData original, UndertaleData modded, string outputDir, bool verbose)
    {
        var result = new GraphicsDifferResult();
        string graphicsDir = Path.Combine(outputDir, "graphics");
        Directory.CreateDirectory(graphicsDir);

        var origSpriteMap = new Dictionary<string, UndertaleSprite>(StringComparer.Ordinal);
        foreach (var spr in original.Sprites)
        {
            if (spr.Name?.Content != null)
                origSpriteMap[spr.Name.Content] = spr;
        }

        var spritesToExport = new List<UndertaleSprite>();
        using var workerOrig = new TextureWorker();
        using var workerMod = new TextureWorker();

        foreach (var modSpr in modded.Sprites)
        {
            if (modSpr.Name?.Content == null) continue;
            string name = modSpr.Name.Content;

            if (!origSpriteMap.TryGetValue(name, out var origSpr))
            {
                spritesToExport.Add(modSpr);
                if (verbose) Console.WriteLine($"  [GRAPHICS] New sprite: {name}");
            }
            else
            {
                bool differs = false;

                if (modSpr.Textures.Count != origSpr.Textures.Count ||
                    modSpr.Width != origSpr.Width ||
                    modSpr.Height != origSpr.Height ||
                    modSpr.OriginX != origSpr.OriginX ||
                    modSpr.OriginY != origSpr.OriginY ||
                    modSpr.MarginLeft != origSpr.MarginLeft ||
                    modSpr.MarginRight != origSpr.MarginRight ||
                    modSpr.MarginTop != origSpr.MarginTop ||
                    modSpr.MarginBottom != origSpr.MarginBottom ||
                    modSpr.BBoxMode != origSpr.BBoxMode ||
                    modSpr.SepMasks != origSpr.SepMasks)
                {
                    differs = true;
                }
                else
                {
                    // Compare texture page item image pixels
                    for (int i = 0; i < modSpr.Textures.Count; i++)
                    {
                        var modItem = modSpr.Textures[i]?.Texture;
                        var origItem = origSpr.Textures[i]?.Texture;

                        if (modItem == null && origItem == null) continue;
                        if (modItem == null || origItem == null)
                        {
                            differs = true;
                            break;
                        }

                        if (modItem.BoundingWidth != origItem.BoundingWidth ||
                            modItem.BoundingHeight != origItem.BoundingHeight ||
                            modItem.TargetWidth != origItem.TargetWidth ||
                            modItem.TargetHeight != origItem.TargetHeight)
                        {
                            differs = true;
                            break;
                        }

                        // Fast check: identical coords on identical texture page
                        if (modItem.SourceX == origItem.SourceX &&
                            modItem.SourceY == origItem.SourceY &&
                            modItem.SourceWidth == origItem.SourceWidth &&
                            modItem.SourceHeight == origItem.SourceHeight &&
                            modItem.TargetX == origItem.TargetX &&
                            modItem.TargetY == origItem.TargetY &&
                            AreTexturesByteIdentical(modItem.TexturePage, origItem.TexturePage))
                        {
                            continue;
                        }

                        // Pixel-accurate comparison
                        try
                        {
                            using var imgOrig = workerOrig.GetTextureFor(origItem, name, false);
                            using var imgMod = workerMod.GetTextureFor(modItem, name, false);
                            if (imgOrig.Signature != imgMod.Signature)
                            {
                                differs = true;
                                break;
                            }
                        }
                        catch
                        {
                            differs = true;
                            break;
                        }
                    }
                }

                if (differs)
                {
                    spritesToExport.Add(modSpr);
                    if (verbose) Console.WriteLine($"  [GRAPHICS] Modified sprite: {name}");

                    // Record custom properties if origin/bbox changed
                    if (modSpr.OriginX != origSpr.OriginX ||
                        modSpr.OriginY != origSpr.OriginY ||
                        modSpr.Width != origSpr.Width ||
                        modSpr.Height != origSpr.Height ||
                        modSpr.MarginLeft != origSpr.MarginLeft ||
                        modSpr.MarginRight != origSpr.MarginRight ||
                        modSpr.MarginTop != origSpr.MarginTop ||
                        modSpr.MarginBottom != origSpr.MarginBottom)
                    {
                        var props = new Dictionary<string, object>();
                        props["origin"] = new { x = modSpr.OriginX, y = modSpr.OriginY };
                        props["size"] = new { width = modSpr.Width, height = modSpr.Height };
                        props["margin"] = new
                        {
                            left = modSpr.MarginLeft,
                            right = modSpr.MarginRight,
                            top = modSpr.MarginTop,
                            bottom = modSpr.MarginBottom
                        };
                        result.ChangeProps[name] = props;
                    }
                }
            }
        }

        if (spritesToExport.Count == 0)
        {
            try { Directory.Delete(graphicsDir, true); } catch { }
            return result;
        }

        Console.WriteLine($"Exporting {spritesToExport.Count} changed/new sprite(s) (ExportAllSprites flat + padded)...");

        using TextureWorker worker = new();

        foreach (var sprite in spritesToExport)
        {
            for (int i = 0; i < sprite.Textures.Count; i++)
            {
                var pageItem = sprite.Textures[i]?.Texture;
                if (pageItem != null)
                {
                    // Flat in graphics/ as {sprite.Name.Content}_{i}.png, padded = true
                    string filePath = Path.Combine(graphicsDir, $"{sprite.Name.Content}_{i}.png");
                    try
                    {
                        worker.ExportAsPNG(pageItem, filePath, null, includePadding: true);
                        result.ExportedCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"  Warning: Failed to export sprite {sprite.Name.Content} frame {i}: {ex.Message}");
                    }
                }
            }
        }

        return result;
    }

    private static bool AreTexturesByteIdentical(UndertaleEmbeddedTexture? a, UndertaleEmbeddedTexture? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        try
        {
            var imgA = a.TextureData?.Image;
            var imgB = b.TextureData?.Image;
            if (imgA != null && imgB != null)
                return imgA.GetRawImageData().SequenceEqual(imgB.GetRawImageData());
        }
        catch { }
        return false;
    }
}
