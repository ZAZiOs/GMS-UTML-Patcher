using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace GMS_Patcher_Diff.Differs;

public class RoomDiffer
{
    public static int Process(UndertaleData original, UndertaleData modded, string outputDir, bool verbose)
    {
        string roomsDir = Path.Combine(outputDir, "rooms");
        Directory.CreateDirectory(roomsDir);

        var origRoomMap = new Dictionary<string, UndertaleRoom>(StringComparer.Ordinal);
        foreach (var r in original.Rooms)
        {
            if (r.Name?.Content != null)
                origRoomMap[r.Name.Content] = r;
        }

        var roomsToExport = new List<(UndertaleRoom room, byte[] serializedBytes)>();

        foreach (var modRoom in modded.Rooms)
        {
            if (modRoom.Name?.Content == null) continue;
            string name = modRoom.Name.Content;

            if (!origRoomMap.TryGetValue(name, out var origRoom))
            {
                byte[] modBytes = RoomFile.SerializeRoom(modRoom, modded);
                roomsToExport.Add((modRoom, modBytes));
                if (verbose) Console.WriteLine($"  [ROOMS] New room: {name}");
            }
            else
            {
                if (!AreRoomsLogicallyEqual(origRoom, modRoom))
                {
                    try
                    {
                        byte[] modBytes = RoomFile.SerializeRoom(modRoom, modded);
                        roomsToExport.Add((modRoom, modBytes));
                        if (verbose) Console.WriteLine($"  [ROOMS] Modified room: {name}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"  Warning: Failed to serialize room {name}: {ex.Message}");
                    }
                }
            }
        }

        if (roomsToExport.Count == 0)
        {
            try { Directory.Delete(roomsDir, true); } catch { }
            return 0;
        }

        Console.WriteLine($"Exporting {roomsToExport.Count} changed/new room(s)...");

        int count = 0;
        foreach (var (room, bytes) in roomsToExport)
        {
            string outPath = Path.Combine(roomsDir, $"{room.Name.Content}.room");
            File.WriteAllBytes(outPath, bytes);
            count++;
        }

        return count;
    }

    private static bool AreRoomsLogicallyEqual(UndertaleRoom a, UndertaleRoom b)
    {
        if (a.Width != b.Width ||
            a.Height != b.Height ||
            a.Persistent != b.Persistent ||
            a.BackgroundColor != b.BackgroundColor ||
            a.DrawBackgroundColor != b.DrawBackgroundColor ||
            (a.Caption?.Content ?? "") != (b.Caption?.Content ?? ""))
        {
            return false;
        }

        if ((a.CreationCodeId?.Name?.Content ?? "") != (b.CreationCodeId?.Name?.Content ?? ""))
            return false;

        // Instances
        if (a.GameObjects.Count != b.GameObjects.Count) return false;
        for (int i = 0; i < a.GameObjects.Count; i++)
        {
            var instA = a.GameObjects[i];
            var instB = b.GameObjects[i];
            if (instA.ObjectDefinition?.Name?.Content != instB.ObjectDefinition?.Name?.Content ||
                instA.X != instB.X ||
                instA.Y != instB.Y ||
                instA.ScaleX != instB.ScaleX ||
                instA.ScaleY != instB.ScaleY ||
                instA.Color != instB.Color)
            {
                return false;
            }
        }

        // Tiles
        if (a.Tiles.Count != b.Tiles.Count) return false;
        for (int i = 0; i < a.Tiles.Count; i++)
        {
            var tileA = a.Tiles[i];
            var tileB = b.Tiles[i];
            if (tileA.BackgroundDefinition?.Name?.Content != tileB.BackgroundDefinition?.Name?.Content ||
                tileA.X != tileB.X || tileA.Y != tileB.Y ||
                tileA.SourceX != tileB.SourceX || tileA.SourceY != tileB.SourceY ||
                tileA.Width != tileB.Width || tileA.Height != tileB.Height ||
                tileA.TileDepth != tileB.TileDepth)
            {
                return false;
            }
        }

        // Backgrounds (GMS1)
        if (a.Backgrounds.Count != b.Backgrounds.Count) return false;
        for (int i = 0; i < a.Backgrounds.Count; i++)
        {
            var bgA = a.Backgrounds[i];
            var bgB = b.Backgrounds[i];
            if (bgA.Enabled != bgB.Enabled ||
                bgA.Foreground != bgB.Foreground ||
                bgA.BackgroundDefinition?.Name?.Content != bgB.BackgroundDefinition?.Name?.Content ||
                bgA.X != bgB.X || bgA.Y != bgB.Y ||
                bgA.TiledHorizontally != bgB.TiledHorizontally ||
                bgA.TiledVertically != bgB.TiledVertically)
            {
                return false;
            }
        }

        // Layers (GMS2)
        if (a.Layers.Count != b.Layers.Count) return false;
        for (int i = 0; i < a.Layers.Count; i++)
        {
            var lA = a.Layers[i];
            var lB = b.Layers[i];
            if (lA.LayerName?.Content != lB.LayerName?.Content ||
                lA.LayerType != lB.LayerType ||
                lA.LayerDepth != lB.LayerDepth)
            {
                return false;
            }
        }

        return true;
    }
}

