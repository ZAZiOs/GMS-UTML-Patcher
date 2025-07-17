using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;
using ImageMagick;

using System.Reflection;


public class GraphicsImportConfig
{
    public required string directory { get; set; }
    public bool importUnknownAsSprite { get; set; } = false;
    public string sprFrameRegex { get; set; } = "^(.+?)(?:_(\\d+))$";
    public string tempFolder { get; set; } = "graphics-temp/";
    public string searchPattern { get; set; } = "*.png";
    public int textureSize { get; set; } = 2048;
    public int paddingBetweenImages { get; set; } = 2;
    public bool debug { get; set; } = false;

    public Dictionary<string, Dictionary<string, object>> changeProps { get; set; } = new();
}

public class GraphicsImporter
{
    public static GraphicsImportConfig CurrentConfig { get; private set; }
    public static List<MagickImage> imagesToCleanup = new();
    public static int Import(UndertaleData Data, GraphicsImportConfig config)
    {
        bool noMasksForBasicRectangles = Data.IsVersionAtLeast(2022, 9);
        CurrentConfig = config;

        ImportGraphicsBase.sprFrameRegex = new Regex(config.sprFrameRegex, RegexOptions.Compiled);
        string importFolder = ImportGraphicsBase.CheckValidity();
        if (!Directory.Exists(importFolder))
        {
            Console.WriteLine($"[GRAPHICS][ERROR 311] Import folder doesn't exist: {importFolder}");
            return 311;
        }
        string tempFolder = PatchFile.GetRelativePath(config.tempFolder);
        Directory.CreateDirectory(tempFolder);

        string outName = Path.Combine(tempFolder, "atlas.txt");

        ImportGraphicsBase.Packer packer = new ImportGraphicsBase.Packer();
        packer.Process(importFolder, config.searchPattern, config.textureSize, config.paddingBetweenImages, config.debug);
        packer.SaveAtlasses(outName);
        Console.WriteLine($"[GRAPHICS][INFO] Atlases saved to: {outName}");

        int lastTextPage = Data.EmbeddedTextures.Count - 1;
        int lastTextPageItem = Data.TexturePageItems.Count - 1;

        bool bboxMasks = Data.IsVersionAtLeast(2024, 6);
        Dictionary<UndertaleSprite, ImportGraphicsBase.Node> maskNodes = new();

        string prefix = outName.Replace(Path.GetExtension(outName), "");
        int atlasCount = 0;

        foreach (ImportGraphicsBase.Atlas atlas in packer.Atlasses)
        {
            string atlasName = Path.Combine(tempFolder, $"{prefix}{atlasCount:000}.png");
            using MagickImage atlasImage = TextureWorker.ReadBGRAImageFromFile(atlasName);
            IPixelCollection<byte> atlasPixels = atlasImage.GetPixels();

            UndertaleEmbeddedTexture texture = new();

            texture.Name = new UndertaleString($"Texture {++lastTextPage}");
            texture.TextureData.Image = GMImage.FromMagickImage(atlasImage).ConvertToPng();
            Data.EmbeddedTextures.Add(texture);

            foreach (ImportGraphicsBase.Node n in atlas.Nodes)
            {
                if (n.Texture != null)
                {
                    // Initalize values of this texture
                    UndertaleTexturePageItem texturePageItem = new();
                    texturePageItem.Name = new UndertaleString($"PageItem {++lastTextPageItem}");
                    texturePageItem.SourceX = (ushort)n.Bounds.X;
                    texturePageItem.SourceY = (ushort)n.Bounds.Y;
                    texturePageItem.SourceWidth = (ushort)n.Bounds.Width;
                    texturePageItem.SourceHeight = (ushort)n.Bounds.Height;
                    texturePageItem.TargetX = (ushort)n.Texture.TargetX;
                    texturePageItem.TargetY = (ushort)n.Texture.TargetY;
                    texturePageItem.TargetWidth = (ushort)n.Bounds.Width;
                    texturePageItem.TargetHeight = (ushort)n.Bounds.Height;
                    texturePageItem.BoundingWidth = (ushort)n.Texture.BoundingWidth;
                    texturePageItem.BoundingHeight = (ushort)n.Texture.BoundingHeight;
                    texturePageItem.TexturePage = texture;

                    // Add this texture to UMT
                    Data.TexturePageItems.Add(texturePageItem);

                    // String processing
                    string stripped = Path.GetFileNameWithoutExtension(n.Texture.Source);

                    ImportGraphicsBase.SpriteType spriteType = ImportGraphicsBase.GetSpriteType(n.Texture.Source);
                    if (config.importUnknownAsSprite)
                    {
                        if (spriteType == ImportGraphicsBase.SpriteType.Unknown || spriteType == ImportGraphicsBase.SpriteType.Font)
                        {
                            spriteType = ImportGraphicsBase.SpriteType.Sprite;
                        }
                    }

                    if (spriteType == ImportGraphicsBase.SpriteType.Background)
                    {
                        UndertaleBackground background = Data.Backgrounds.ByName(stripped);
                        if (background != null)
                        {
                            background.Texture = texturePageItem;
                        }
                        else
                        {
                            // No background found, let's make one
                            UndertaleString backgroundUTString = Data.Strings.MakeString(stripped);
                            UndertaleBackground newBackground = new();
                            newBackground.Name = backgroundUTString;
                            newBackground.Transparent = false;
                            newBackground.Preload = false;
                            newBackground.Texture = texturePageItem;
                            Data.Backgrounds.Add(newBackground);
                        }
                    }
                    else if (spriteType == ImportGraphicsBase.SpriteType.Sprite)
                    {
                        // Get sprite to add this texture to
                        string spriteName;
                        int frame = 0;
                        try
                        {
                            var spriteParts = ImportGraphicsBase.sprFrameRegex.Match(stripped);
                            spriteName = spriteParts.Groups[1].Value;
                            Int32.TryParse(spriteParts.Groups[2].Value, out frame);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"[GRAPHICS][ERROR] Failed to parse sprite name and frame from '{stripped}': {e.Message}");
                            continue;
                        }

                        if (string.IsNullOrEmpty(spriteName))
                        {
                            Console.WriteLine($"[GRAPHICS][ERROR] Sprite name is empty for file '{stripped}'. Skipping.");
                            continue;
                        }

                        // Create TextureEntry object
                        UndertaleSprite.TextureEntry texentry = new();
                        texentry.Texture = texturePageItem;

                        // Set values for new sprites
                        UndertaleSprite sprite = Data.Sprites.ByName(spriteName);
                        if (sprite is null)
                        {
                            UndertaleString spriteUTString = Data.Strings.MakeString(spriteName);
                            UndertaleSprite newSprite = new();
                            newSprite.Name = spriteUTString;
                            newSprite.Width = (uint)n.Texture.BoundingWidth;
                            newSprite.Height = (uint)n.Texture.BoundingHeight;
                            newSprite.MarginLeft = n.Texture.TargetX;
                            newSprite.MarginRight = n.Texture.TargetX + n.Bounds.Width - 1;
                            newSprite.MarginTop = n.Texture.TargetY;
                            newSprite.MarginBottom = n.Texture.TargetY + n.Bounds.Height - 1;
                            newSprite.OriginX = 0;
                            newSprite.OriginY = 0;
                            if (frame > 0)
                            {
                                for (int i = 0; i < frame; i++)
                                    newSprite.Textures.Add(null);
                            }

                            // Only generate collision masks for sprites that need them (in newer GameMaker versions)
                            if (!noMasksForBasicRectangles ||
                                newSprite.SepMasks is not (UndertaleSprite.SepMaskType.AxisAlignedRect or UndertaleSprite.SepMaskType.RotatedRect))
                            {
                                // Generate mask later (when the current atlas is about to be unloaded)
                                maskNodes.Add(newSprite, n);
                            }

                            ImportGraphicsBase.ApplyCustomFields(newSprite, spriteName);
                            newSprite.Textures.Add(texentry);
                            Data.Sprites.Add(newSprite);
                            continue;
                        }

                        ImportGraphicsBase.ApplyCustomFields(sprite, spriteName);
                        if (frame > sprite.Textures.Count - 1)
                        {
                            while (frame > sprite.Textures.Count - 1)
                            {
                                sprite.Textures.Add(texentry);
                            }
                            continue;
                        }
                        
                        sprite.Textures[frame] = texentry;

                        // Update sprite dimensions
                        uint oldWidth = sprite.Width, oldHeight = sprite.Height;
                        sprite.Width = (uint)n.Texture.BoundingWidth;
                        sprite.Height = (uint)n.Texture.BoundingHeight;
                        bool changedSpriteDimensions = (oldWidth != sprite.Width || oldHeight != sprite.Height);

                        // Grow bounding box depending on how much is trimmed
                        bool grewBoundingBox = false;
                        bool fullImageBbox = sprite.BBoxMode == 1;
                        bool manualBbox = sprite.BBoxMode == 2;
                        if (!manualBbox)
                        {
                            int marginLeft = fullImageBbox ? 0 : n.Texture.TargetX;
                            int marginRight = fullImageBbox ? ((int)sprite.Width - 1) : (n.Texture.TargetX + n.Bounds.Width - 1);
                            int marginTop = fullImageBbox ? 0 : n.Texture.TargetY;
                            int marginBottom = fullImageBbox ? ((int)sprite.Height - 1) : (n.Texture.TargetY + n.Bounds.Height - 1);
                            if (marginLeft < sprite.MarginLeft)
                            {
                                sprite.MarginLeft = marginLeft;
                                grewBoundingBox = true;
                            }
                            if (marginTop < sprite.MarginTop)
                            {
                                sprite.MarginTop = marginTop;
                                grewBoundingBox = true;
                            }
                            if (marginRight > sprite.MarginRight)
                            {
                                sprite.MarginRight = marginRight;
                                grewBoundingBox = true;
                            }
                            if (marginBottom > sprite.MarginBottom)
                            {
                                sprite.MarginBottom = marginBottom;
                                grewBoundingBox = true;
                            }
                        }

                        // Only generate collision masks for sprites that need them (in newer GameMaker versions)
                        if (!noMasksForBasicRectangles ||
                            sprite.SepMasks is not (UndertaleSprite.SepMaskType.AxisAlignedRect or UndertaleSprite.SepMaskType.RotatedRect) ||
                            sprite.CollisionMasks.Count > 0)
                        {
                            if ((bboxMasks && grewBoundingBox) ||
                                (sprite.SepMasks is UndertaleSprite.SepMaskType.Precise && sprite.CollisionMasks.Count == 0) ||
                                (!bboxMasks && changedSpriteDimensions))
                            {
                                // Use this node for the sprite's collision mask if the bounding box grew, if no collision mask exists for a precise sprite,
                                // or if the sprite's dimensions have been changed altogether when bbox masks are not active.
                                maskNodes[sprite] = n;
                            }
                        }
                    }
                }
            }

            foreach ((UndertaleSprite maskSpr, ImportGraphicsBase.Node maskNode) in maskNodes)
            {
                // Generate collision mask using either bounding box or sprite dimensions
                maskSpr.CollisionMasks.Clear();
                maskSpr.CollisionMasks.Add(maskSpr.NewMaskEntry(Data));
                (int maskWidth, int maskHeight) = maskSpr.CalculateMaskDimensions(Data);
                int maskStride = ((maskWidth + 7) / 8) * 8;

                BitArray maskingBitArray = new BitArray(maskStride * maskHeight);
                for (int y = 0; y < maskHeight && y < maskNode.Bounds.Height; y++)
                {
                    for (int x = 0; x < maskWidth && x < maskNode.Bounds.Width; x++)
                    {
                        IMagickColor<byte> pixelColor = atlasPixels.GetPixel(x + maskNode.Bounds.X, y + maskNode.Bounds.Y).ToColor();
                        if (bboxMasks)
                        {
                            maskingBitArray[(y * maskStride) + x] = (pixelColor.A > 0);
                        }
                        else
                        {
                            maskingBitArray[((y + maskNode.Texture.TargetY) * maskStride) + x + maskNode.Texture.TargetX] = (pixelColor.A > 0);
                        }
                    }
                }
                BitArray tempBitArray = new BitArray(maskingBitArray.Length);
                for (int i = 0; i < maskingBitArray.Length; i += 8)
                {
                    for (int j = 0; j < 8; j++)
                    {
                        tempBitArray[j + i] = maskingBitArray[-(j - 7) + i];
                    }
                }

                int numBytes = maskingBitArray.Length / 8;
                byte[] bytes = new byte[numBytes];
                tempBitArray.CopyTo(bytes, 0);
                for (int i = 0; i < bytes.Length; i++)
                    maskSpr.CollisionMasks[0].Data[i] = bytes[i];
            }
            maskNodes.Clear();

            // Increment atlas
            atlasCount++;
        }
        Console.WriteLine($"[GRAPHICS][INFO] Imported {Data.TexturePageItems.Count} texture page items and {Data.EmbeddedTextures.Count} embedded textures.");

