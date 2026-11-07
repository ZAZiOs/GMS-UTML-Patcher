// Script for UndertaleModTool to export Game Objects to JSON format
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using UndertaleModLib;
using UndertaleModLib.Models;

EnsureDataLoaded();

List<UndertaleGameObject> objectsToExport = new();

bool exportAll = ScriptQuestion("Do you want to export ALL game objects? (Click 'No' to pick specific objects from a list)");
if (exportAll)
{
    foreach (var obj in Data.GameObjects)
    {
        if (obj != null)
            objectsToExport.Add(obj);
    }
}
else
{
    var picker = new ObjectPickerWindow(Data.GameObjects.Where(o => o != null).ToList());
    bool? dialogResult = picker.ShowDialog();
    if (dialogResult != true || picker.SelectedObjects.Count == 0)
    {
        ScriptMessage("No objects selected. Export canceled.");
        return;
    }
    objectsToExport = picker.SelectedObjects;
}

string exportFolder = PromptChooseDirectory();
if (string.IsNullOrEmpty(exportFolder))
    throw new ScriptException("Export folder was not selected.");

Directory.CreateDirectory(exportFolder);

SetProgressBar(null, "Objects Exported", 0, objectsToExport.Count);
StartProgressBarUpdater();

int exportedCount = 0;
await Task.Run(() =>
{
    foreach (var obj in objectsToExport)
    {
        string filename = Path.Combine(exportFolder, $"{obj.Name.Content}.json");
        string json = StandaloneObjectPacker.SerializeToJson(obj, Data);
        File.WriteAllText(filename, json);
        exportedCount++;
        IncrementProgress();
    }
});

await StopProgressBarUpdater();
HideProgressBar();

ScriptMessage($"Successfully exported {exportedCount} game object(s) to:\n{exportFolder}");

public class ObjectPickerItem
{
    public bool IsSelected { get; set; }
    public UndertaleGameObject Object { get; set; }
    public string Name => Object.Name?.Content ?? "<unnamed>";
}

public class ObjectPickerWindow : Window
{
    public List<UndertaleGameObject> SelectedObjects { get; } = new();
    private readonly List<ObjectPickerItem> _items;
    private readonly ListBox _listBox;
    private readonly TextBox _searchBox;

    public ObjectPickerWindow(List<UndertaleGameObject> objects)
    {
        Title = "Select Game Objects to Export";
        Width = 450;
        Height = 550;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _items = objects.Select(o => new ObjectPickerItem { IsSelected = false, Object = o }).ToList();

        var rootGrid = new Grid { Margin = new Thickness(10) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Search Box
        _searchBox = new TextBox { Margin = new Thickness(0, 0, 0, 8), FontSize = 14 };
        _searchBox.TextChanged += (s, e) => FilterItems();
        Grid.SetRow(_searchBox, 0);
        rootGrid.Children.Add(_searchBox);

        // List Box
        _listBox = new ListBox { SelectionMode = SelectionMode.Single };
        var itemTemplate = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(CheckBox));
        factory.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsSelected"));
        factory.SetBinding(ContentControl.ContentProperty, new Binding("Name"));
        itemTemplate.VisualTree = factory;
        _listBox.ItemTemplate = itemTemplate;
        _listBox.ItemsSource = _items;
        Grid.SetRow(_listBox, 1);
        rootGrid.Children.Add(_listBox);

        // Buttons
        var btnGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var btnAll = new Button { Content = "Select All", Width = 80, Margin = new Thickness(0, 0, 5, 0) };
        btnAll.Click += (s, e) => SetAllSelection(true);
        Grid.SetColumn(btnAll, 0);
        btnGrid.Children.Add(btnAll);

        var btnNone = new Button { Content = "Clear", Width = 60, Margin = new Thickness(0, 0, 5, 0) };
        btnNone.Click += (s, e) => SetAllSelection(false);
        Grid.SetColumn(btnNone, 1);
        btnGrid.Children.Add(btnNone);

        var btnOk = new Button { Content = "Export", Width = 80, Margin = new Thickness(0, 0, 5, 0), IsDefault = true };
        btnOk.Click += (s, e) =>
        {
            SelectedObjects.Clear();
            SelectedObjects.AddRange(_items.Where(x => x.IsSelected).Select(x => x.Object));
            DialogResult = true;
            Close();
        };
        Grid.SetColumn(btnOk, 3);
        btnGrid.Children.Add(btnOk);

        var btnCancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        Grid.SetColumn(btnCancel, 4);
        btnGrid.Children.Add(btnCancel);

        Grid.SetRow(btnGrid, 2);
        rootGrid.Children.Add(btnGrid);

        Content = rootGrid;
    }

    private void SetAllSelection(bool val)
    {
        foreach (var item in _items)
            item.IsSelected = val;
        _listBox.Items.Refresh();
    }

    private void FilterItems()
    {
        string filter = _searchBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(filter))
        {
            _listBox.ItemsSource = _items;
        }
        else
        {
            _listBox.ItemsSource = _items.Where(i => i.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
    }
}

public static class StandaloneObjectPacker
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
}

