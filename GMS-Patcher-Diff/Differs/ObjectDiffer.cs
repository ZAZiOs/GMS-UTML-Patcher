using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace GMS_Patcher_Diff.Differs;

public class ObjectDiffer
{
    public static int Process(UndertaleData original, UndertaleData modded, string outputDir, bool verbose)
    {
        string objectsDir = Path.Combine(outputDir, "objects");
        Directory.CreateDirectory(objectsDir);

        var origObjMap = new Dictionary<string, UndertaleGameObject>(StringComparer.Ordinal);
        foreach (var obj in original.GameObjects)
        {
            if (obj.Name?.Content != null)
                origObjMap[obj.Name.Content] = obj;
        }

        var objectsToExport = new List<UndertaleGameObject>();

        foreach (var modObj in modded.GameObjects)
        {
            if (modObj.Name?.Content == null) continue;
            string name = modObj.Name.Content;

            if (!origObjMap.TryGetValue(name, out var origObj))
            {
                objectsToExport.Add(modObj);
                if (verbose) Console.WriteLine($"  [OBJECTS] New object: {name}");
            }
            else
            {
                bool differs = false;

                if (modObj.Sprite?.Name?.Content != origObj.Sprite?.Name?.Content ||
                    modObj.Visible != origObj.Visible ||
                    modObj.Solid != origObj.Solid ||
                    modObj.Persistent != origObj.Persistent ||
                    modObj.Depth != origObj.Depth ||
                    modObj.ParentId?.Name?.Content != origObj.ParentId?.Name?.Content ||
                    modObj.TextureMaskId?.Name?.Content != origObj.TextureMaskId?.Name?.Content ||
                    modObj.UsesPhysics != origObj.UsesPhysics)
                {
                    differs = true;
                }
                else
                {
                    // Compare events 0..13, ignoring dummy PreCreate (event 14) if original had none
                    int maxEvents = Math.Max(modObj.Events.Count, origObj.Events.Count);
                    for (int t = 0; t < maxEvents; t++)
                    {
                        var modSub = t < modObj.Events.Count ? modObj.Events[t] : null;
                        var origSub = t < origObj.Events.Count ? origObj.Events[t] : null;

                        if (t == 14) // PreCreate special case (auto-generated in GMS2)
                        {
                            bool origHasReal = origSub != null && origSub.Any(e => !IsDummyPreCreate(e));
                            bool modHasReal = modSub != null && modSub.Any(e => !IsDummyPreCreate(e));
                            if (origHasReal != modHasReal)
                            {
                                differs = true;
                                break;
                            }
                            continue;
                        }

                        int modCount = modSub?.Count ?? 0;
                        int origCount = origSub?.Count ?? 0;
                        if (modCount != origCount)
                        {
                            differs = true;
                            break;
                        }

                        for (int e = 0; e < modCount; e++)
                        {
                            var mEv = modSub![e];
                            var oEv = origSub![e];
                            if (mEv.EventSubtype != oEv.EventSubtype || mEv.Actions.Count != oEv.Actions.Count)
                            {
                                differs = true;
                                break;
                            }

                            for (int a = 0; a < mEv.Actions.Count; a++)
                            {
                                if (mEv.Actions[a].CodeId?.Name?.Content != oEv.Actions[a].CodeId?.Name?.Content)
                                {
                                    differs = true;
                                    break;
                                }
                            }
                            if (differs) break;
                        }
                        if (differs) break;
                    }
                }

                if (differs)
                {
                    objectsToExport.Add(modObj);
                    if (verbose) Console.WriteLine($"  [OBJECTS] Modified object: {name}");
                }
            }
        }

        if (objectsToExport.Count == 0)
        {
            try { Directory.Delete(objectsDir, true); } catch { }
            return 0;
        }

        Console.WriteLine($"Exporting {objectsToExport.Count} changed/new game object(s)...");

        int count = 0;
        foreach (var obj in objectsToExport)
        {
            try
            {
                string json = ObjectFile.SerializeToJson(obj, modded);
                string outPath = Path.Combine(objectsDir, $"{obj.Name.Content}.json");
                File.WriteAllText(outPath, json);
                count++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: Failed to export object {obj.Name.Content}: {ex.Message}");
            }
        }

        return count;
    }

    private static bool IsDummyPreCreate(UndertaleGameObject.Event ev)
    {
        if (ev.Actions.Count == 0) return true;
        if (ev.Actions.Count == 1)
        {
            var code = ev.Actions[0].CodeId;
            if (code == null) return true;
            if (code.Instructions.Count == 2 &&
                code.Instructions[0].Kind == UndertaleInstruction.Opcode.Call &&
                code.Instructions[0].ValueFunction?.Name?.Content == "event_inherited" &&
                code.Instructions[1].Kind == UndertaleInstruction.Opcode.Popz)
            {
                return true;
            }
        }
        return false;
    }
}