        foreach (MagickImage img in imagesToCleanup)
        {
            img.Dispose();
        }
        return 0;
    }
}

public class ImportGraphicsBase
{
    public static Regex sprFrameRegex { get; set; }

    public class TextureInfo
    {
        public string Source = "";
        public int Width;
        public int Height;
        public int TargetX;
        public int TargetY;
        public int BoundingWidth;
        public int BoundingHeight;
        public MagickImage? Image;
    }

    public enum SpriteType
    {
        Sprite,
        Background,
        Font,
        Unknown
    }

    public enum SplitType
    {
        Horizontal,
        Vertical,
    }

    public enum BestFitHeuristic
    {
        Area,
        MaxOneAxis,
    }

    public struct Rect
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class Node
    {
        public Rect Bounds;
        public TextureInfo Texture;
        public SplitType SplitType;
    }

    public class Atlas
    {
        public int Width;
        public int Height;
        public List<Node> Nodes = new();
    }

    public class Packer
    {
        public List<TextureInfo> SourceTextures;
        public StringWriter Log;
        public StringWriter Error;
        public int Padding;
        public int AtlasSize;
        public bool DebugMode;
        public BestFitHeuristic FitHeuristic;
        public List<Atlas> Atlasses;

        public Packer()
        {
            SourceTextures = new List<TextureInfo>();
            Log = new StringWriter();
            Error = new StringWriter();
        }

