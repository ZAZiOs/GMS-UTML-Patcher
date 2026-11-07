// Script for UndertaleModTool to export Game Settings & Entrypoints
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;

EnsureDataLoaded();

string savePath = PromptSaveFile("json", "JSON Settings File (*.json)|*.json");
if (string.IsNullOrEmpty(savePath))
    throw new ScriptException("Export file was not selected.");

var gen = Data.GeneralInfo;
if (gen == null)
    throw new ScriptException("GeneralInfo (GEN8) is missing in loaded data.");

bool sync = (gen.Info & (UndertaleGeneralInfo.InfoFlags.SyncVertex1 | UndertaleGeneralInfo.InfoFlags.SyncVertex2)) != 0;

var settings = new
{
    displayName = gen.DisplayName?.Content ?? "",
    gameName = gen.Name?.Content ?? "",
    fps = gen.GMS2FPS,
    defaultWindowWidth = gen.DefaultWindowWidth,
    defaultWindowHeight = gen.DefaultWindowHeight,
    flags = new
    {
        fullscreen = (gen.Info & UndertaleGeneralInfo.InfoFlags.Fullscreen) != 0,
        interpolate = (gen.Info & UndertaleGeneralInfo.InfoFlags.Interpolate) != 0,
        scale = (gen.Info & UndertaleGeneralInfo.InfoFlags.Scale) != 0,
        showCursor = (gen.Info & UndertaleGeneralInfo.InfoFlags.ShowCursor) != 0,
        sizeable = (gen.Info & UndertaleGeneralInfo.InfoFlags.Sizeable) != 0,
        sync = sync,
        borderless = (gen.Info & UndertaleGeneralInfo.InfoFlags.BorderlessWindow) != 0
    },
    roomOrder = new
    {
        startRoom = gen.RoomOrder.FirstOrDefault()?.Resource?.Name?.Content ?? "",
        order = gen.RoomOrder.Select(r => r.Resource?.Name?.Content ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
    },
    globalInitScripts = Data.GlobalInitScripts?.Select(g => g.Code?.Name?.Content ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>(),
    gameEndScripts = Data.GameEndScripts?.Select(g => g.Code?.Name?.Content ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>()
};

string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(savePath, json);

ScriptMessage($"Settings successfully exported to:\n{savePath}");

