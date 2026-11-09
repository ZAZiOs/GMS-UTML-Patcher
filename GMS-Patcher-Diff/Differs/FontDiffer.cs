using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace GMS_Patcher_Diff.Differs;

public class FontDiffer
{
    public static int Process(UndertaleData original, UndertaleData modded, string outputDir, bool verbose)
    {
        string fontsDir = Path.Combine(outputDir, "fonts");
        Directory.CreateDirectory(fontsDir);

        var origFontMap = new Dictionary<string, UndertaleFont>(StringComparer.Ordinal);
        foreach (var f in original.Fonts)
        {
            if (f.Name?.Content != null)
                origFontMap[f.Name.Content] = f;
        }

        var fontsToExport = new List<UndertaleFont>();
        using var workerOrig = new TextureWorker();
        using var workerMod = new TextureWorker();

        foreach (var modFont in modded.Fonts)
        {
            if (modFont.Name?.Content == null) continue;
            string name = modFont.Name.Content;

            if (!origFontMap.TryGetValue(name, out var origFont))
            {
                fontsToExport.Add(modFont);
                if (verbose) Console.WriteLine($"  [FONTS] New font: {name}");
            }
            else
            {
                bool differs = false;

                if (modFont.DisplayName?.Content != origFont.DisplayName?.Content ||
                    modFont.EmSize != origFont.EmSize ||
                    modFont.Bold != origFont.Bold ||
                    modFont.Italic != origFont.Italic ||
                    modFont.RangeStart != origFont.RangeStart ||
                    modFont.RangeEnd != origFont.RangeEnd ||
                    modFont.Glyphs.Count != origFont.Glyphs.Count)
                {
                    differs = true;
                }
                else
                {
                    var modItem = modFont.Texture;
                    var origItem = origFont.Texture;

                    if (modItem != null && origItem != null)
                    {
                        if (modItem.BoundingWidth != origItem.BoundingWidth ||
                            modItem.BoundingHeight != origItem.BoundingHeight ||
                            modItem.TargetWidth != origItem.TargetWidth ||
                            modItem.TargetHeight != origItem.TargetHeight)
                        {
                            differs = true;
                        }
                        else if (modItem.SourceX == origItem.SourceX &&
                                 modItem.SourceY == origItem.SourceY &&
                                 modItem.SourceWidth == origItem.SourceWidth &&
                                 modItem.SourceHeight == origItem.SourceHeight &&
                                 modItem.TargetX == origItem.TargetX &&
                                 modItem.TargetY == origItem.TargetY &&
                                 modItem.TexturePage == origItem.TexturePage)
                        {
                            // Identical
                        }
                        else
                        {
                            try
                            {
                                using var imgOrig = workerOrig.GetTextureFor(origItem, name, false);
                                using var imgMod = workerMod.GetTextureFor(modItem, name, false);
                                if (imgOrig.Signature != imgMod.Signature)
                                {
                                    differs = true;
                                }
                            }
                            catch
                            {
                                differs = true;
                            }
                        }
                    }
                    else if (modItem != null || origItem != null)
                    {
                        differs = true;
                    }
                }

                if (differs)
                {
                    fontsToExport.Add(modFont);
                    if (verbose) Console.WriteLine($"  [FONTS] Modified font: {name}");
                }
            }
        }

        if (fontsToExport.Count == 0)
        {
            try { Directory.Delete(fontsDir, true); } catch { }
            return 0;
        }

        Console.WriteLine($"Exporting {fontsToExport.Count} changed/new font(s)...");

        using TextureWorker worker = new();
        int count = 0;

        foreach (var font in fontsToExport)
        {
            if (font.Texture != null)
            {
                string outPath = Path.Combine(fontsDir, $"{font.Name.Content}.png");
                try
                {
                    worker.ExportAsPNG(font.Texture, outPath, null, includePadding: true);
                    count++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Warning: Failed to export font texture {font.Name.Content}: {ex.Message}");
                }
            }

            // Export glyphs CSV required by GMS-UTML-Patcher font importer
            try
            {
                string csvPath = Path.Combine(fontsDir, $"glyphs_{font.Name.Content}.csv");
                using var writer = new StreamWriter(csvPath);
                writer.WriteLine($"\"{font.DisplayName?.Content ?? font.Name.Content}\";{font.EmSize};{font.Bold};{font.Italic};{font.Charset};{font.AntiAliasing};{font.ScaleX};{font.ScaleY}");
                writer.WriteLine($"{font.RangeStart}");
                foreach (var g in font.Glyphs)
                {
                    writer.WriteLine($"{g.Character};{g.SourceX};{g.SourceY};{g.SourceWidth};{g.SourceHeight};{g.Shift};{g.Offset}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: Failed to export font glyphs {font.Name.Content}: {ex.Message}");
            }
        }

        return count;
    }
}