        public void Process(string _SourceDir, string _Pattern, int _AtlasSize, int _Padding, bool _DebugMode)
        {
            Padding = _Padding;
            AtlasSize = _AtlasSize;
            DebugMode = _DebugMode;
            //1: scan for all the textures we need to pack
            ScanForTextures(_SourceDir, _Pattern);
            List<TextureInfo> textures = new List<TextureInfo>();
            textures = SourceTextures.ToList();
            //2: generate as many atlasses as needed (with the latest one as small as possible)
            Atlasses = new List<Atlas>();
            while (textures.Count > 0)
            {
                Atlas atlas = new Atlas();
                atlas.Width = _AtlasSize;
                atlas.Height = _AtlasSize;
                List<TextureInfo> leftovers = LayoutAtlas(textures, atlas);
                if (leftovers.Count == 0)
                {
                    // we reached the last atlas. Check if this last atlas could have been twice smaller
                    while (leftovers.Count == 0)
                    {
                        atlas.Width /= 2;
                        atlas.Height /= 2;
                        leftovers = LayoutAtlas(textures, atlas);
                    }
                    // we need to go 1 step larger as we found the first size that is too small
                    // if the atlas is 0x0 then it should be 1x1 instead
                    if (atlas.Width == 0)
                    {
                        atlas.Width = 1;
                    }
                    else
                    {
                        atlas.Width *= 2;
                    }
                    if (atlas.Height == 0)
                    {
                        atlas.Height = 1;
                    }
                    else
                    {
                        atlas.Height *= 2;
                    }
                    leftovers = LayoutAtlas(textures, atlas);
                }
                Atlasses.Add(atlas);
                textures = leftovers;
            }
        }

