using UndertaleModLib;
using UndertaleModLib.Models;
using static UndertaleModLib.Models.UndertaleSound;
using System.Text.Json;
using System.Linq;

public class AudioImportConfig
{
    public string Directory { get; set; } = "";
    public bool ReplaceProperties { get; set; } = false;
    public bool Embed { get; set; } = false;
    public bool Decode { get; set; } = false;
    public bool UseFolderAsAudioGroup { get; set; } = false;
    public bool ManuallyConfigureEach { get; set; } = false;
    public Dictionary<string, AudioFileConfig>? Files { get; set; }
}

public class AudioFileConfig
{
    public bool? Embed { get; set; }
    public bool? Decode { get; set; }
    public string? Audiogroup { get; set; }
}

public static class AudioImporter
{
    public static int Import(UndertaleData data, AudioImportConfig config)
    {
        string importFolder = PatchFile.GetRelativePath(config.Directory);
        if (!Directory.Exists(importFolder))
        {
            Out.ERROR("AUDIO", "red", 311, $"Import folder doesn't exist: {importFolder}");
            return 311;
        }

        var dirFiles = Directory.GetFiles(importFolder);
        var folderName = new DirectoryInfo(importFolder).Name;

        bool replaceProps = config.ReplaceProperties;
        bool manual = config.ManuallyConfigureEach;
        bool globalEmbed = config.Embed;
        bool globalDecode = config.Decode;
        bool globalUseGroup = config.UseFolderAsAudioGroup;

        bool hasGroups = data.AudioGroups.Count > 0;
        int groupID = -1;

        Out.INFO("AUDIO", "red", $"Import from: {importFolder} ({dirFiles.Length} files)");

        foreach (var file in dirFiles)
        {
            var filename = Path.GetFileName(file);
            if (!filename.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) &&
                !filename.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = Path.GetFileNameWithoutExtension(file);
            bool isOgg = filename.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);

            AudioFileConfig? fc = null;
            if (config.Files != null && config.Files.TryGetValue(filename, out var tmp))
                fc = tmp;

            bool embed = manual ? (fc?.Embed ?? false) : (isOgg ? globalEmbed : true);
            bool decode = manual ? (fc?.Decode ?? false) : (isOgg ? globalDecode : false);
            string grpName = manual ? (fc?.Audiogroup ?? string.Empty) : (globalUseGroup ? folderName : string.Empty);
            bool useGroup = embed && hasGroups && !string.IsNullOrEmpty(grpName);

            var existing = data.Sounds.FirstOrDefault(s => s?.Name?.Content == name);
            Out.INFO("AUDIO", "red", existing != null 
                ? $"Replacing: {name}" 
                : $"Adding: {name}");

            if (useGroup && existing == null)
            {
                groupID = Enumerable.Range(0, data.AudioGroups.Count)
                    .FirstOrDefault(i => data.AudioGroups[i]?.Name?.Content == grpName);
                if (groupID < 0)
                {
                    data.AudioGroups.Add(new UndertaleAudioGroup { Name = data.Strings.MakeString(grpName) });
                    groupID = data.AudioGroups.Count - 1;
                }
            }
            else if (existing != null)
            {
                groupID = existing.GroupID;
            }

            int embedID = -1;
            if (embed && !useGroup)
            {
                var sd = new UndertaleEmbeddedAudio { Data = File.ReadAllBytes(file) };
                if (existing?.AudioFile != null)
                    data.EmbeddedAudio.Remove(existing.AudioFile);
                data.EmbeddedAudio.Add(sd);
                embedID = data.EmbeddedAudio.Count - 1;
            }

            var flags = AudioEntryFlags.Regular;
            if (isOgg && embed && decode)
                flags = AudioEntryFlags.IsEmbedded | AudioEntryFlags.IsCompressed | AudioEntryFlags.Regular;
            else if (isOgg && embed)
                flags = AudioEntryFlags.IsCompressed | AudioEntryFlags.Regular;
            else if (!isOgg)
                flags = AudioEntryFlags.IsEmbedded | AudioEntryFlags.Regular;

            bool inline = embed && !useGroup;
            UndertaleEmbeddedAudio? finalRef = inline && embedID >= 0 && embedID < data.EmbeddedAudio.Count
                ? data.EmbeddedAudio[embedID]
                : null;
            int finalID = inline ? embedID : -1;
            UndertaleAudioGroup? finalGrp = hasGroups
                ? (useGroup ? data.AudioGroups[groupID] : data.AudioGroups[data.GetBuiltinSoundGroupID()])
                : null;

            if (existing == null)
            {
                data.Sounds.Add(new UndertaleSound
                {
                    Name = data.Strings.MakeString(name),
                    Flags = flags,
                    Type = data.Strings.MakeString(isOgg ? ".ogg" : ".wav"),
                    File = data.Strings.MakeString(filename),
                    Effects = 0,
                    Volume = 1,
                    Pitch = 1,
                    AudioID = finalID,
                    AudioFile = finalRef,
                    AudioGroup = finalGrp,
                    GroupID = useGroup ? groupID : data.GetBuiltinSoundGroupID()
                });
            }
            else if (replaceProps)
            {
                existing.Flags = flags;
                existing.Type = data.Strings.MakeString(isOgg ? ".ogg" : ".wav");
                existing.File = data.Strings.MakeString(filename);
                existing.Effects = 0;
                existing.Volume = 1;
                existing.Pitch = 1;
                existing.AudioID = finalID;
                existing.AudioFile = finalRef;
                existing.AudioGroup = finalGrp;
                existing.GroupID = useGroup ? groupID : data.GetBuiltinSoundGroupID();
            }
            else
            {
                existing.AudioID = finalID;
                existing.AudioFile = finalRef;
            }
        }

        Out.SUCCESS("AUDIO", "red", "Import completed successfully");
        return 0;
    }
}