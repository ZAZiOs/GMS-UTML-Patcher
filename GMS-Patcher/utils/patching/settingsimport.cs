using System;
using System.Collections.Generic;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;

public class SettingsFlagsConfig
{
    public bool? fullscreen { get; set; }
    public bool? interpolate { get; set; }
    public bool? scale { get; set; }
    public bool? showCursor { get; set; }
    public bool? sizeable { get; set; }
    public bool? sync { get; set; }
    public bool? borderless { get; set; }
}

public class SettingsRoomOrderConfig
{
    public string? startRoom { get; set; }
    public List<string>? order { get; set; }
}

public class SettingsImportConfig
{
    public string? displayName { get; set; }
    public string? gameName { get; set; }
    public float? fps { get; set; }
    public uint? defaultWindowWidth { get; set; }
    public uint? defaultWindowHeight { get; set; }
    public SettingsFlagsConfig? flags { get; set; }
    public SettingsRoomOrderConfig? roomOrder { get; set; }
    public List<string>? globalInitScripts { get; set; }
    public List<string>? gameEndScripts { get; set; }
}

public class SettingsImporter
{
    public static int Apply(UndertaleData Data, SettingsImportConfig config)
    {
        if (Data.GeneralInfo == null)
        {
            Out.ERROR("SETTINGS", "green", 381, "GeneralInfo chunk (GEN8) is missing in data.win");
            return 381;
        }

        Out.INFO("SETTINGS", "green", "Applying game settings & entrypoints...");

        if (!string.IsNullOrEmpty(config.displayName))
        {
            Data.GeneralInfo.DisplayName = Data.Strings.MakeString(config.displayName);
            Out.INFO("SETTINGS", "green", $"DisplayName set to: '{config.displayName}'");
        }

        if (!string.IsNullOrEmpty(config.gameName))
        {
            Data.GeneralInfo.Name = Data.Strings.MakeString(config.gameName);
            Out.INFO("SETTINGS", "green", $"Game Name set to: '{config.gameName}'");
        }

        if (config.fps.HasValue)
        {
            Data.GeneralInfo.GMS2FPS = config.fps.Value;
            Out.INFO("SETTINGS", "green", $"Target FPS set to: {config.fps.Value}");
        }

        if (config.defaultWindowWidth.HasValue)
            Data.GeneralInfo.DefaultWindowWidth = config.defaultWindowWidth.Value;

        if (config.defaultWindowHeight.HasValue)
            Data.GeneralInfo.DefaultWindowHeight = config.defaultWindowHeight.Value;

        if (config.flags != null)
        {
            SetFlag(Data, UndertaleGeneralInfo.InfoFlags.Fullscreen, config.flags.fullscreen);
            SetFlag(Data, UndertaleGeneralInfo.InfoFlags.Interpolate, config.flags.interpolate);
            SetFlag(Data, UndertaleGeneralInfo.InfoFlags.Scale, config.flags.scale);
            SetFlag(Data, UndertaleGeneralInfo.InfoFlags.ShowCursor, config.flags.showCursor);
            SetFlag(Data, UndertaleGeneralInfo.InfoFlags.Sizeable, config.flags.sizeable);
            SetFlag(Data, UndertaleGeneralInfo.InfoFlags.BorderlessWindow, config.flags.borderless);

            if (config.flags.sync.HasValue)
            {
                var syncFlags = UndertaleGeneralInfo.InfoFlags.SyncVertex1 | UndertaleGeneralInfo.InfoFlags.SyncVertex2;
                if (config.flags.sync.Value)
                    Data.GeneralInfo.Info |= syncFlags;
                else
                    Data.GeneralInfo.Info &= ~syncFlags;
            }
            Out.INFO("SETTINGS", "green", "Display and engine flags updated.");
        }

        if (config.roomOrder != null)
        {
            if (!string.IsNullOrEmpty(config.roomOrder.startRoom))
            {
                var entry = Data.GeneralInfo.RoomOrder.FirstOrDefault(r => r.Resource?.Name?.Content == config.roomOrder.startRoom);
                if (entry != null)
                {
                    Data.GeneralInfo.RoomOrder.Remove(entry);
                    Data.GeneralInfo.RoomOrder.Insert(0, entry);
                    Out.INFO("SETTINGS", "green", $"Start room set to: '{config.roomOrder.startRoom}'");
                }
                else
                {
                    Out.WARN("SETTINGS", "green", $"Room '{config.roomOrder.startRoom}' not found in RoomOrder.");
                }
            }

            if (config.roomOrder.order != null && config.roomOrder.order.Count > 0)
            {
                var newOrder = new List<UndertaleResourceById<UndertaleRoom, UndertaleChunkROOM>>();
                foreach (string rName in config.roomOrder.order)
                {
                    var roomObj = Data.Rooms.ByName(rName);
                    if (roomObj != null)
                    {
                        newOrder.Add(new UndertaleResourceById<UndertaleRoom, UndertaleChunkROOM> { Resource = roomObj });
                    }
                }

                // Add any missing rooms to keep all rooms preserved
                foreach (var existing in Data.GeneralInfo.RoomOrder)
                {
                    if (existing.Resource != null && !newOrder.Any(n => n.Resource == existing.Resource))
                        newOrder.Add(existing);
                }

                Data.GeneralInfo.RoomOrder.Clear();
                foreach (var item in newOrder)
                    Data.GeneralInfo.RoomOrder.Add(item);

                Out.INFO("SETTINGS", "green", $"Reordered {config.roomOrder.order.Count} room(s) in RoomOrder.");
            }
        }

        if (config.globalInitScripts != null && config.globalInitScripts.Count > 0)
        {
            if (Data.GlobalInitScripts != null)
            {
                foreach (string scriptName in config.globalInitScripts)
                {
                    var codeObj = Data.Code.ByName(scriptName);
                    if (codeObj == null)
                    {
                        Out.WARN("SETTINGS", "green", $"GlobalInit script '{scriptName}' not found in Code.");
                        continue;
                    }

                    if (!Data.GlobalInitScripts.Any(g => g.Code == codeObj))
                    {
                        Data.GlobalInitScripts.Add(new UndertaleGlobalInit { Code = codeObj });
                        Out.INFO("SETTINGS", "green", $"Registered GlobalInit script: '{scriptName}'");
                    }
                }
            }
        }

        if (config.gameEndScripts != null && config.gameEndScripts.Count > 0)
        {
            if (Data.GameEndScripts != null)
            {
                foreach (string scriptName in config.gameEndScripts)
                {
                    var codeObj = Data.Code.ByName(scriptName);
                    if (codeObj == null)
                    {
                        Out.WARN("SETTINGS", "green", $"GameEnd script '{scriptName}' not found in Code.");
                        continue;
                    }

                    if (!Data.GameEndScripts.Any(g => g.Code == codeObj))
                    {
                        Data.GameEndScripts.Add(new UndertaleGlobalInit { Code = codeObj });
                        Out.INFO("SETTINGS", "green", $"Registered GameEnd script: '{scriptName}'");
                    }
                }
            }
        }

        Out.SUCCESS("SETTINGS", "green", "Game settings and entrypoints applied successfully.");
        return 0;
    }

    private static void SetFlag(UndertaleData data, UndertaleGeneralInfo.InfoFlags flag, bool? val)
    {
        if (!val.HasValue) return;
        if (val.Value)
            data.GeneralInfo.Info |= flag;
        else
            data.GeneralInfo.Info &= ~flag;
    }
}