        public void SaveAtlasses(string _Destination)
        {
            int atlasCount = 0;
            string prefix = _Destination.Replace(Path.GetExtension(_Destination), "");
            string descFile = _Destination;

            StreamWriter tw = new StreamWriter(_Destination);
            tw.WriteLine("source_tex, atlas_tex, x, y, width, height");
            foreach (Atlas atlas in Atlasses)
            {
                string atlasName = $"{prefix}{atlasCount:000}.png";

                // 1: Save images
                using (MagickImage img = CreateAtlasImage(atlas))
                    TextureWorker.SaveImageToFile(img, atlasName);

                // 2: save description in file
                foreach (Node n in atlas.Nodes)
                {
                    if (n.Texture != null)
                    {
                        tw.Write(n.Texture.Source + ", ");
                        tw.Write(atlasName + ", ");
                        tw.Write((n.Bounds.X).ToString() + ", ");
                        tw.Write((n.Bounds.Y).ToString() + ", ");
                        tw.Write((n.Bounds.Width).ToString() + ", ");
                        tw.WriteLine((n.Bounds.Height).ToString());
                    }
                }
                ++atlasCount;
            }
            tw.Close();
            tw = new StreamWriter(prefix + ".log");
            tw.WriteLine("--- LOG -------------------------------------------");
            tw.WriteLine(Log.ToString());
            tw.WriteLine("--- ERROR -----------------------------------------");
            tw.WriteLine(Error.ToString());
            tw.Close();
        }

