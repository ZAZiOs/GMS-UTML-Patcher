using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace GMS_Patcher_Diff.Differs;

public class AudioDiffer
{
    public static int Process(UndertaleData original, UndertaleData modded, string outputDir, bool verbose)
    {
        string audioDir = Path.Combine(outputDir, "audio");
        Directory.CreateDirectory(audioDir);

        var origSoundMap = new Dictionary<string, UndertaleSound>(StringComparer.Ordinal);
        foreach (var snd in original.Sounds)
        {
            if (snd.Name?.Content != null)
                origSoundMap[snd.Name.Content] = snd;
        }

        var soundsToExport = new List<UndertaleSound>();

        foreach (var modSnd in modded.Sounds)
        {
            if (modSnd.Name?.Content == null) continue;
            string name = modSnd.Name.Content;

            if (!origSoundMap.TryGetValue(name, out var origSnd))
            {
                soundsToExport.Add(modSnd);
                if (verbose) Console.WriteLine($"  [AUDIO] New sound: {name}");
            }
            else
            {
                byte[]? modData = modSnd.AudioFile?.Data;
                byte[]? origData = origSnd.AudioFile?.Data;

                if (modData == null && origData == null) continue;
                if (modData == null || origData == null)
                {
                    soundsToExport.Add(modSnd);
                    if (verbose) Console.WriteLine($"  [AUDIO] Modified sound: {name}");
                    continue;
                }

                if (modData.AsSpan().SequenceEqual(origData))
                {
                    continue;
                }

                // If both are WAV files, compare PCM payload ignoring metadata chunks
                if (IsWavPayloadEqual(origData, modData))
                {
                    continue;
                }

                soundsToExport.Add(modSnd);
                if (verbose) Console.WriteLine($"  [AUDIO] Modified sound: {name}");
            }
        }

        if (soundsToExport.Count == 0)
        {
            try { Directory.Delete(audioDir, true); } catch { }
            return 0;
        }

        Console.WriteLine($"Exporting {soundsToExport.Count} changed/new audio file(s)...");

        int count = 0;
        foreach (var snd in soundsToExport)
        {
            if (snd.AudioFile?.Data != null)
            {
                string extension = ".wav";
                if (snd.AudioFile.Data.Length > 4 &&
                    snd.AudioFile.Data[0] == 0x4F && snd.AudioFile.Data[1] == 0x67 &&
                    snd.AudioFile.Data[2] == 0x67 && snd.AudioFile.Data[3] == 0x53) // "OggS"
                {
                    extension = ".ogg";
                }

                string outPath = Path.Combine(audioDir, $"{snd.Name.Content}{extension}");
                File.WriteAllBytes(outPath, snd.AudioFile.Data);
                count++;
            }
        }

        return count;
    }

    private static bool IsWavPayloadEqual(byte[] a, byte[] b)
    {
        ReadOnlySpan<byte> fmtA = ExtractWavChunk(a, "fmt "u8);
        ReadOnlySpan<byte> fmtB = ExtractWavChunk(b, "fmt "u8);
        if (fmtA.IsEmpty || fmtB.IsEmpty || !fmtA.SequenceEqual(fmtB))
            return false;

        ReadOnlySpan<byte> dataA = ExtractWavChunk(a, "data"u8);
        ReadOnlySpan<byte> dataB = ExtractWavChunk(b, "data"u8);
        if (dataA.IsEmpty || dataB.IsEmpty || !dataA.SequenceEqual(dataB))
            return false;

        return true;
    }

    private static ReadOnlySpan<byte> ExtractWavChunk(byte[] wav, ReadOnlySpan<byte> chunkFourCC)
    {
        if (wav.Length < 12) return ReadOnlySpan<byte>.Empty;
        if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F') return ReadOnlySpan<byte>.Empty;

        int pos = 12; // skip RIFF header (4) + file size (4) + WAVE (4)
        while (pos + 8 <= wav.Length)
        {
            var header = wav.AsSpan(pos, 4);
            int chunkSize = BitConverter.ToInt32(wav, pos + 4);
            pos += 8;

            if (header.SequenceEqual(chunkFourCC))
            {
                if (pos + chunkSize <= wav.Length)
                    return wav.AsSpan(pos, chunkSize);
                return wav.AsSpan(pos);
            }

            pos += chunkSize;
            // Chunks in RIFF are word-aligned
            if ((chunkSize & 1) != 0) pos++;
        }

        return ReadOnlySpan<byte>.Empty;
    }
}
