using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using UndertaleModLib;

public static class TestFileGenerator
{
    public static void Generate(string dataPath, UndertaleData game, string outputPath)
    {
        JsonObject root;

        if (File.Exists(outputPath))
        {
            try
            {
                var existingJson = File.ReadAllText(outputPath);
                root = JsonNode.Parse(existingJson)?.AsObject() ?? new JsonObject();
            }
            catch (Exception ex)
            {
                Out.WARN("CHECKS", "blue", $"Could not parse existing JSON, creating new one. {ex.Message}");
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        root["checks"] = new JsonObject
        {
            ["Name"] = game.GeneralInfo.Name.Content,
            ["GMS2"] = game.IsGameMaker2(),
            ["YYC"] = game.IsYYC(),
            ["buildTimestamp"] = game.GeneralInfo.Timestamp,
            ["SHA256"] = TestFileCommon.ComputeSha256(dataPath)
        };

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);
        var checksNode = root["checks"];
        if (checksNode != null)
        {
            var checks = checksNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(checks);
        }
        else
        {
            Console.WriteLine("'checks' node is missing or null.");
        }
    }
}