        private void ScanForTextures(string _Path, string _Wildcard)
        {
            DirectoryInfo di = new(_Path);
            FileInfo[] files = di.GetFiles(_Wildcard, SearchOption.AllDirectories);
            foreach (FileInfo fi in files)
            {
                (int width, int height) = TextureWorker.GetImageSizeFromFile(fi.FullName);
                if (width == -1 || height == -1)
                    continue;

                if (width <= AtlasSize && height <= AtlasSize)
                {
                    TextureInfo ti = new();

                    MagickReadSettings settings = new()
                    {
                        ColorSpace = ColorSpace.sRGB,
                    };
                    MagickImage img = new(fi.FullName);
                    GraphicsImporter.imagesToCleanup.Add(img);

                    ti.Source = fi.FullName;
                    ti.BoundingWidth = (int)img.Width;
                    ti.BoundingHeight = (int)img.Height;

                    // GameMaker doesn't trim tilesets. I assume it didn't trim backgrounds too
                    ti.TargetX = 0;
                    ti.TargetY = 0;
                    if (GetSpriteType(ti.Source) != SpriteType.Background)
                    {
                        img.BorderColor = MagickColors.Transparent;
                        img.BackgroundColor = MagickColors.Transparent;
                        img.Border(1);
                        IMagickGeometry? bbox = img.BoundingBox;
                        if (bbox is not null)
                        {
                            ti.TargetX = bbox.X - 1;
                            ti.TargetY = bbox.Y - 1;
                            // yes, .Trim() mutates the image...
                            // it doesn't really matter though since it isn't written back or anything
                            img.Trim();
                        }
                        else
                        {
                            // Empty sprites should be 1x1
                            ti.TargetX = 0;
                            ti.TargetY = 0;
                            img.Crop(1, 1);
                        }
                        img.ResetPage();
                    }
                    ti.Width = (int)img.Width;
                    ti.Height = (int)img.Height;
                    ti.Image = img;

                    SourceTextures.Add(ti);

                    Log.WriteLine($"Added {fi.FullName}");
                }
                else
                {
                    Error.WriteLine($"{fi.FullName} is too large to fix in the atlas. Skipping!");
                }
            }
        }

        private void HorizontalSplit(Node _ToSplit, int _Width, int _Height, List<Node> _List)
        {
            Node n1 = new Node();
            n1.Bounds.X = _ToSplit.Bounds.X + _Width + Padding;
            n1.Bounds.Y = _ToSplit.Bounds.Y;
            n1.Bounds.Width = _ToSplit.Bounds.Width - _Width - Padding;
            n1.Bounds.Height = _Height;
            n1.SplitType = SplitType.Vertical;
            Node n2 = new Node();
            n2.Bounds.X = _ToSplit.Bounds.X;
            n2.Bounds.Y = _ToSplit.Bounds.Y + _Height + Padding;
            n2.Bounds.Width = _ToSplit.Bounds.Width;
            n2.Bounds.Height = _ToSplit.Bounds.Height - _Height - Padding;
            n2.SplitType = SplitType.Horizontal;
            if (n1.Bounds.Width > 0 && n1.Bounds.Height > 0)
                _List.Add(n1);
            if (n2.Bounds.Width > 0 && n2.Bounds.Height > 0)
                _List.Add(n2);
        }

