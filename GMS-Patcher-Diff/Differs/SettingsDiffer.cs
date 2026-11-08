using System;
using System.Collections.Generic;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace GMS_Patcher_Diff.Differs;

public class SettingsDiffer
{
    public static SettingsImportConfig? Process(UndertaleData original, UndertaleData modded, bool verbose)
    {
        if (original.GeneralInfo == null || modded.GeneralInfo == null)
            return null;

        var origInfo = original.GeneralInfo;
        var modInfo = modded.GeneralInfo;

        var config = new SettingsImportConfig();
        bool hasChanges = false;

        // DisplayName & GameName
        if (modInfo.DisplayName?.Content != origInfo.DisplayName?.Content)
        {
            config.displayName = modInfo.DisplayName?.Content;
            hasChanges = true;
            if (verbose) Console.WriteLine($"  [SETTINGS] DisplayName changed: {config.displayName}");
        }

        if (modInfo.Name?.Content != origInfo.Name?.Content)
        {
            config.gameName = modInfo.Name?.Content;
            hasChanges = true;
            if (verbose) Console.WriteLine($"  [SETTINGS] GameName changed: {config.gameName}");
        }

        // FPS
        if (Math.Abs(modInfo.GMS2FPS - origInfo.GMS2FPS) > 0.01f)
        {
            config.fps = modInfo.GMS2FPS;
            hasChanges = true;
            if (verbose) Console.WriteLine($"  [SETTINGS] FPS changed: {config.fps}");
        }

        // Window size
        if (modInfo.DefaultWindowWidth != origInfo.DefaultWindowWidth)
        {
            config.defaultWindowWidth = modInfo.DefaultWindowWidth;
            hasChanges = true;
        }

        if (modInfo.DefaultWindowHeight != origInfo.DefaultWindowHeight)
        {
            config.defaultWindowHeight = modInfo.DefaultWindowHeight;
            hasChanges = true;
        }

        // Flags
        var flagsConfig = new SettingsFlagsConfig();
        bool flagsChanged = false;

        CheckFlag(UndertaleGeneralInfo.InfoFlags.Fullscreen, ref flagsChanged, val => flagsConfig.fullscreen = val, origInfo, modInfo);
        CheckFlag(UndertaleGeneralInfo.InfoFlags.Interpolate, ref flagsChanged, val => flagsConfig.interpolate = val, origInfo, modInfo);
        CheckFlag(UndertaleGeneralInfo.InfoFlags.Scale, ref flagsChanged, val => flagsConfig.scale = val, origInfo, modInfo);
        CheckFlag(UndertaleGeneralInfo.InfoFlags.ShowCursor, ref flagsChanged, val => flagsConfig.showCursor = val, origInfo, modInfo);
        CheckFlag(UndertaleGeneralInfo.InfoFlags.Sizeable, ref flagsChanged, val => flagsConfig.sizeable = val, origInfo, modInfo);
        CheckFlag(UndertaleGeneralInfo.InfoFlags.BorderlessWindow, ref flagsChanged, val => flagsConfig.borderless = val, origInfo, modInfo);

        var syncFlags = UndertaleGeneralInfo.InfoFlags.SyncVertex1 | UndertaleGeneralInfo.InfoFlags.SyncVertex2;
        bool origSync = (origInfo.Info & syncFlags) != 0;
        bool modSync = (modInfo.Info & syncFlags) != 0;
        if (origSync != modSync)
        {
            flagsConfig.sync = modSync;
            flagsChanged = true;
        }

        if (flagsChanged)
        {
            config.flags = flagsConfig;
            hasChanges = true;
            if (verbose) Console.WriteLine("  [SETTINGS] Engine flags changed.");
        }

        // Room Order / startRoom
        string? origStartRoom = origInfo.RoomOrder.FirstOrDefault()?.Resource?.Name?.Content;
        string? modStartRoom = modInfo.RoomOrder.FirstOrDefault()?.Resource?.Name?.Content;
        if (origStartRoom != modStartRoom && !string.IsNullOrEmpty(modStartRoom))
        {
            config.roomOrder = new SettingsRoomOrderConfig { startRoom = modStartRoom };
            hasChanges = true;
            if (verbose) Console.WriteLine($"  [SETTINGS] Start room changed: {modStartRoom}");
        }

        // Global Init Scripts (FORM.GLOB)
        if (modded.GlobalInitScripts != null)
        {
            var origGlob = new HashSet<string>(
                (original.GlobalInitScripts ?? Enumerable.Empty<UndertaleGlobalInit>())
                .Select(s => s.Code?.Name?.Content ?? "")
                .Where(s => !string.IsNullOrEmpty(s)),
                StringComparer.Ordinal);

            var newGlob = modded.GlobalInitScripts
                .Select(s => s.Code?.Name?.Content ?? "")
                .Where(s => !string.IsNullOrEmpty(s) && !origGlob.Contains(s))
                .ToList();

            if (newGlob.Count > 0)
            {
                config.globalInitScripts = newGlob;
                hasChanges = true;
                if (verbose) Console.WriteLine($"  [SETTINGS] Added {newGlob.Count} globalInitScript(s).");
            }
        }

        // Game End Scripts (FORM.GMEN)
        if (modded.GameEndScripts != null)
        {
            var origGmen = new HashSet<string>(
                (original.GameEndScripts ?? Enumerable.Empty<UndertaleGlobalInit>())
                .Select(s => s.Code?.Name?.Content ?? "")
                .Where(s => !string.IsNullOrEmpty(s)),
                StringComparer.Ordinal);

            var newGmen = modded.GameEndScripts
                .Select(s => s.Code?.Name?.Content ?? "")
                .Where(s => !string.IsNullOrEmpty(s) && !origGmen.Contains(s))
                .ToList();

            if (newGmen.Count > 0)
            {
                config.gameEndScripts = newGmen;
                hasChanges = true;
                if (verbose) Console.WriteLine($"  [SETTINGS] Added {newGmen.Count} gameEndScript(s).");
            }
        }

        return hasChanges ? config : null;
    }

    private static void CheckFlag(UndertaleGeneralInfo.InfoFlags flag, ref bool changed, Action<bool> setter, UndertaleGeneralInfo orig, UndertaleGeneralInfo mod)
    {
        bool origHas = orig.Info.HasFlag(flag);
        bool modHas = mod.Info.HasFlag(flag);
        if (origHas != modHas)
        {
            setter(modHas);
            changed = true;
        }
    }
}
