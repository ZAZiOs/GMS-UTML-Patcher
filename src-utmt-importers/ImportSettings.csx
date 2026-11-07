// Script for UndertaleModTool to import Game Settings & Entrypoints
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;

EnsureDataLoaded();

string loadPath = PromptLoadFile("json", "JSON Settings File (*.json)|*.json");
if (string.IsNullOrEmpty(loadPath))
    throw new ScriptException("Settings file was not selected.");

if (!File.Exists(loadPath))
    throw new ScriptException("Selected file does not exist.");

var gen = Data.GeneralInfo;
if (gen == null)
    throw new ScriptException("GeneralInfo (GEN8) is missing in loaded data.");

using var doc = JsonDocument.Parse(File.ReadAllText(loadPath));
var root = doc.RootElement;

SyncBinding("GeneralInfo, Strings, Rooms, Code", true);

if (root.TryGetProperty("displayName", out var dnProp) && dnProp.ValueKind == JsonValueKind.String)
    gen.DisplayName = Data.Strings.MakeString(dnProp.GetString() ?? "");

if (root.TryGetProperty("gameName", out var gnProp) && gnProp.ValueKind == JsonValueKind.String)
    gen.Name = Data.Strings.MakeString(gnProp.GetString() ?? "");

if (root.TryGetProperty("fps", out var fpsProp) && fpsProp.TryGetSingle(out float fps))
    gen.GMS2FPS = fps;

if (root.TryGetProperty("defaultWindowWidth", out var wwProp) && wwProp.TryGetUInt32(out uint ww))
    gen.DefaultWindowWidth = ww;

if (root.TryGetProperty("defaultWindowHeight", out var whProp) && whProp.TryGetUInt32(out uint wh))
    gen.DefaultWindowHeight = wh;

if (root.TryGetProperty("flags", out var flagsProp) && flagsProp.ValueKind == JsonValueKind.Object)
{
    SetFlag(UndertaleGeneralInfo.InfoFlags.Fullscreen, flagsProp, "fullscreen");
    SetFlag(UndertaleGeneralInfo.InfoFlags.Interpolate, flagsProp, "interpolate");
    SetFlag(UndertaleGeneralInfo.InfoFlags.Scale, flagsProp, "scale");
    SetFlag(UndertaleGeneralInfo.InfoFlags.ShowCursor, flagsProp, "showCursor");
    SetFlag(UndertaleGeneralInfo.InfoFlags.Sizeable, flagsProp, "sizeable");
    SetFlag(UndertaleGeneralInfo.InfoFlags.BorderlessWindow, flagsProp, "borderless");

    if (flagsProp.TryGetProperty("sync", out var syncProp) && (syncProp.ValueKind == JsonValueKind.True || syncProp.ValueKind == JsonValueKind.False))
    {
        var syncFlags = UndertaleGeneralInfo.InfoFlags.SyncVertex1 | UndertaleGeneralInfo.InfoFlags.SyncVertex2;
        if (syncProp.GetBoolean())
            gen.Info |= syncFlags;
        else
            gen.Info &= ~syncFlags;
    }
}

if (root.TryGetProperty("roomOrder", out var roProp) && roProp.ValueKind == JsonValueKind.Object)
{
    if (roProp.TryGetProperty("startRoom", out var srProp) && srProp.ValueKind == JsonValueKind.String)
    {
        string startRoomName = srProp.GetString();
        var entry = gen.RoomOrder.FirstOrDefault(r => r.Resource?.Name?.Content == startRoomName);
        if (entry != null)
        {
            gen.RoomOrder.Remove(entry);
            gen.RoomOrder.Insert(0, entry);
        }
    }

    if (roProp.TryGetProperty("order", out var orderProp) && orderProp.ValueKind == JsonValueKind.Array)
    {
        var newOrder = new List<UndertaleResourceById<UndertaleRoom, UndertaleChunkROOM>>();
        foreach (var elem in orderProp.EnumerateArray())
        {
            string rName = elem.GetString();
            var room = Data.Rooms.ByName(rName);
            if (room != null)
                newOrder.Add(new UndertaleResourceById<UndertaleRoom, UndertaleChunkROOM> { Resource = room });
        }

        foreach (var existing in gen.RoomOrder)
        {
            if (existing.Resource != null && !newOrder.Any(n => n.Resource == existing.Resource))
                newOrder.Add(existing);
        }

        gen.RoomOrder.Clear();
        foreach (var item in newOrder)
            gen.RoomOrder.Add(item);
    }
}

if (root.TryGetProperty("globalInitScripts", out var giProp) && giProp.ValueKind == JsonValueKind.Array && Data.GlobalInitScripts != null)
{
    foreach (var elem in giProp.EnumerateArray())
    {
        string scriptName = elem.GetString();
        var codeObj = Data.Code.ByName(scriptName);
        if (codeObj != null && !Data.GlobalInitScripts.Any(g => g.Code == codeObj))
            Data.GlobalInitScripts.Add(new UndertaleGlobalInit { Code = codeObj });
    }
}

if (root.TryGetProperty("gameEndScripts", out var geProp) && geProp.ValueKind == JsonValueKind.Array && Data.GameEndScripts != null)
{
    foreach (var elem in geProp.EnumerateArray())
    {
        string scriptName = elem.GetString();
        var codeObj = Data.Code.ByName(scriptName);
        if (codeObj != null && !Data.GameEndScripts.Any(g => g.Code == codeObj))
            Data.GameEndScripts.Add(new UndertaleGlobalInit { Code = codeObj });
    }
}

DisableAllSyncBindings();
ScriptMessage("Game settings applied successfully!");

void SetFlag(UndertaleGeneralInfo.InfoFlags flag, JsonElement obj, string propName)
{
    if (obj.TryGetProperty(propName, out var p) && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False))
    {
        if (p.GetBoolean())
            gen.Info |= flag;
        else
            gen.Info &= ~flag;
    }
}

