using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using UndertaleModLib;

public static class TestFileValidator
{
    public static int Validate(string dataPath, UndertaleData game, Arguments arguments)
    {
        if (string.IsNullOrEmpty(arguments.PatcherFile))
        {
            Out.ERROR("CHECKS", "blue", 11, "No patch file provided.");
            return 11;
        }

        if (!File.Exists(arguments.PatcherFile))
        {
            Out.ERROR("CHECKS", "blue", 103, $"Patch file not found: {arguments.PatcherFile}");
            return 103;
        }

        try
        {
            var json = File.ReadAllText(arguments.PatcherFile);
            var root = JsonNode.Parse(json)?.AsObject();

            if (root is null || !root.TryGetPropertyValue("checks", out var checksNode) || checksNode is not JsonObject checks)
            {
                Out.ERROR("CHECKS", "blue", 201, "Invalid or missing 'checks' object in test file.");
                return 201;
            }

            Out.INFO("CHECKS", "blue", "Starting checks.");

            if (!CheckField("Name", game.GeneralInfo.Name.Content, checks) ||
                !CheckField("GMS2", game.IsGameMaker2(), checks) ||
                !CheckField("YYC", game.IsYYC(), checks))
            {
                Out.ERROR("CHECKS", "blue", 202, "Basic check failed (Name, GMS2, or YYC mismatch).");
                return 202;
            }

            if (!arguments.SkipTimestampCheck)
            {
                if (checks.TryGetPropertyValue("buildTimestamp", out var timestampNode) && 
                    timestampNode is JsonValue expectedTimestamp)
                {
                    const int TIMESTAMP_OFFSET = 86399; 
                    ulong expectedTs = expectedTimestamp.GetValue<ulong>();
                    ulong actualTs = game.GeneralInfo.Timestamp;
                    
                    if (actualTs != expectedTs)
                    {
                        string expectedDate = DateTimeOffset.FromUnixTimeSeconds((long)(expectedTs + TIMESTAMP_OFFSET)).ToString("yyyy.MM.dd HH:mm");
                        string actualDate = DateTimeOffset.FromUnixTimeSeconds((long)(actualTs + TIMESTAMP_OFFSET)).ToString("yyyy.MM.dd HH:mm");
                        
                        Out.FAIL("CHECKS", "blue",
                            $"Timestamp mismatch:\n" +
                            $"Expected: {expectedDate} (Unix: {expectedTs})\n" +
                            $"Actual:   {actualDate} (Unix: {actualTs})");
                        Out.INFO("CHECKS", "blue", "To skip timestamp check use --skip-timestamp, to update it use --make-checks");
                        return 203;
                    }
                }
                else
                {
                    Out.ERROR("CHECKS", "blue", 203, "Missing or invalid 'buildTimestamp' in checks.");
                    return 203;
                }
            }
            else
            {
                Out.SKIP("CHECKS", "blue", "Skipping timestamp check.");
            }

            if (!arguments.SkipHashCheck)
            {
                string actualHash = TestFileCommon.ComputeSha256(dataPath);
                if (!CheckField("SHA256", actualHash, checks))
                {
                    Out.FAIL("CHECKS", "blue", $"Hash mismatch: expected SHA256 does not match actual.");
                    Out.INFO("CHECKS", "blue", "To skip SHA256 check use --skip-hashcheck, to update it use --make-checks");
                    return 204;
                }
            }
            else
            {
                Out.SKIP("CHECKS", "blue", "Skipping SHA256 check.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Out.ERROR("CHECKS", "blue", 200, $"Failed to validate test file: {ex.Message}");
            return 200;
        }
    }

    private static bool CheckField<T>(string field, T actualValue, JsonObject checks)
    {
        if (!checks.TryGetPropertyValue(field, out var expectedNode))
        {
            Out.FAIL("CHECKS", "blue", $"Field '{field}' missing in test file.");
            return false;
        }

        try
        {
            var expectedValue = expectedNode.Deserialize<T>();
            if (!Equals(actualValue, expectedValue))
            {
                return false;
            }
            Out.PASS("CHECKS", "blue", $"{field} OK");
            return true;
        }
        catch
        {
            Out.FAIL("CHECKS", "blue", $"Could not deserialize or compare '{field}'.");
            return false;
        }
    }
}