        private void VerticalSplit(Node _ToSplit, int _Width, int _Height, List<Node> _List)
        {
            Node n1 = new Node();
            n1.Bounds.X = _ToSplit.Bounds.X + _Width + Padding;
            n1.Bounds.Y = _ToSplit.Bounds.Y;
            n1.Bounds.Width = _ToSplit.Bounds.Width - _Width - Padding;
            n1.Bounds.Height = _ToSplit.Bounds.Height;
            n1.SplitType = SplitType.Vertical;
            Node n2 = new Node();
            n2.Bounds.X = _ToSplit.Bounds.X;
            n2.Bounds.Y = _ToSplit.Bounds.Y + _Height + Padding;
            n2.Bounds.Width = _Width;
            n2.Bounds.Height = _ToSplit.Bounds.Height - _Height - Padding;
            n2.SplitType = SplitType.Horizontal;
            if (n1.Bounds.Width > 0 && n1.Bounds.Height > 0)
                _List.Add(n1);
            if (n2.Bounds.Width > 0 && n2.Bounds.Height > 0)
                _List.Add(n2);
        }

        private TextureInfo FindBestFitForNode(Node _Node, List<TextureInfo> _Textures)
        {
            TextureInfo bestFit = null;
            float nodeArea = _Node.Bounds.Width * _Node.Bounds.Height;
            float maxCriteria = 0.0f;
            foreach (TextureInfo ti in _Textures)
            {
                switch (FitHeuristic)
                {
                    // Max of Width and Height ratios
                    case BestFitHeuristic.MaxOneAxis:
                        if (ti.Width <= _Node.Bounds.Width && ti.Height <= _Node.Bounds.Height)
                        {
                            float wRatio = (float)ti.Width / (float)_Node.Bounds.Width;
                            float hRatio = (float)ti.Height / (float)_Node.Bounds.Height;
                            float ratio = wRatio > hRatio ? wRatio : hRatio;
                            if (ratio > maxCriteria)
                            {
                                maxCriteria = ratio;
                                bestFit = ti;
                            }
                        }
                        break;
                    // Maximize Area coverage
                    case BestFitHeuristic.Area:
                        if (ti.Width <= _Node.Bounds.Width && ti.Height <= _Node.Bounds.Height)
                        {
                            float textureArea = ti.Width * ti.Height;
                            float coverage = textureArea / nodeArea;
                            if (coverage > maxCriteria)
                            {
                                maxCriteria = coverage;
                                bestFit = ti;
                            }
                        }
                        break;
                }
            }
            return bestFit;
        }

        private List<TextureInfo> LayoutAtlas(List<TextureInfo> _Textures, Atlas _Atlas)
        {
            List<Node> freeList = new List<Node>();
            List<TextureInfo> textures = new List<TextureInfo>();
            _Atlas.Nodes = new List<Node>();
            textures = _Textures.ToList();
            Node root = new Node();
            root.Bounds.Width = _Atlas.Width;
            root.Bounds.Height = _Atlas.Height;
            root.SplitType = SplitType.Horizontal;
            freeList.Add(root);
            while (freeList.Count > 0 && textures.Count > 0)
            {
                Node node = freeList[0];
                freeList.RemoveAt(0);
                TextureInfo bestFit = FindBestFitForNode(node, textures);
                if (bestFit != null)
                {
                    if (node.SplitType == SplitType.Horizontal)
                    {
                        HorizontalSplit(node, bestFit.Width, bestFit.Height, freeList);
                    }
                    else
                    {
                        VerticalSplit(node, bestFit.Width, bestFit.Height, freeList);
                    }
                    node.Texture = bestFit;
                    node.Bounds.Width = bestFit.Width;
                    node.Bounds.Height = bestFit.Height;
                    textures.Remove(bestFit);
                }
                _Atlas.Nodes.Add(node);
            }
            return textures;
        }

        private MagickImage CreateAtlasImage(Atlas _Atlas)
        {
            MagickImage img = new(MagickColors.Transparent, (uint)_Atlas.Width, (uint)_Atlas.Height);

            foreach (Node n in _Atlas.Nodes)
            {
                if (n.Texture is not null)
                {
                    MagickImage sourceImg = n.Texture.Image;
                    using IMagickImage<byte> resizedSourceImg = TextureWorker.ResizeImage(sourceImg, n.Bounds.Width, n.Bounds.Height);
                    img.Composite(resizedSourceImg, n.Bounds.X, n.Bounds.Y, CompositeOperator.Copy);
                }
            }

            return img;
        }
    }

