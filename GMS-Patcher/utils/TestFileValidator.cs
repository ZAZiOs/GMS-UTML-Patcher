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

            // Сравниваем поля
            bool success = true;

            success &= CheckField("Name", game.GeneralInfo.Name.Content, checks);
            success &= CheckField("GMS2", game.IsGameMaker2(), checks);
            success &= CheckField("YYC", game.IsYYC(), checks);

            if (!arguments.SkipTimestampCheck)
            {
                if (!CheckField("buildTimestamp", game.GeneralInfo.Timestamp, checks))
                {
                    return 202;
                }
            }
            else
                Out.SKIP("CHECKS", "blue", "Skipping timestamp check.");

            if (!arguments.SkipHashCheck)
            {
                if (!CheckField("SHA256", TestFileCommon.ComputeSha256(dataPath), checks))
                {
                    return 203;
                }
            }
            else
                Out.SKIP("CHECKS", "blue", "Skipping SHA256 check.");

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
                Out.FAIL("CHECKS", "blue", $"{field} mismatch: expected '{expectedValue}', got '{actualValue}'");
                Out.INFO("CHECKS", "blue", "To skip SHA256 check - pass --skip-hashcheck");
                Out.INFO("CHECKS", "blue", "To skip Timestamp check - pass --skip-timecheck");
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
