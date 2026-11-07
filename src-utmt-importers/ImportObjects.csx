// Script for UndertaleModTool to import Game Objects from JSON format
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using UndertaleModLib;
using UndertaleModLib.Models;

EnsureDataLoaded();

string importFolder = PromptChooseDirectory();
if (string.IsNullOrEmpty(importFolder))
    throw new ScriptException("The import folder was not set.");

string[] dirFiles = Directory.GetFiles(importFolder, "*.json");
if (dirFiles.Length == 0)
    throw new ScriptException("The selected folder doesn't contain any .json files.");

bool replaceExisting = ScriptQuestion("Replace existing objects if they already exist in data.win?");
bool addNew = ScriptQuestion("Add new objects if they don't exist yet?");

SetProgressBar(null, "Importing Game Objects", 0, dirFiles.Length);
StartProgressBarUpdater();

SyncBinding("GameObjects, Strings, Sprites, Code, CodeLocals", true);

int importedCount = 0;
await Task.Run(() =>
{
    foreach (string file in dirFiles)
    {
        string json = File.ReadAllText(file);
        StandaloneObjectDeserializer.DeserializeFromJson(
            json,
            Data,
            replaceExisting,
            addNew,
            out bool isNewObject
        );
        importedCount++;
        IncrementProgress();
    }
});

DisableAllSyncBindings();

await StopProgressBarUpdater();
HideProgressBar();

ScriptMessage($"All {importedCount} game object(s) successfully imported!");

public static class StandaloneObjectDeserializer
{
    public class GameObjectDefinition
    {
        public string Name { get; set; }
        public string Sprite { get; set; }
        public bool Visible { get; set; } = true;
        public bool Solid { get; set; }
        public bool Persistent { get; set; }
        public int Depth { get; set; }
        public string Parent { get; set; }
        public string Mask { get; set; }
        public bool Managed { get; set; }
        public PhysicsDefinition Physics { get; set; }
        public List<EventDefinition> Events { get; set; } = new();
    }

    public class PhysicsDefinition
    {
        public string CollisionShape { get; set; } = "Circle";
        public float Density { get; set; } = 0.5f;
        public float Restitution { get; set; } = 0.1f;
        public uint Group { get; set; }
        public float LinearDamping { get; set; } = 0.1f;
        public float AngularDamping { get; set; } = 0.1f;
        public float Friction { get; set; } = 0.2f;
        public bool Sensor { get; set; }
        public bool Awake { get; set; } = true;
        public bool Kinematic { get; set; }
        public List<PhysicsVertexDefinition> Vertices { get; set; } = new();
    }

    public class PhysicsVertexDefinition
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    public class EventDefinition
    {
        public string Type { get; set; }
        public string Subtype { get; set; }
        public string Target { get; set; }
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static UndertaleGameObject DeserializeFromJson(
        string json,
        UndertaleData data,
        bool replaceExisting,
        bool addNew,
        out bool isNewObject)
    {
        isNewObject = false;
        var def = JsonSerializer.Deserialize<GameObjectDefinition>(json, _jsonOptions)
                  ?? throw new InvalidDataException("Invalid game object JSON.");

        UndertaleGameObject existingObj = data.GameObjects.ByName(def.Name);
        UndertaleGameObject obj;

        if (existingObj != null)
        {
            if (!replaceExisting)
                return existingObj;
            obj = existingObj;
        }
        else
        {
            if (!addNew)
                throw new InvalidOperationException($"Object '{def.Name}' does not exist in target data and addNew is disabled.");

            obj = new UndertaleGameObject { Name = data.Strings.MakeString(def.Name) };
            data.GameObjects.Add(obj);
            isNewObject = true;
        }

        obj.Name = data.Strings.MakeString(def.Name);
        obj.Sprite = def.Sprite != null ? data.Sprites.ByName(def.Sprite) : null;
        obj.Visible = def.Visible;
        obj.Solid = def.Solid;
        obj.Persistent = def.Persistent;
        obj.Depth = def.Depth;
        obj.ParentId = def.Parent != null ? data.GameObjects.ByName(def.Parent) : null;
        obj.TextureMaskId = def.Mask != null ? data.Sprites.ByName(def.Mask) : null;
        obj.Managed = def.Managed;

        if (def.Physics != null)
        {
            obj.UsesPhysics = true;
            if (Enum.TryParse<CollisionShapeFlags>(def.Physics.CollisionShape, true, out var shape))
                obj.CollisionShape = shape;
            obj.Density = def.Physics.Density;
            obj.Restitution = def.Physics.Restitution;
            obj.Group = def.Physics.Group;
            obj.LinearDamping = def.Physics.LinearDamping;
            obj.AngularDamping = def.Physics.AngularDamping;
            obj.Friction = def.Physics.Friction;
            obj.IsSensor = def.Physics.Sensor;
            obj.Awake = def.Physics.Awake;
            obj.Kinematic = def.Physics.Kinematic;

            obj.PhysicsVertices.Clear();
            if (def.Physics.Vertices != null)
            {
                foreach (var v in def.Physics.Vertices)
                {
                    obj.PhysicsVertices.Add(new UndertaleGameObject.UndertalePhysicsVertex { X = v.X, Y = v.Y });
                }
            }
        }
        else
        {
            obj.UsesPhysics = false;
            obj.PhysicsVertices.Clear();
        }

        if (def.Events != null)
        {
            foreach (var evDef in def.Events)
            {
                if (!Enum.TryParse<EventType>(evDef.Type, true, out var eventType))
                    continue;

                uint subtypeVal = 0;
                if (eventType == EventType.Collision)
                {
                    if (!string.IsNullOrEmpty(evDef.Target))
                    {
                        var targetObj = data.GameObjects.ByName(evDef.Target);
                        if (targetObj != null)
                        {
                            int idx = data.GameObjects.IndexOf(targetObj);
                            if (idx >= 0)
                                subtypeVal = (uint)idx;
                        }
                    }
                    else if (uint.TryParse(evDef.Subtype, out var sVal))
                    {
                        subtypeVal = sVal;
                    }
                }
                else
                {
                    subtypeVal = ParseSubtype(eventType, evDef.Subtype);
                }

                obj.EventHandlerFor(eventType, subtypeVal, data);
            }
        }

        return obj;
    }

    private static uint ParseSubtype(EventType type, string subtypeStr)
    {
        if (string.IsNullOrWhiteSpace(subtypeStr))
            return 0;

        if (uint.TryParse(subtypeStr, out uint val))
            return val;

        switch (type)
        {
            case EventType.Step:
                if (Enum.TryParse<EventSubtypeStep>(subtypeStr, true, out var step))
                    return (uint)step;
                break;
            case EventType.Draw:
                if (Enum.TryParse<EventSubtypeDraw>(subtypeStr, true, out var draw))
                    return (uint)draw;
                break;
            case EventType.Other:
                if (Enum.TryParse<EventSubtypeOther>(subtypeStr, true, out var other))
                    return (uint)other;
                break;
            case EventType.Mouse:
                if (Enum.TryParse<EventSubtypeMouse>(subtypeStr, true, out var mouse))
                    return (uint)mouse;
                break;
            case EventType.Gesture:
                if (Enum.TryParse<EventSubtypeGesture>(subtypeStr, true, out var gesture))
                    return (uint)gesture;
                break;
            case EventType.Keyboard:
            case EventType.KeyPress:
            case EventType.KeyRelease:
                if (Enum.TryParse<EventSubtypeKey>(subtypeStr, true, out var key))
                    return (uint)key;
                break;
        }

        return 0;
    }
}

