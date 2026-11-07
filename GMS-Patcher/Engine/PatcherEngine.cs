using System;
using System.IO;
using System.Threading;
using UndertaleModLib;

namespace GMS_UTML_Patcher.Engine;

public static class PatcherEngine
{
    public static int ApplyPatch(
        PatchOptions options,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(options.DataPath))
        {
            Out.ERROR("ARGUMENTS", "gray", 10, "--data-path is required.");
            return 10;
        }

        if (string.IsNullOrEmpty(options.PatcherFile))
        {
            Out.ERROR("ARGUMENTS", "gray", 11, "--patcher-file is required.");
            return 11;
        }

        if (!File.Exists(options.DataPath))
        {
            Out.ERROR("INPUT", "gray", 100, $"File not found: {options.DataPath}");
            return 100;
        }

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new PatchProgress(5, $"Loading data file: {Path.GetFileName(options.DataPath)}"));

        UndertaleData data;
        try
        {
            Out.INFO("INPUT", "gray", $"Loading data from: {options.DataPath}");
            using var stream = File.OpenRead(options.DataPath);
            data = UndertaleIO.Read(stream);
            Out.SUCCESS("INPUT", "gray", "Loaded data file successfully.");
        }
        catch (Exception ex)
        {
            Out.ERROR("INPUT", "gray", 102, $"Failed to load data: {ex.Message}");
            return 102;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (options.TestMode)
        {
            try
            {
                TestFileGenerator.Generate(options.DataPath, data, options.PatcherFile);
                progress?.Report(new PatchProgress(100, "Checks generated successfully."));
                return 0;
            }
            catch (Exception ex)
            {
                Out.ERROR("CHECKS", "blue", 20, $"Failed to generate/update checks in file: {ex.Message}");
                return 20;
            }
        }

        if (!File.Exists(options.PatcherFile))
        {
            Out.ERROR("INPUT", "gray", 103, $"Patcher file not found: {options.PatcherFile}");
            return 103;
        }

        progress?.Report(new PatchProgress(20, "Validating game integrity..."));

        int valid = TestFileValidator.Validate(options.DataPath, data, options);
        if (valid != 0)
        {
            Out.ERROR("CHECKS", "blue", valid, "Game validation failed.");
            return valid;
        }
        else
        {
            Out.SUCCESS("CHECKS", "blue", "File validated successfully.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new PatchProgress(40, "Applying patch components..."));

        int patchResult = PatchFile.Apply(data, options.PatcherFile);
        if (patchResult != 0)
        {
            return patchResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        string outputPath = options.OutputPath ?? options.DataPath;
        progress?.Report(new PatchProgress(80, $"Saving patched data: {Path.GetFileName(outputPath)}"));

        try
        {
            string? outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            Out.INFO("OUTPUT", "gray", $"Saving patched data to: {outputPath}");
            using var outStream = File.Create(outputPath);
            UndertaleIO.Write(outStream, data);
            Out.SUCCESS("OUTPUT", "gray", "Data saved successfully.");
        }
        catch (Exception ex)
        {
            Out.ERROR("OUTPUT", "gray", 104, $"Failed to save patched data: {ex.Message}");
            return 104;
        }

        progress?.Report(new PatchProgress(100, "Patch applied successfully."));
        return 0;
    }
}

