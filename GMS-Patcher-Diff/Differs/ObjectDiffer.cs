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
                    if (!AreObjectEventsLogicallyEqual(modObj, modded, origObj, original))
                    {
                        differs = true;
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
                Console.Error.WriteLine($"  Warning: Failed to serialize object {obj.Name.Content}: {ex.Message}");
            }
        }

        return count;
    }

    private static bool AreObjectEventsLogicallyEqual(UndertaleGameObject modObj, UndertaleData modded,
                                                     UndertaleGameObject origObj, UndertaleData original)
    {
        int maxEvents = Math.Max(modObj.Events.Count, origObj.Events.Count);
        for (int t = 0; t < maxEvents; t++)
        {
            var modSub = t < modObj.Events.Count ? modObj.Events[t] : null;
            var origSub = t < origObj.Events.Count ? origObj.Events[t] : null;

            if (t == (int)EventType.PreCreate) // PreCreate special case (auto-generated in GMS2)
            {
                bool origHasReal = origSub != null && origSub.Any(e => !IsDummyPreCreate(e));
                bool modHasReal = modSub != null && modSub.Any(e => !IsDummyPreCreate(e));
                if (origHasReal != modHasReal)
                    return false;
                continue;
            }

            var modKeys = GetEventKeys(modSub, (EventType)t, modded);
            var origKeys = GetEventKeys(origSub, (EventType)t, original);

            if (!modKeys.SetEquals(origKeys))
                return false;
        }

        return true;
    }

    private static HashSet<string> GetEventKeys(IList<UndertaleGameObject.Event>? subList, EventType eventType, UndertaleData data)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (subList == null) return set;

        foreach (var ev in subList)
        {
            if (eventType == EventType.Collision)
            {
                int targetIdx = (int)ev.EventSubtype;
                string targetName = (targetIdx >= 0 && targetIdx < data.GameObjects.Count)
                    ? (data.GameObjects[targetIdx]?.Name?.Content ?? targetIdx.ToString())
                    : targetIdx.ToString();
                set.Add($"Collision:{targetName}");
            }
            else
            {
                set.Add($"{eventType}:{ev.EventSubtype}");
            }
        }

        return set;
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

