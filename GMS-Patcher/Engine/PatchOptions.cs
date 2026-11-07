using System;

namespace GMS_UTML_Patcher.Engine;

public class PatchOptions
{
    public required string DataPath { get; set; }
    public required string PatcherFile { get; set; }
    public string? OutputPath { get; set; }
    public bool SkipTimestampCheck { get; set; }
    public bool SkipHashCheck { get; set; }
    public bool TestMode { get; set; }
    public bool Debug { get; set; }

    public static PatchOptions FromArguments(Arguments args)
    {
        return new PatchOptions
        {
            DataPath = args.DataPath ?? string.Empty,
            PatcherFile = args.PatcherFile ?? string.Empty,
            OutputPath = args.OutputPath,
            SkipTimestampCheck = args.SkipTimestampCheck,
            SkipHashCheck = args.SkipHashCheck,
            TestMode = args.TestMode
        };
    }
}

