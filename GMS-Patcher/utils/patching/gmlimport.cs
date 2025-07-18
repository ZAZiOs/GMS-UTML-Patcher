using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

class GMLImportConfig
{
    public required string directory { get; set; }
    public bool linkCode { get; set; } = true;
    public string importType { get; set; } = "basic";
}

class GMLImporter
{
    public static int Import(UndertaleData Data, GMLImportConfig config)
    {
        if (string.IsNullOrEmpty(config.directory))
        {
            Console.WriteLine("[GML][ERROR] Import directory is not set.");
            return 1;
        }
        
        string[] dirFiles = Directory.GetFiles(PatchFile.GetRelativePath(config.directory));

        if (dirFiles.Length == 0)
        {
            Console.WriteLine("[GML][ERROR] The selected folder is empty.");
            return 2;
        }
        else if (!dirFiles.Any(x => x.EndsWith(".gml")))
        {
            Console.WriteLine("[GML][ERROR] The selected folder doesn't contain any GML file.");
            return 3;
        }

        switch (config.importType.ToLower())
        {
            case "basic":
                return BasicGMLImport.ImportGML(Data, dirFiles);
            default:
                Console.WriteLine($"[GML][ERROR] Unknown import type: {config.importType}");
                return 4;
        }
    }
}

class BasicGMLImport
{
    public static int ImportGML(UndertaleData Data, string[] dirFiles)
    {
        UndertaleModLib.Compiler.CodeImportGroup importGroup = new UndertaleModLib.Compiler.CodeImportGroup(Data);

        foreach (string file in dirFiles)
        {
            string code = File.ReadAllText(file);
            string codeName = Path.GetFileNameWithoutExtension(file);
            importGroup.QueueReplace(codeName, code);
        }
        importGroup.Import();
        Console.WriteLine("[GML][SUCCESS] All files successfully imported.");
        return 0;
    }
}