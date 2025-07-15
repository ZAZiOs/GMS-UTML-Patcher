using System;

public class Arguments
{
    public string DataPath { get; set; }
    public string OutputPath { get; set; }
    public string PatcherFile { get; set; }
    public bool SkipTimestampCheck { get; set; }
    public bool SkipHashCheck { get; set; }
    public bool TestMode { get; set; }

    public string PatcherMode => PatcherFile != null ? "enabled" : null;

    public static Arguments Parse(string[] args)
    {
        var parsed = new Arguments();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--data-path":
                    var dataPath = GetArgValue(args, ++i, "--data-path");
                    parsed.DataPath = dataPath;
                    if (!args.Contains("--output"))
                    {
                        if (dataPath.EndsWith(".win", StringComparison.OrdinalIgnoreCase))
                        {
                            parsed.OutputPath = dataPath.Substring(0, dataPath.Length - 4) + ".patched.win";
                        }
                        else
                        {
                            parsed.OutputPath = dataPath + ".patched.win";
                        }
                    }
                    break;

                case "--output":
                    parsed.OutputPath = GetArgValue(args, ++i, "--output");
                    break;

                case "--patcher-file":
                    parsed.PatcherFile = GetArgValue(args, ++i, "--patcher");
                    break;

                case "--skip-timecheck":
                    parsed.SkipTimestampCheck = true;
                    parsed.SkipHashCheck = true;
                    break;

                case "--skip-hashcheck":
                    parsed.SkipHashCheck = true;
                    break;

                case "--make-checks":
                    parsed.TestMode = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        return parsed;
    }

    private static string GetArgValue(string[] args, int index, string optionName)
    {
        if (index >= args.Length)
            throw new ArgumentException($"Expected value after {optionName}");

        return args[index];
    }
}
