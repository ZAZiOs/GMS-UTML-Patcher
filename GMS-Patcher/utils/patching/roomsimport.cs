using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Compiler;

public class RoomsImportConfig
{
    public required string directory { get; set; }
    public bool replaceExisting { get; set; } = true;
    public bool addNew { get; set; } = true;
    public bool compileCode { get; set; } = true;
}

public class RoomsImporter
{
    public static int Import(UndertaleData Data, RoomsImportConfig config)
    {
        string? folder = PatchFile.GetRelativePath(config.directory);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Out.ERROR("ROOMS", "magenta", 351, $"The rooms import directory does not exist: {config.directory}");
            return 351;
        }

        string[] roomFiles = Directory.GetFiles(folder, "*.room", SearchOption.TopDirectoryOnly);
        if (roomFiles.Length == 0)
        {
            Out.ERROR("ROOMS", "magenta", 352, $"No .room files found in directory: {folder}");
            return 352;
        }

        Out.INFO("ROOMS", "magenta", $"Found {roomFiles.Length} room file(s) in: {folder}");

        var queuedCodes = new List<RoomFile.ExportedCode>();
        int importedCount = 0;

        foreach (string file in roomFiles)
        {
            try
            {
                byte[] raw = File.ReadAllBytes(file);
                var room = RoomFile.DeserializeRoom(
                    raw,
                    Data,
                    config.replaceExisting,
                    config.addNew,
                    queuedCodes,
                    out bool isNewRoom
                );

                importedCount++;
                string status = isNewRoom ? "added" : "replaced";
                Out.INFO("ROOMS", "magenta", $"Room '{room.Name.Content}' {status} from {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Out.ERROR("ROOMS", "magenta", 353, $"Failed to import room from '{file}': {ex.Message}");
                return 353;
            }
        }

        if (config.compileCode && queuedCodes.Count > 0)
        {
            try
            {
                Out.INFO("ROOMS", "magenta", $"Compiling {queuedCodes.Count} creation code script(s)...");
                var importGroup = new CodeImportGroup(Data);
                foreach (var qc in queuedCodes)
                {
                    if (!string.IsNullOrWhiteSpace(qc.Code))
                    {
                        importGroup.QueueReplace(qc.Name, qc.Code);
                    }
                }
                importGroup.Import();
                Out.SUCCESS("ROOMS", "magenta", "All room creation code compiled successfully.");
            }
            catch (Exception ex)
            {
                Out.ERROR("ROOMS", "magenta", 354, $"Failed to compile room creation code: {ex.Message}");
                return 354;
            }
        }

        Out.SUCCESS("ROOMS", "magenta", $"Successfully imported {importedCount} room(s).");
        return 0;
    }
}

