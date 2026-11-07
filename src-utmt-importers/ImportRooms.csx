// Script for UndertaleModTool to import rooms from compact binary .room format
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Compiler;

EnsureDataLoaded();

string importFolder = PromptChooseDirectory();
if (string.IsNullOrEmpty(importFolder))
    throw new ScriptException("The import folder was not set.");

string[] dirFiles = Directory.GetFiles(importFolder, "*.room");
if (dirFiles.Length == 0)
    throw new ScriptException("The selected folder doesn't contain any .room files.");

bool replaceExisting = ScriptQuestion("Replace existing rooms if they already exist in data.win?");
bool addNew = ScriptQuestion("Add new rooms if they don't exist yet?");
bool compileCode = ScriptQuestion("Automatically compile and link room & instance creation code?");

SetProgressBar(null, "Importing rooms", 0, dirFiles.Length);
StartProgressBarUpdater();

SyncBinding("Rooms, Strings, Code, CodeLocals, Scripts, GameObjects, Backgrounds, Sprites", true);

var queuedCodes = new List<StandaloneRoomDeserializer.ExportedCode>();
int importedCount = 0;

await Task.Run(() =>
{
    foreach (string file in dirFiles)
    {
        byte[] raw = File.ReadAllBytes(file);
        StandaloneRoomDeserializer.DeserializeRoom(
            raw,
            Data,
            replaceExisting,
            addNew,
            queuedCodes,
            out bool isNewRoom
        );
        importedCount++;
        IncrementProgress();
    }

    if (compileCode && queuedCodes.Count > 0)
    {
        SetProgressBar(null, "Compiling creation code...", dirFiles.Length, dirFiles.Length);
        var importGroup = new CodeImportGroup(Data);
        foreach (var qc in queuedCodes)
        {
            if (!string.IsNullOrWhiteSpace(qc.Code))
            {
                importGroup.QueueReplace(qc.Name, qc.Code);
            }
        }
        importGroup.Import();
    }
});

DisableAllSyncBindings();

await StopProgressBarUpdater();
HideProgressBar();

ScriptMessage($"All {importedCount} room(s) successfully imported!");

public static class StandaloneRoomDeserializer
{
    private const uint MAGIC = 0x4D524D47; // "GMRM"
    private const byte FORMAT_VERSION = 1;

    public class ExportedCode
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    private static string[] ReadStringTable(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        string[] table = new string[count];
        for (int i = 0; i < count; i++)
            table[i] = reader.ReadString();
        return table;
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

        string[] stringTable = ReadStringTable(defReader);
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

