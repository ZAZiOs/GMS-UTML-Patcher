using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using Underanalyzer.Decompiler;

public static class RoomFile
{
    private const uint MAGIC = 0x4D524D47; // "GMRM" in Little Endian (0x47, 0x4D, 0x52, 0x4D)
    private const byte FORMAT_VERSION = 1;

    public class ExportedCode
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    private class StringTable
    {
        private readonly List<string> _list = new();
        private readonly Dictionary<string, int> _dict = new(StringComparer.Ordinal);

        public int GetOrAdd(string? str)
        {
            if (str == null)
                return -1;
            if (_dict.TryGetValue(str, out int idx))
                return idx;
            idx = _list.Count;
            _list.Add(str);
            _dict[str] = idx;
            return idx;
        }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(_list.Count);
            foreach (var s in _list)
                writer.Write(s);
        }

        public static string[] ReadFrom(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            string[] table = new string[count];
            for (int i = 0; i < count; i++)
                table[i] = reader.ReadString();
            return table;
        }
    }

    private static string DecompileCode(UndertaleData data, UndertaleCode? code)
    {
        if (code == null)
            return string.Empty;
        try
        {
            var globalContext = new GlobalDecompileContext(data);
            var settings = data.ToolInfo?.DecompilerSettings ?? new DecompileSettings();
            var ctx = new Underanalyzer.Decompiler.DecompileContext(globalContext, code, settings);
            return ctx.DecompileToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static byte[] SerializeRoom(UndertaleRoom room, UndertaleData data)
    {
        bool isGms2 = data.IsGameMaker2();
        var stringTable = new StringTable();

        using var memoryStream = new MemoryStream();
        using (var defStream = new DeflateStream(memoryStream, CompressionLevel.Optimal, true))
        using (var writer = new BinaryWriter(defStream))
        {
            using var tempStream = new MemoryStream();
            using var tempWriter = new BinaryWriter(tempStream);

            // Room Properties
            tempWriter.Write(stringTable.GetOrAdd(room.Name?.Content));
            tempWriter.Write(stringTable.GetOrAdd(room.Caption?.Content));
            tempWriter.Write(room.Width);
            tempWriter.Write(room.Height);
            tempWriter.Write(room.Speed);
            tempWriter.Write(room.Persistent);
            tempWriter.Write(room.BackgroundColor);
            tempWriter.Write(room.DrawBackgroundColor);
            tempWriter.Write((uint)room.Flags);

            // Creation Code
            bool hasCreationCode = room.CreationCodeId != null;
            tempWriter.Write(hasCreationCode);
            if (hasCreationCode)
            {
                tempWriter.Write(stringTable.GetOrAdd(room.CreationCodeId.Name?.Content));
                tempWriter.Write(DecompileCode(data, room.CreationCodeId));
            }

            if (!isGms2)
            {
                // GMS 1.x: Backgrounds
                tempWriter.Write(room.Backgrounds.Count);
                foreach (var bg in room.Backgrounds)
                {
                    tempWriter.Write(bg.Enabled);
                    tempWriter.Write(bg.Foreground);
                    tempWriter.Write(stringTable.GetOrAdd(bg.BackgroundDefinition?.Name?.Content));
                    tempWriter.Write(bg.X);
                    tempWriter.Write(bg.Y);
                    tempWriter.Write(bg.TiledHorizontally);
                    tempWriter.Write(bg.TiledVertically);
                    tempWriter.Write(bg.SpeedX);
                    tempWriter.Write(bg.SpeedY);
                    tempWriter.Write(bg.Stretch);
                }

                // GMS 1.x: Views
                tempWriter.Write(room.Views.Count);
                foreach (var view in room.Views)
                {
                    tempWriter.Write(view.Enabled);
                    tempWriter.Write(view.ViewX);
                    tempWriter.Write(view.ViewY);
                    tempWriter.Write(view.ViewWidth);
                    tempWriter.Write(view.ViewHeight);
                    tempWriter.Write(view.PortX);
                    tempWriter.Write(view.PortY);
                    tempWriter.Write(view.PortWidth);
                    tempWriter.Write(view.PortHeight);
                    tempWriter.Write(view.BorderX);
                    tempWriter.Write(view.BorderY);
                    tempWriter.Write(view.SpeedX);
                    tempWriter.Write(view.SpeedY);
                    tempWriter.Write(stringTable.GetOrAdd(view.ObjectId?.Name?.Content));
                }

                // GMS 1.x: GameObjects
                SerializeGameObjects(room.GameObjects, tempWriter, stringTable, data);

                // GMS 1.x: Tiles
                SerializeTiles(room.Tiles, tempWriter, stringTable);
            }
            else
            {
                // GMS 2+: Layers
                tempWriter.Write(room.Layers.Count);
                foreach (var layer in room.Layers)
                {
                    tempWriter.Write(layer.LayerId);
                    tempWriter.Write(stringTable.GetOrAdd(layer.LayerName?.Content));
                    tempWriter.Write((uint)layer.LayerType);
                    tempWriter.Write(layer.LayerDepth);
                    tempWriter.Write(layer.XOffset);
                    tempWriter.Write(layer.YOffset);
                    tempWriter.Write(layer.HSpeed);
                    tempWriter.Write(layer.VSpeed);
                    tempWriter.Write(layer.IsVisible);

                    switch (layer.LayerType)
                    {
                        case UndertaleRoom.LayerType.Instances:
                        {
                            var instData = layer.InstancesData;
                            var instances = instData != null ? instData.Instances : (IList<UndertaleRoom.GameObject>)Array.Empty<UndertaleRoom.GameObject>();
                            SerializeGameObjects(instances, tempWriter, stringTable, data);
                            break;
                        }
                        case UndertaleRoom.LayerType.Tiles:
                        {
                            var tilesData = layer.TilesData;
                            tempWriter.Write(stringTable.GetOrAdd(tilesData?.Background?.Name?.Content));
                            uint tx = tilesData?.TilesX ?? 0;
                            uint ty = tilesData?.TilesY ?? 0;
                            tempWriter.Write(tx);
                            tempWriter.Write(ty);

                            var flatTiles = new List<uint>((int)(tx * ty));
                            if (tilesData?.TileData != null)
                            {
                                for (int y = 0; y < ty; y++)
                                {
                                    for (int x = 0; x < tx; x++)
                                    {
                                        flatTiles.Add(tilesData.TileData[y][x]);
                                    }
                                }
                            }
                            WriteRle(flatTiles, tempWriter);
                            break;
                        }
                        case UndertaleRoom.LayerType.Assets:
                        {
                            var assets = layer.AssetsData;
                            // Sprites
                            int sprCount = assets?.Sprites?.Count ?? 0;
                            tempWriter.Write(sprCount);
                            if (assets?.Sprites != null)
                            {
                                foreach (var spr in assets.Sprites)
                                {
                                    tempWriter.Write(stringTable.GetOrAdd(spr.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(spr.Sprite?.Name?.Content));
                                    tempWriter.Write(spr.X);
                                    tempWriter.Write(spr.Y);
                                    tempWriter.Write(spr.ScaleX);
                                    tempWriter.Write(spr.ScaleY);
                                    tempWriter.Write(spr.Color);
                                    tempWriter.Write(spr.AnimationSpeed);
                                    tempWriter.Write((uint)spr.AnimationSpeedType);
                                    tempWriter.Write(spr.FrameIndex);
                                    tempWriter.Write(spr.Rotation);
                                }
                            }

                            // LegacyTiles
                            int tileCount = assets?.LegacyTiles?.Count ?? 0;
                            tempWriter.Write(tileCount);
                            if (assets?.LegacyTiles != null)
                            {
                                SerializeTiles(assets.LegacyTiles, tempWriter, stringTable);
                            }

                            // Sequences
                            int seqCount = assets?.Sequences?.Count ?? 0;
                            tempWriter.Write(seqCount);
                            if (assets?.Sequences != null)
                            {
                                foreach (var seq in assets.Sequences)
                                {
                                    tempWriter.Write(stringTable.GetOrAdd(seq.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(seq.Sequence?.Name?.Content));
                                    tempWriter.Write(seq.X);
                                    tempWriter.Write(seq.Y);
                                    tempWriter.Write(seq.ScaleX);
                                    tempWriter.Write(seq.ScaleY);
                                    tempWriter.Write(seq.Color);
                                    tempWriter.Write(seq.AnimationSpeed);
                                    tempWriter.Write((uint)seq.AnimationSpeedType);
                                    tempWriter.Write(seq.FrameIndex);
                                    tempWriter.Write(seq.Rotation);
                                }
                            }

                            // ParticleSystems
                            int psCount = assets?.ParticleSystems?.Count ?? 0;
                            tempWriter.Write(psCount);
                            if (assets?.ParticleSystems != null)
                            {
                                foreach (var ps in assets.ParticleSystems)
                                {
                                    tempWriter.Write(stringTable.GetOrAdd(ps.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(ps.ParticleSystem?.Name?.Content));
                                    tempWriter.Write(ps.X);
                                    tempWriter.Write(ps.Y);
                                    tempWriter.Write(ps.ScaleX);
                                    tempWriter.Write(ps.ScaleY);
                                    tempWriter.Write(ps.Color);
                                    tempWriter.Write(ps.Rotation);
                                }
                            }

                            // TextItems
                            int textCount = assets?.TextItems?.Count ?? 0;
                            tempWriter.Write(textCount);
                            if (assets?.TextItems != null)
                            {
                                foreach (var txt in assets.TextItems)
                                {
                                    tempWriter.Write(stringTable.GetOrAdd(txt.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(txt.Font?.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(txt.Text?.Content));
                                    tempWriter.Write(txt.X);
                                    tempWriter.Write(txt.Y);
                                    tempWriter.Write(txt.ScaleX);
                                    tempWriter.Write(txt.ScaleY);
                                    tempWriter.Write(txt.Color);
                                    tempWriter.Write(txt.Rotation);
                                    tempWriter.Write(txt.OriginX);
                                    tempWriter.Write(txt.OriginY);
                                    tempWriter.Write(txt.Alignment);
                                    tempWriter.Write(txt.CharSpacing);
                                    tempWriter.Write(txt.LineSpacing);
                                    tempWriter.Write(txt.FrameWidth);
                                    tempWriter.Write(txt.FrameHeight);
                                    tempWriter.Write(txt.Wrap);
                                }
                            }
                            break;
                        }
                        case UndertaleRoom.LayerType.Background:
                        {
                            var bgData = layer.BackgroundData;
                            tempWriter.Write(stringTable.GetOrAdd(bgData?.Sprite?.Name?.Content));
                            tempWriter.Write(bgData?.Visible ?? true);
                            tempWriter.Write(bgData?.Foreground ?? false);
                            tempWriter.Write(bgData?.TiledHorizontally ?? false);
                            tempWriter.Write(bgData?.TiledVertically ?? false);
                            tempWriter.Write(bgData?.Stretch ?? false);
                            tempWriter.Write(bgData?.Color ?? 0xFF000000);
                            tempWriter.Write(bgData?.FirstFrame ?? 0f);
                            tempWriter.Write(bgData?.AnimationSpeed ?? 0f);
                            tempWriter.Write((uint)(bgData?.AnimationSpeedType ?? AnimationSpeedType.FPS));
                            break;
                        }
                        case UndertaleRoom.LayerType.Effect:
                        {
                            var eff = layer.EffectData;
                            tempWriter.Write(stringTable.GetOrAdd(eff?.EffectType?.Content));
                            int propCount = eff?.Properties?.Count ?? 0;
                            tempWriter.Write(propCount);
                            if (eff?.Properties != null)
                            {
                                foreach (var p in eff.Properties)
                                {
                                    tempWriter.Write((uint)p.Kind);
                                    tempWriter.Write(stringTable.GetOrAdd(p.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(p.Value?.Content));
                                }
                            }
                            break;
                        }
                        default:
                            break;
                    }
                }
            }

            stringTable.WriteTo(writer);
            tempStream.Position = 0;
            tempStream.CopyTo(defStream);
        }

        byte[] compressedData = memoryStream.ToArray();

        using var resultStream = new MemoryStream();
        using var finalWriter = new BinaryWriter(resultStream);
        finalWriter.Write(MAGIC);
        finalWriter.Write(FORMAT_VERSION);
        finalWriter.Write(isGms2);
        finalWriter.Write(compressedData.Length);
        finalWriter.Write(compressedData);

        return resultStream.ToArray();
    }

    private static void SerializeGameObjects(IList<UndertaleRoom.GameObject> list, BinaryWriter writer, StringTable stringTable, UndertaleData data)
    {
        writer.Write(list.Count);
        foreach (var go in list)
        {
            writer.Write(go.X);
            writer.Write(go.Y);
            writer.Write(stringTable.GetOrAdd(go.ObjectDefinition?.Name?.Content));
            writer.Write(go.InstanceID);
            writer.Write(go.ScaleX);
            writer.Write(go.ScaleY);
            writer.Write(go.Color);
            writer.Write(go.Rotation);
            writer.Write(go.ImageSpeed);
            writer.Write(go.ImageIndex);

            bool hasCc = go.CreationCode != null;
            writer.Write(hasCc);
            if (hasCc)
            {
                writer.Write(stringTable.GetOrAdd(go.CreationCode.Name?.Content));
                writer.Write(DecompileCode(data, go.CreationCode));
            }

            bool hasPre = go.PreCreateCode != null;
            writer.Write(hasPre);
            if (hasPre)
            {
                writer.Write(stringTable.GetOrAdd(go.PreCreateCode.Name?.Content));
                writer.Write(DecompileCode(data, go.PreCreateCode));
            }
        }
    }

    private static void SerializeTiles(IList<UndertaleRoom.Tile> list, BinaryWriter writer, StringTable stringTable)
    {
        writer.Write(list.Count);
        foreach (var tile in list)
        {
            writer.Write(tile.X);
            writer.Write(tile.Y);
            writer.Write(tile.spriteMode);
            string? defName = tile.spriteMode ? tile.SpriteDefinition?.Name?.Content : tile.BackgroundDefinition?.Name?.Content;
            writer.Write(stringTable.GetOrAdd(defName));
            writer.Write(tile.SourceX);
            writer.Write(tile.SourceY);
            writer.Write(tile.Width);
            writer.Write(tile.Height);
            writer.Write(tile.TileDepth);
            writer.Write(tile.InstanceID);
            writer.Write(tile.ScaleX);
            writer.Write(tile.ScaleY);
            writer.Write(tile.Color);
        }
    }

    private static void WriteRle(List<uint> values, BinaryWriter writer)
    {
        writer.Write(values.Count);
        if (values.Count == 0)
            return;

        int i = 0;
        var runs = new List<(uint Val, int Len)>();
        while (i < values.Count)
        {
            uint current = values[i];
            int len = 1;
            while (i + 1 < values.Count && values[i + 1] == current && len < int.MaxValue)
            {
                len++;
                i++;
            }
            runs.Add((current, len));
            i++;
        }

        writer.Write(runs.Count);
        foreach (var run in runs)
        {
            writer.Write(run.Val);
            writer.Write(run.Len);
        }
    }

    private static List<uint> ReadRle(BinaryReader reader, int totalCount)
    {
        var result = new List<uint>(totalCount);
        if (totalCount == 0)
            return result;

        int runCount = reader.ReadInt32();
        for (int r = 0; r < runCount; r++)
        {
            uint val = reader.ReadUInt32();
            int len = reader.ReadInt32();
            for (int i = 0; i < len; i++)
                result.Add(val);
        }
        return result;
    }

    public static UndertaleRoom DeserializeRoom(
        byte[] rawBytes,
        UndertaleData data,
        bool replaceExisting,
        bool addNew,
        List<ExportedCode> queuedCodes,
        out bool isNewRoom)
    {
        isNewRoom = false;

        using var memoryStream = new MemoryStream(rawBytes);
        using var reader = new BinaryReader(memoryStream);

        uint magic = reader.ReadUInt32();
        if (magic != MAGIC)
            throw new InvalidDataException("Invalid room file signature.");

        byte version = reader.ReadByte();
        if (version != FORMAT_VERSION)
            throw new InvalidDataException($"Unsupported room format version {version}.");

        bool isGms2InFile = reader.ReadBoolean();
        int compressedLen = reader.ReadInt32();
        byte[] compressedData = reader.ReadBytes(compressedLen);

        using var defIn = new MemoryStream(compressedData);
        using var defStream = new DeflateStream(defIn, CompressionMode.Decompress);
        using var defReader = new BinaryReader(defStream);

        string[] stringTable = StringTable.ReadFrom(defReader);
        string? GetString(int id) => (id >= 0 && id < stringTable.Length) ? stringTable[id] : null;

        string roomName = GetString(defReader.ReadInt32()) ?? throw new InvalidDataException("Room name missing.");
        string caption = GetString(defReader.ReadInt32()) ?? string.Empty;
        uint width = defReader.ReadUInt32();
        uint height = defReader.ReadUInt32();
        uint speed = defReader.ReadUInt32();
        bool persistent = defReader.ReadBoolean();
        uint bgColor = defReader.ReadUInt32();
        bool drawBgColor = defReader.ReadBoolean();
        uint flags = defReader.ReadUInt32();

        UndertaleRoom? existingRoom = data.Rooms.ByName(roomName);
        UndertaleRoom room;

        if (existingRoom != null)
        {
            if (!replaceExisting)
                return existingRoom;
            room = existingRoom;
            room.Backgrounds.Clear();
            room.Views.Clear();
            room.GameObjects.Clear();
            room.Tiles.Clear();
            room.Layers.Clear();
            room.Sequences.Clear();
        }
        else
        {
            if (!addNew)
                throw new InvalidOperationException($"Room {roomName} does not exist in target data and addNew is disabled.");

            room = new UndertaleRoom();
            isNewRoom = true;
        }

        room.Name = data.Strings.MakeString(roomName);
        room.Caption = data.Strings.MakeString(caption);
        room.Width = width;
        room.Height = height;
        room.Speed = speed;
        room.Persistent = persistent;
        room.BackgroundColor = bgColor;
        room.DrawBackgroundColor = drawBgColor;
        room.Flags = (UndertaleRoom.RoomEntryFlags)flags;

        // Creation Code
        bool hasCreationCode = defReader.ReadBoolean();
        if (hasCreationCode)
        {
            string? codeName = GetString(defReader.ReadInt32());
            string gml = defReader.ReadString();
            if (string.IsNullOrEmpty(codeName))
                codeName = $"gml_Room_{roomName}_Create";

            queuedCodes.Add(new ExportedCode { Name = codeName, Code = gml });
            var codeObj = data.Code.ByName(codeName);
            if (codeObj == null)
            {
                codeObj = new UndertaleCode { Name = data.Strings.MakeString(codeName) };
                data.Code.Add(codeObj);
            }
            room.CreationCodeId = codeObj;
        }
        else
        {
            room.CreationCodeId = null;
        }

        if (!isGms2InFile)
        {
            // GMS 1.x: Backgrounds
            int bgCount = defReader.ReadInt32();
            for (int i = 0; i < bgCount; i++)
            {
                bool enabled = defReader.ReadBoolean();
                bool foreground = defReader.ReadBoolean();
                string? bgName = GetString(defReader.ReadInt32());
                int x = defReader.ReadInt32();
                int y = defReader.ReadInt32();
                bool tileX = defReader.ReadBoolean();
                bool tileY = defReader.ReadBoolean();
                int spdX = defReader.ReadInt32();
                int spdY = defReader.ReadInt32();
                bool stretch = defReader.ReadBoolean();

                var bg = new UndertaleRoom.Background
                {
                    Enabled = enabled,
                    Foreground = foreground,
                    BackgroundDefinition = bgName != null ? data.Backgrounds.ByName(bgName) : null,
                    X = x,
                    Y = y,
                    TiledHorizontally = tileX,
                    TiledVertically = tileY,
                    SpeedX = spdX,
                    SpeedY = spdY,
                    Stretch = stretch
                };
                room.Backgrounds.Add(bg);
            }

            // GMS 1.x: Views
            int viewCount = defReader.ReadInt32();
            for (int i = 0; i < viewCount; i++)
            {
                bool enabled = defReader.ReadBoolean();
                int vx = defReader.ReadInt32();
                int vy = defReader.ReadInt32();
                int vw = defReader.ReadInt32();
                int vh = defReader.ReadInt32();
                int px = defReader.ReadInt32();
                int py = defReader.ReadInt32();
                int pw = defReader.ReadInt32();
                int ph = defReader.ReadInt32();
                uint bx = defReader.ReadUInt32();
                uint by = defReader.ReadUInt32();
                int sx = defReader.ReadInt32();
                int sy = defReader.ReadInt32();
                string? objName = GetString(defReader.ReadInt32());

                var view = new UndertaleRoom.View
                {
                    Enabled = enabled,
                    ViewX = vx,
                    ViewY = vy,
                    ViewWidth = vw,
                    ViewHeight = vh,
                    PortX = px,
                    PortY = py,
                    PortWidth = pw,
                    PortHeight = ph,
                    BorderX = bx,
                    BorderY = by,
                    SpeedX = sx,
                    SpeedY = sy,
                    ObjectId = objName != null ? data.GameObjects.ByName(objName) : null
                };
                room.Views.Add(view);
            }

            // GMS 1.x: GameObjects
            DeserializeGameObjects(defReader, GetString, room.GameObjects, data, queuedCodes, roomName);

            // GMS 1.x: Tiles
            DeserializeTiles(defReader, GetString, room.Tiles, data);
        }
        else
        {
            // GMS 2+: Layers
            int layerCount = defReader.ReadInt32();
            for (int l = 0; l < layerCount; l++)
            {
                uint layerId = defReader.ReadUInt32();
                string? layerName = GetString(defReader.ReadInt32()) ?? $"Layer_{layerId}";
                var layerType = (UndertaleRoom.LayerType)defReader.ReadUInt32();
                int depth = defReader.ReadInt32();
                float xOff = defReader.ReadSingle();
                float yOff = defReader.ReadSingle();
                float hSpd = defReader.ReadSingle();
                float vSpd = defReader.ReadSingle();
                bool isVis = defReader.ReadBoolean();

                var layer = new UndertaleRoom.Layer
                {
                    ParentRoom = room,
                    LayerId = layerId,
                    LayerName = data.Strings.MakeString(layerName),
                    LayerType = layerType,
                    LayerDepth = depth,
                    XOffset = xOff,
                    YOffset = yOff,
                    HSpeed = hSpd,
                    VSpeed = vSpd,
                    IsVisible = isVis
                };

                switch (layerType)
                {
                    case UndertaleRoom.LayerType.Instances:
                    {
                        var instData = new UndertaleRoom.Layer.LayerInstancesData();
                        layer.Data = instData;
                        DeserializeGameObjects(defReader, GetString, instData.Instances, data, queuedCodes, roomName);
                        foreach (var inst in instData.Instances)
                            room.GameObjects.Add(inst);
                        break;
                    }
                    case UndertaleRoom.LayerType.Tiles:
                    {
                        var tilesData = new UndertaleRoom.Layer.LayerTilesData { ParentLayer = layer };
                        layer.Data = tilesData;

                        string? bgName = GetString(defReader.ReadInt32());
                        tilesData.Background = bgName != null ? data.Backgrounds.ByName(bgName) : null;
                        uint tx = defReader.ReadUInt32();
                        uint ty = defReader.ReadUInt32();
                        tilesData.TilesX = tx;
                        tilesData.TilesY = ty;

                        int totalTiles = defReader.ReadInt32();
                        var flat = ReadRle(defReader, totalTiles);
                        tilesData.TileData = new uint[ty][];
                        int idx = 0;
                        for (int y = 0; y < ty; y++)
                        {
                            tilesData.TileData[y] = new uint[tx];
                            for (int x = 0; x < tx; x++)
                            {
                                tilesData.TileData[y][x] = (idx < flat.Count) ? flat[idx++] : 0;
                            }
                        }
                        break;
                    }
                    case UndertaleRoom.LayerType.Assets:
                    {
                        var assetsData = new UndertaleRoom.Layer.LayerAssetsData();
                        layer.Data = assetsData;

                        // Sprites
                        int sprCount = defReader.ReadInt32();
                        for (int s = 0; s < sprCount; s++)
                        {
                            string? sprInstName = GetString(defReader.ReadInt32());
                            string? sprResName = GetString(defReader.ReadInt32());
                            int x = defReader.ReadInt32();
                            int y = defReader.ReadInt32();
                            float sx = defReader.ReadSingle();
                            float sy = defReader.ReadSingle();
                            uint color = defReader.ReadUInt32();
                            float animSpd = defReader.ReadSingle();
                            var animType = (AnimationSpeedType)defReader.ReadUInt32();
                            float fIndex = defReader.ReadSingle();
                            float rot = defReader.ReadSingle();

                            var sprInst = new UndertaleRoom.SpriteInstance
                            {
                                Name = sprInstName != null ? data.Strings.MakeString(sprInstName) : null,
                                Sprite = sprResName != null ? data.Sprites.ByName(sprResName) : null,
                                X = x,
                                Y = y,
                                ScaleX = sx,
                                ScaleY = sy,
                                Color = color,
                                AnimationSpeed = animSpd,
                                AnimationSpeedType = animType,
                                FrameIndex = fIndex,
                                Rotation = rot
                            };
                            assetsData.Sprites.Add(sprInst);
                        }

                        // LegacyTiles
                        int tileCount = defReader.ReadInt32();
                        if (tileCount > 0)
                        {
                            assetsData.LegacyTiles = new UndertalePointerList<UndertaleRoom.Tile>();
                            DeserializeTiles(defReader, GetString, assetsData.LegacyTiles, data);
                        }

                        // Sequences
                        int seqCount = defReader.ReadInt32();
                        for (int sq = 0; sq < seqCount; sq++)
                        {
                            string? seqInstName = GetString(defReader.ReadInt32());
                            string? seqResName = GetString(defReader.ReadInt32());
                            int x = defReader.ReadInt32();
                            int y = defReader.ReadInt32();
                            float sx = defReader.ReadSingle();
                            float sy = defReader.ReadSingle();
                            uint color = defReader.ReadUInt32();
                            float animSpd = defReader.ReadSingle();
                            var animType = (AnimationSpeedType)defReader.ReadUInt32();
                            float fIndex = defReader.ReadSingle();
                            float rot = defReader.ReadSingle();

                            var seqInst = new UndertaleRoom.SequenceInstance
                            {
                                Name = seqInstName != null ? data.Strings.MakeString(seqInstName) : null,
                                Sequence = seqResName != null ? data.Sequences?.ByName(seqResName) : null,
                                X = x,
                                Y = y,
                                ScaleX = sx,
                                ScaleY = sy,
                                Color = color,
                                AnimationSpeed = animSpd,
                                AnimationSpeedType = animType,
                                FrameIndex = fIndex,
                                Rotation = rot
                            };
                            assetsData.Sequences.Add(seqInst);
                        }

                        // ParticleSystems
                        int psCount = defReader.ReadInt32();
                        for (int p = 0; p < psCount; p++)
                        {
                            string? psInstName = GetString(defReader.ReadInt32());
                            string? psResName = GetString(defReader.ReadInt32());
                            int x = defReader.ReadInt32();
                            int y = defReader.ReadInt32();
                            float sx = defReader.ReadSingle();
                            float sy = defReader.ReadSingle();
                            uint color = defReader.ReadUInt32();
                            float rot = defReader.ReadSingle();

                            var psInst = new UndertaleRoom.ParticleSystemInstance
                            {
                                Name = psInstName != null ? data.Strings.MakeString(psInstName) : null,
                                ParticleSystem = psResName != null ? data.ParticleSystems?.ByName(psResName) : null,
                                X = x,
                                Y = y,
                                ScaleX = sx,
                                ScaleY = sy,
                                Color = color,
                                Rotation = rot
                            };
                            assetsData.ParticleSystems.Add(psInst);
                        }

                        // TextItems
                        int textCount = defReader.ReadInt32();
                        for (int t = 0; t < textCount; t++)
                        {
                            string? txtInstName = GetString(defReader.ReadInt32());
                            string? fontName = GetString(defReader.ReadInt32());
                            string? textVal = GetString(defReader.ReadInt32());
                            int x = defReader.ReadInt32();
                            int y = defReader.ReadInt32();
                            float sx = defReader.ReadSingle();
                            float sy = defReader.ReadSingle();
                            uint color = defReader.ReadUInt32();
                            float rot = defReader.ReadSingle();
                            float ox = defReader.ReadSingle();
                            float oy = defReader.ReadSingle();
                            int align = defReader.ReadInt32();
                            float cSpace = defReader.ReadSingle();
                            float lSpace = defReader.ReadSingle();
                            float fw = defReader.ReadSingle();
                            float fh = defReader.ReadSingle();
                            bool wrap = defReader.ReadBoolean();

                            var txtInst = new UndertaleRoom.TextItemInstance
                            {
                                Name = txtInstName != null ? data.Strings.MakeString(txtInstName) : null,
                                Font = fontName != null ? data.Fonts.ByName(fontName) : null,
                                Text = textVal != null ? data.Strings.MakeString(textVal) : null,
                                X = x,
                                Y = y,
                                ScaleX = sx,
                                ScaleY = sy,
                                Color = color,
                                Rotation = rot,
                                OriginX = ox,
                                OriginY = oy,
                                Alignment = align,
                                CharSpacing = cSpace,
                                LineSpacing = lSpace,
                                FrameWidth = fw,
                                FrameHeight = fh,
                                Wrap = wrap
                            };
                            assetsData.TextItems.Add(txtInst);
                        }
                        break;
                    }
                    case UndertaleRoom.LayerType.Background:
                    {
                        var bgData = new UndertaleRoom.Layer.LayerBackgroundData { ParentLayer = layer };
                        layer.Data = bgData;

                        string? sprName = GetString(defReader.ReadInt32());
                        bgData.Sprite = sprName != null ? data.Sprites.ByName(sprName) : null;
                        bgData.Visible = defReader.ReadBoolean();
                        bgData.Foreground = defReader.ReadBoolean();
                        bgData.TiledHorizontally = defReader.ReadBoolean();
                        bgData.TiledVertically = defReader.ReadBoolean();
                        bgData.Stretch = defReader.ReadBoolean();
                        bgData.Color = defReader.ReadUInt32();
                        bgData.FirstFrame = defReader.ReadSingle();
                        bgData.AnimationSpeed = defReader.ReadSingle();
                        bgData.AnimationSpeedType = (AnimationSpeedType)defReader.ReadUInt32();
                        break;
                    }
                    case UndertaleRoom.LayerType.Effect:
                    {
                        var effData = new UndertaleRoom.Layer.LayerEffectData();
                        layer.Data = effData;

                        string? effType = GetString(defReader.ReadInt32());
                        effData.EffectType = effType != null ? data.Strings.MakeString(effType) : null;
                        int propCount = defReader.ReadInt32();
                        effData.Properties = new UndertaleSimpleList<UndertaleRoom.EffectProperty>();
                        for (int pr = 0; pr < propCount; pr++)
                        {
                            var kind = (UndertaleRoom.EffectProperty.PropertyType)defReader.ReadUInt32();
                            string? pName = GetString(defReader.ReadInt32());
                            string? pVal = GetString(defReader.ReadInt32());
                            effData.Properties.Add(new UndertaleRoom.EffectProperty
                            {
                                Kind = kind,
                                Name = pName != null ? data.Strings.MakeString(pName) : null,
                                Value = pVal != null ? data.Strings.MakeString(pVal) : null
                            });
                        }
                        break;
                    }
                    default:
                        break;
                }

                room.Layers.Add(layer);
            }
        }

        if (isNewRoom)
        {
            data.Rooms.Add(room);
            data.GeneralInfo.RoomOrder.Add(new UndertaleResourceById<UndertaleRoom, UndertaleChunkROOM> { Resource = room });
        }

        foreach (var obj in room.GameObjects)
        {
            if (obj.InstanceID >= data.GeneralInfo.LastObj)
                data.GeneralInfo.LastObj = obj.InstanceID + 1;
        }
        foreach (var tile in room.Tiles)
        {
            if (tile.InstanceID >= data.GeneralInfo.LastTile)
                data.GeneralInfo.LastTile = tile.InstanceID + 1;
        }

        return room;
    }

    private static void DeserializeGameObjects(
        BinaryReader reader,
        Func<int, string?> getString,
        IList<UndertaleRoom.GameObject> list,
        UndertaleData data,
        List<ExportedCode> queuedCodes,
        string roomName)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            string? objDefName = getString(reader.ReadInt32());
            uint instId = reader.ReadUInt32();
            float sx = reader.ReadSingle();
            float sy = reader.ReadSingle();
            uint col = reader.ReadUInt32();
            float rot = reader.ReadSingle();
            float imgSpd = reader.ReadSingle();
            int imgIdx = reader.ReadInt32();

            var go = new UndertaleRoom.GameObject
            {
                X = x,
                Y = y,
                ObjectDefinition = objDefName != null ? data.GameObjects.ByName(objDefName) : null,
                InstanceID = instId,
                ScaleX = sx,
                ScaleY = sy,
                Color = col,
                Rotation = rot,
                ImageSpeed = imgSpd,
                ImageIndex = imgIdx
            };

            bool hasCc = reader.ReadBoolean();
            if (hasCc)
            {
                string? codeName = getString(reader.ReadInt32());
                string gml = reader.ReadString();
                if (string.IsNullOrEmpty(codeName))
                    codeName = $"gml_RoomCC_{roomName}_{instId}_Create";

                queuedCodes.Add(new ExportedCode { Name = codeName, Code = gml });
                var codeObj = data.Code.ByName(codeName);
                if (codeObj == null)
                {
                    codeObj = new UndertaleCode { Name = data.Strings.MakeString(codeName) };
                    data.Code.Add(codeObj);
                }
                go.CreationCode = codeObj;
            }

            bool hasPre = reader.ReadBoolean();
            if (hasPre)
            {
                string? codeName = getString(reader.ReadInt32());
                string gml = reader.ReadString();
                if (string.IsNullOrEmpty(codeName))
                    codeName = $"gml_RoomCC_{roomName}_{instId}_PreCreate";

                queuedCodes.Add(new ExportedCode { Name = codeName, Code = gml });
                var codeObj = data.Code.ByName(codeName);
                if (codeObj == null)
                {
                    codeObj = new UndertaleCode { Name = data.Strings.MakeString(codeName) };
                    data.Code.Add(codeObj);
                }
                go.PreCreateCode = codeObj;
            }

            list.Add(go);
        }
    }

    private static void DeserializeTiles(
        BinaryReader reader,
        Func<int, string?> getString,
        IList<UndertaleRoom.Tile> list,
        UndertaleData data)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            bool spriteMode = reader.ReadBoolean();
            string? defName = getString(reader.ReadInt32());
            int srcX = reader.ReadInt32();
            int srcY = reader.ReadInt32();
            uint w = reader.ReadUInt32();
            uint h = reader.ReadUInt32();
            int depth = reader.ReadInt32();
            uint instId = reader.ReadUInt32();
            float sx = reader.ReadSingle();
            float sy = reader.ReadSingle();
            uint col = reader.ReadUInt32();

            var tile = new UndertaleRoom.Tile
            {
                X = x,
                Y = y,
                spriteMode = spriteMode,
                SourceX = srcX,
                SourceY = srcY,
                Width = w,
                Height = h,
                TileDepth = depth,
                InstanceID = instId,
                ScaleX = sx,
                ScaleY = sy,
                Color = col
            };

            if (defName != null)
            {
                if (spriteMode)
                    tile.SpriteDefinition = data.Sprites.ByName(defName);
                else
                    tile.BackgroundDefinition = data.Backgrounds.ByName(defName);
            }

            list.Add(tile);
        }
    }
}
