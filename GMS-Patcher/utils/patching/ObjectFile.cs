using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using UndertaleModLib;
using UndertaleModLib.Models;

public static class ObjectFile
{
    public class GameObjectDefinition
    {
        public required string Name { get; set; }
        public string? Sprite { get; set; }
        public bool Visible { get; set; } = true;
        public bool Solid { get; set; }
        public bool Persistent { get; set; }
        public int Depth { get; set; }
        public string? Parent { get; set; }
        public string? Mask { get; set; }
        public bool Managed { get; set; }
        public PhysicsDefinition? Physics { get; set; }
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
        public required string Type { get; set; }
        public string? Subtype { get; set; }
        public string? Target { get; set; }
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string SerializeToJson(UndertaleGameObject obj, UndertaleData data)
    {
        var def = new GameObjectDefinition
        {
            Name = obj.Name?.Content ?? string.Empty,
            Sprite = obj.Sprite?.Name?.Content,
            Visible = obj.Visible,
            Solid = obj.Solid,
            Persistent = obj.Persistent,
            Depth = obj.Depth,
            Parent = obj.ParentId?.Name?.Content,
            Mask = obj.TextureMaskId?.Name?.Content,
            Managed = obj.Managed
        };

        if (obj.UsesPhysics)
        {
            def.Physics = new PhysicsDefinition
            {
                CollisionShape = obj.CollisionShape.ToString(),
                Density = obj.Density,
                Restitution = obj.Restitution,
                Group = obj.Group,
                LinearDamping = obj.LinearDamping,
                AngularDamping = obj.AngularDamping,
                Friction = obj.Friction,
                Sensor = obj.IsSensor,
                Awake = obj.Awake,
                Kinematic = obj.Kinematic,
                Vertices = obj.PhysicsVertices?.Select(v => new PhysicsVertexDefinition { X = v.X, Y = v.Y }).ToList() ?? new()
            };
        }

        for (int typeIdx = 0; typeIdx < obj.Events.Count; typeIdx++)
        {
            var eventType = (EventType)typeIdx;
            var subEvents = obj.Events[typeIdx];
            if (subEvents == null) continue;

            foreach (var ev in subEvents)
            {
                var evDef = new EventDefinition
                {
                    Type = eventType.ToString()
                };

                if (eventType == EventType.Collision)
                {
                    int targetIdx = (int)ev.EventSubtype;
                    if (targetIdx >= 0 && targetIdx < data.GameObjects.Count)
                    {
                        evDef.Target = data.GameObjects[targetIdx]?.Name?.Content;
                    }
                    else
                    {
                        evDef.Subtype = ev.EventSubtype.ToString();
                    }
                }
                else
                {
                    evDef.Subtype = FormatSubtype(eventType, ev.EventSubtype);
                }

                def.Events.Add(evDef);
            }
        }

        return JsonSerializer.Serialize(def, _jsonOptions);
    }

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

        UndertaleGameObject? existingObj = data.GameObjects.ByName(def.Name);
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

        // Register event slots
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

    private static string FormatSubtype(EventType type, uint subtype)
    {
        switch (type)
        {
            case EventType.Step:
                if (Enum.IsDefined(typeof(EventSubtypeStep), subtype))
                    return ((EventSubtypeStep)subtype).ToString();
                break;
            case EventType.Draw:
                if (Enum.IsDefined(typeof(EventSubtypeDraw), subtype))
                    return ((EventSubtypeDraw)subtype).ToString();
                break;
            case EventType.Other:
                if (Enum.IsDefined(typeof(EventSubtypeOther), subtype))
                    return ((EventSubtypeOther)subtype).ToString();
                break;
            case EventType.Mouse:
                if (Enum.IsDefined(typeof(EventSubtypeMouse), subtype))
                    return ((EventSubtypeMouse)subtype).ToString();
                break;
            case EventType.Gesture:
                if (Enum.IsDefined(typeof(EventSubtypeGesture), subtype))
                    return ((EventSubtypeGesture)subtype).ToString();
                break;
            case EventType.Keyboard:
            case EventType.KeyPress:
            case EventType.KeyRelease:
                if (Enum.IsDefined(typeof(EventSubtypeKey), subtype))
                    return ((EventSubtypeKey)subtype).ToString();
                break;
        }
        return subtype.ToString();
    }

    private static uint ParseSubtype(EventType type, string? subtypeStr)
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
