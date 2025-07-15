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
            Console.WriteLine("[ERROR 11] No patch file provided.");
            return 11;
        }

        if (!File.Exists(arguments.PatcherFile))
        {
            Console.WriteLine($"[ERROR 103] Patch file not found: {arguments.PatcherFile}");
            return 103;
        }

        try
        {
            var json = File.ReadAllText(arguments.PatcherFile);
            var root = JsonNode.Parse(json)?.AsObject();

            if (root is null || !root.TryGetPropertyValue("checks", out var checksNode) || checksNode is not JsonObject checks)
            {
                Console.WriteLine("[ERROR 201] Invalid or missing 'checks' object in test file.");
                return 201;
            }

            Console.WriteLine("[INFO] Starting checks.");

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
                Console.WriteLine("[INFO] Skipping timestamp check.");

            if (!arguments.SkipHashCheck)
            {
                if (!CheckField("SHA256", TestFileCommon.ComputeSha256(dataPath), checks))
                {
                    return 203;
                }
            }
            else
                Console.WriteLine("[INFO] Skipping SHA256 check.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to validate test file: {ex.Message}");
            return 200;
        }
    }

    private static bool CheckField<T>(string field, T actualValue, JsonObject checks)
    {
        if (!checks.TryGetPropertyValue(field, out var expectedNode))
        {
            Console.WriteLine($"[FAIL] Field '{field}' missing in test file.");
            return false;
        }

        try
        {
            var expectedValue = expectedNode.Deserialize<T>();
            if (!Equals(actualValue, expectedValue))
            {
                Console.WriteLine($"[FAIL] {field} mismatch: expected '{expectedValue}', got '{actualValue}'");
                return false;
            }

            Console.WriteLine($"[PASS] {field} OK");
            return true;
        }
        catch
        {
            Console.WriteLine($"[FAIL] Could not deserialize or compare '{field}'.");
            return false;
        }
    }
}