    public static SpriteType GetSpriteType(string path)
    {
        string folderPath = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folderPath))
            return SpriteType.Unknown;
        string folderName = new DirectoryInfo(folderPath).Name;

        string lowerName = folderName.ToLower();

        if (lowerName == "backgrounds" || lowerName == "background")
        {
            return SpriteType.Background;
        }
        else if (lowerName == "fonts" || lowerName == "font")
        {
            return SpriteType.Font;
        }
        else if (lowerName == "sprites" || lowerName == "sprite")
        {
            return SpriteType.Sprite;
        }
        return SpriteType.Unknown;
    }

    public static string CheckValidity()
    {
        // Check if the config is set
        if (GraphicsImporter.CurrentConfig == null)
            return "err_no_config";

        // Get import folder
        string importFolder = PatchFile.GetRelativePath(GraphicsImporter.CurrentConfig.directory);
        if (importFolder == null)
            return "err_no_import_folder";

        //Stop the script if there's missing sprite entries or w/e.
        // bool hadMessage = false;
        string currSpriteName = null;
        string[] dirFiles = Directory.GetFiles(importFolder, "*.png", SearchOption.AllDirectories);
        foreach (string file in dirFiles)
        {
            string FileNameWithExtension = Path.GetFileName(file);
            string stripped = Path.GetFileNameWithoutExtension(file);
            string spriteName = "";

            SpriteType spriteType = GetSpriteType(file);

            if ((spriteType != SpriteType.Sprite) && (spriteType != SpriteType.Background))
            {
                /*if (!hadMessage)
                {
                    hadMessage = true;
                    importAsSprite = ScriptQuestion(FileNameWithExtension + @" is in an incorrectly-named folder (valid names being ""Sprites"" and ""Backgrounds""). Would you like to import these images as sprites?
    Pressing ""No"" will cause the program to ignore these images.");
                }*/
                if (!GraphicsImporter.CurrentConfig.importUnknownAsSprite)
                {
                    if (GraphicsImporter.CurrentConfig.debug)
                        Console.WriteLine($"[GRAPHICS][NOTE] {FileNameWithExtension} is in an incorrectly-named folder (valid names being \"Sprites\" and \"Backgrounds\"). Based on your configuration [importUnknownAsSprite] it's skipped.");
                    continue;
                }
                else
                {
                    if (GraphicsImporter.CurrentConfig.debug)
                        Console.WriteLine($"[GRAPHICS][NOTE] {FileNameWithExtension} is in an incorrectly-named folder (valid names being \"Sprites\" and \"Backgrounds\"). Based on your configuration [importUnknownAsSprite] it will be imported as sprite.");
                    spriteType = SpriteType.Sprite;
                }
            }

            // Check for duplicate filenames
            string[] dupFiles = Directory.GetFiles(importFolder, FileNameWithExtension, SearchOption.AllDirectories);
            if (dupFiles.Length > 1)
                return "err_duplicate_file_count_" + dupFiles.Length + "_" + FileNameWithExtension;

            // Sprites can have multiple frames! Do some sprite-specific checking.
            if (spriteType == SpriteType.Sprite)
            {
                if (sprFrameRegex == null)
                {
                    Console.WriteLine("[GRAPHICS][ERROR] sprFrameRegex is null!");
                    return "err_no_regex";
                }
                var spriteParts = sprFrameRegex.Match(stripped);
                // Allow sprites without underscores
                if (!spriteParts.Groups[2].Success)
                    continue;

                spriteName = spriteParts.Groups[1].Value;

                if (!Int32.TryParse(spriteParts.Groups[2].Value, out int frame))
                    return "err_invalid_frame_index_" + FileNameWithExtension;
                if (frame < 0)
                    return "err_negative_frame_index_" + FileNameWithExtension;

                // If it's not a first frame of the sprite
                if (spriteName == currSpriteName)
                    continue;

                string[][] spriteFrames = Directory.GetFiles(importFolder, $"{spriteName}_*.png", SearchOption.AllDirectories)
                                                .Select(x =>
                                                {
                                                    var match = sprFrameRegex.Match(Path.GetFileNameWithoutExtension(x));
                                                    if (match.Groups[2].Success)
                                                        return new string[] { match.Groups[1].Value, match.Groups[2].Value };
                                                    else
                                                        return null;
                                                })
                                                .OfType<string[]>().ToArray();
                if (spriteFrames.Length == 1)
                {
                    currSpriteName = null;
                    continue;
                }

                int[] frameIndexes = spriteFrames.Select(x =>
                {
                    if (Int32.TryParse(x[1], out int frame))
                        return (int?)frame;
                    else
                        return null;
                }).OfType<int?>().Cast<int>().OrderBy(x => x).ToArray();
                if (frameIndexes.Length == 1)
                {
                    currSpriteName = null;
                    continue;
                }

                for (int i = 0; i < frameIndexes.Length - 1; i++)
                {
                    int num = frameIndexes[i];
                    int nextNum = frameIndexes[i + 1];

                    if (nextNum - num > 1)
                        return "err_" + spriteName + "_missing_frame_index_" + (num + 1);
                }
                currSpriteName = spriteName;
            }
        }
        return importFolder;
    }

    public static void ApplyCustomFields(UndertaleSprite sprite, string spriteName)
    {
        var config = GraphicsImporter.CurrentConfig.changeProps;
        if (!config.TryGetValue(spriteName, out var spriteProps))
            return;
        
        Console.WriteLine($"[GRAPHICS][NOTE] Applying custom properties for sprite '{spriteName}'");

        JsonElement? GetJsonElement(object obj)
        {
            return obj is JsonElement je ? je : null;
        }

        if (spriteProps.TryGetValue("size", out var sizeObj))
        {
            var sizeElem = GetJsonElement(sizeObj);
            if (sizeElem.HasValue && sizeElem.Value.ValueKind == JsonValueKind.Object)
            {
                if (sizeElem.Value.TryGetProperty("width", out var w) && w.TryGetUInt32(out var wVal))
                    sprite.Width = wVal;
                if (sizeElem.Value.TryGetProperty("height", out var h) && h.TryGetUInt32(out var hVal))
                    sprite.Height = hVal;
            }
            Console.WriteLine($"[GRAPHICS][NOTE] Set size for sprite '{spriteName}' to {sprite.Width}x{sprite.Height}");
        }

        if (spriteProps.TryGetValue("margin", out var marginObj))
        {
            var marginElem = GetJsonElement(marginObj);
            if (marginElem.HasValue && marginElem.Value.ValueKind == JsonValueKind.Object)
            {
                if (marginElem.Value.TryGetProperty("left", out var l) && l.TryGetInt32(out var lVal))
                    sprite.MarginLeft = lVal;
                if (marginElem.Value.TryGetProperty("right", out var r) && r.TryGetInt32(out var rVal))
                    sprite.MarginRight = rVal;
                if (marginElem.Value.TryGetProperty("top", out var t) && t.TryGetInt32(out var tVal))
                    sprite.MarginTop = tVal;
                if (marginElem.Value.TryGetProperty("bottom", out var b) && b.TryGetInt32(out var bVal))
                    sprite.MarginBottom = bVal;
            }
        }

        if (spriteProps.TryGetValue("transparent", out var transparentObj))
        {
            var transparentElem = GetJsonElement(transparentObj);
            if (transparentElem.HasValue && (transparentElem.Value.ValueKind == JsonValueKind.True || transparentElem.Value.ValueKind == JsonValueKind.False))
                sprite.Transparent = transparentElem.Value.GetBoolean();
        }

        if (spriteProps.TryGetValue("origin", out var originObj))
        {
            var originElem = GetJsonElement(originObj);
            if (originElem.HasValue && originElem.Value.ValueKind == JsonValueKind.Object)
            {
                if (originElem.Value.TryGetProperty("x", out var ox) && ox.TryGetInt32(out var oxVal))
                    sprite.OriginX = oxVal;
                if (originElem.Value.TryGetProperty("y", out var oy) && oy.TryGetInt32(out var oyVal))
                    sprite.OriginY = oyVal;
            }
        }
    }


}



