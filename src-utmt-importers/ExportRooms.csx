// Script for UndertaleModTool to export rooms to compact binary .room format
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using Underanalyzer.Decompiler;

EnsureDataLoaded();

List<UndertaleRoom> roomsToExport = new();

bool exportAll = ScriptQuestion("Do you want to export ALL rooms? (Click 'No' to pick specific rooms from a list)");
if (exportAll)
{
    foreach (var r in Data.Rooms)
    {
        if (r != null)
            roomsToExport.Add(r);
    }
}
else
{
    var picker = new RoomPickerWindow(Data.Rooms.Where(r => r != null).ToList());
    bool? dialogResult = picker.ShowDialog();
    if (dialogResult != true || picker.SelectedRooms.Count == 0)
    {
        ScriptMessage("No rooms selected. Export canceled.");
        return;
    }
    roomsToExport = picker.SelectedRooms;
}

string exportFolder = PromptChooseDirectory();
if (string.IsNullOrEmpty(exportFolder))
    throw new ScriptException("Export folder was not selected.");

Directory.CreateDirectory(exportFolder);

SetProgressBar(null, "Rooms Exported", 0, roomsToExport.Count);
StartProgressBarUpdater();

int exportedCount = 0;
await Task.Run(() =>
{
    foreach (var room in roomsToExport)
    {
        string filename = Path.Combine(exportFolder, $"{room.Name.Content}.room");
        byte[] bytes = StandaloneRoomPacker.SerializeRoom(room, Data);
        File.WriteAllBytes(filename, bytes);
        exportedCount++;
        IncrementProgress();
    }
});

await StopProgressBarUpdater();
HideProgressBar();

ScriptMessage($"Successfully exported {exportedCount} room(s) to:\n{exportFolder}");

public class RoomPickerItem
{
    public bool IsSelected { get; set; }
    public UndertaleRoom Room { get; set; }
    public string Name => Room.Name?.Content ?? "<unnamed>";
}

public class RoomPickerWindow : Window
{
    public List<UndertaleRoom> SelectedRooms { get; } = new();
    private readonly List<RoomPickerItem> _items;
    private readonly ListBox _listBox;
    private readonly TextBox _searchBox;

    public RoomPickerWindow(List<UndertaleRoom> rooms)
    {
        Title = "Select Rooms to Export";
        Width = 450;
        Height = 550;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _items = rooms.Select(r => new RoomPickerItem { IsSelected = false, Room = r }).ToList();

        var rootGrid = new Grid { Margin = new Thickness(10) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

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

        // Buttons Grid
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
            SelectedRooms.Clear();
            SelectedRooms.AddRange(_items.Where(x => x.IsSelected).Select(x => x.Room));
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

public static class StandaloneRoomPacker
{
    private const uint MAGIC = 0x4D524D47; // "GMRM"
    private const byte FORMAT_VERSION = 1;

    private class StringTable
    {
        private readonly List<string> _list = new();
        private readonly Dictionary<string, int> _dict = new(StringComparer.Ordinal);

        public int GetOrAdd(string? str)
        {
            if (str == null)
                return -1;
            if (_dict.TryGetValue(str, out int idx))
                return idx;
            idx = _list.Count;
            _list.Add(str);
            _dict[str] = idx;
            return idx;
        }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(_list.Count);
            foreach (var s in _list)
                writer.Write(s);
        }
    }

    private static string DecompileCode(UndertaleData data, UndertaleCode? code)
    {
        if (code == null)
            return string.Empty;
        try
        {
            var globalContext = new GlobalDecompileContext(data);
            var settings = data.ToolInfo?.DecompilerSettings ?? new DecompileSettings();
            var ctx = new Underanalyzer.Decompiler.DecompileContext(globalContext, code, settings);
            return ctx.DecompileToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static byte[] SerializeRoom(UndertaleRoom room, UndertaleData data)
    {
        bool isGms2 = data.IsGameMaker2();
        var stringTable = new StringTable();

        using var memoryStream = new MemoryStream();
        using (var defStream = new DeflateStream(memoryStream, CompressionLevel.Optimal, true))
        using (var writer = new BinaryWriter(defStream))
        {
            using var tempStream = new MemoryStream();
            using var tempWriter = new BinaryWriter(tempStream);

            // Room Properties
            tempWriter.Write(stringTable.GetOrAdd(room.Name?.Content));
            tempWriter.Write(stringTable.GetOrAdd(room.Caption?.Content));
            tempWriter.Write(room.Width);
            tempWriter.Write(room.Height);
            tempWriter.Write(room.Speed);
            tempWriter.Write(room.Persistent);
            tempWriter.Write(room.BackgroundColor);
            tempWriter.Write(room.DrawBackgroundColor);
            tempWriter.Write((uint)room.Flags);

            // Creation Code
            bool hasCreationCode = room.CreationCodeId != null;
            tempWriter.Write(hasCreationCode);
            if (hasCreationCode)
            {
                tempWriter.Write(stringTable.GetOrAdd(room.CreationCodeId.Name?.Content));
                tempWriter.Write(DecompileCode(data, room.CreationCodeId));
            }

            if (!isGms2)
            {
                // GMS 1.x: Backgrounds
                tempWriter.Write(room.Backgrounds.Count);
                foreach (var bg in room.Backgrounds)
                {
                    tempWriter.Write(bg.Enabled);
                    tempWriter.Write(bg.Foreground);
                    tempWriter.Write(stringTable.GetOrAdd(bg.BackgroundDefinition?.Name?.Content));
                    tempWriter.Write(bg.X);
                    tempWriter.Write(bg.Y);
                    tempWriter.Write(bg.TiledHorizontally);
                    tempWriter.Write(bg.TiledVertically);
                    tempWriter.Write(bg.SpeedX);
                    tempWriter.Write(bg.SpeedY);
                    tempWriter.Write(bg.Stretch);
                }

                // GMS 1.x: Views
                tempWriter.Write(room.Views.Count);
                foreach (var view in room.Views)
                {
                    tempWriter.Write(view.Enabled);
                    tempWriter.Write(view.ViewX);
                    tempWriter.Write(view.ViewY);
                    tempWriter.Write(view.ViewWidth);
                    tempWriter.Write(view.ViewHeight);
                    tempWriter.Write(view.PortX);
                    tempWriter.Write(view.PortY);
                    tempWriter.Write(view.PortWidth);
                    tempWriter.Write(view.PortHeight);
                    tempWriter.Write(view.BorderX);
                    tempWriter.Write(view.BorderY);
                    tempWriter.Write(view.SpeedX);
                    tempWriter.Write(view.SpeedY);
                    tempWriter.Write(stringTable.GetOrAdd(view.ObjectId?.Name?.Content));
                }

                // GMS 1.x: GameObjects
                SerializeGameObjects(room.GameObjects, tempWriter, stringTable, data);

                // GMS 1.x: Tiles
                SerializeTiles(room.Tiles, tempWriter, stringTable);
            }
            else
            {
                // GMS 2+: Layers
                tempWriter.Write(room.Layers.Count);
                foreach (var layer in room.Layers)
                {
                    tempWriter.Write(layer.LayerId);
                    tempWriter.Write(stringTable.GetOrAdd(layer.LayerName?.Content));
                    tempWriter.Write((uint)layer.LayerType);
                    tempWriter.Write(layer.LayerDepth);
                    tempWriter.Write(layer.XOffset);
                    tempWriter.Write(layer.YOffset);
                    tempWriter.Write(layer.HSpeed);
                    tempWriter.Write(layer.VSpeed);
                    tempWriter.Write(layer.IsVisible);

                    switch (layer.LayerType)
                    {
                        case UndertaleRoom.LayerType.Instances:
                        {
                            var instData = layer.InstancesData;
                            var instances = instData != null ? instData.Instances : (IList<UndertaleRoom.GameObject>)Array.Empty<UndertaleRoom.GameObject>();
                            SerializeGameObjects(instances, tempWriter, stringTable, data);
                            break;
                        }
                        case UndertaleRoom.LayerType.Tiles:
                        {
                            var tilesData = layer.TilesData;
                            tempWriter.Write(stringTable.GetOrAdd(tilesData?.Background?.Name?.Content));
                            uint tx = tilesData?.TilesX ?? 0;
                            uint ty = tilesData?.TilesY ?? 0;
                            tempWriter.Write(tx);
                            tempWriter.Write(ty);

                            var flatTiles = new List<uint>((int)(tx * ty));
                            if (tilesData?.TileData != null)
                            {
                                for (int y = 0; y < ty; y++)
                                {
                                    for (int x = 0; x < tx; x++)
                                    {
                                        flatTiles.Add(tilesData.TileData[y][x]);
                                    }
                                }
                            }
                            WriteRle(flatTiles, tempWriter);
                            break;
                        }
                        case UndertaleRoom.LayerType.Assets:
                        {
                            var assets = layer.AssetsData;
                            // Sprites
                            int sprCount = assets?.Sprites?.Count ?? 0;
                            tempWriter.Write(sprCount);
                            if (assets?.Sprites != null)
                            {
                                foreach (var spr in assets.Sprites)
                                {
                                    tempWriter.Write(stringTable.GetOrAdd(spr.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(spr.Sprite?.Name?.Content));
                                    tempWriter.Write(spr.X);
                                    tempWriter.Write(spr.Y);
                                    tempWriter.Write(spr.ScaleX);
                                    tempWriter.Write(spr.ScaleY);
                                    tempWriter.Write(spr.Color);
                                    tempWriter.Write(spr.AnimationSpeed);
                                    tempWriter.Write((uint)spr.AnimationSpeedType);
                                    tempWriter.Write(spr.FrameIndex);
                                    tempWriter.Write(spr.Rotation);
                                }
                            }

                            // LegacyTiles
                            int tileCount = assets?.LegacyTiles?.Count ?? 0;
                            tempWriter.Write(tileCount);
                            if (assets?.LegacyTiles != null)
                            {
                                SerializeTiles(assets.LegacyTiles, tempWriter, stringTable);
                            }

                            // Sequences
                            int seqCount = assets?.Sequences?.Count ?? 0;
                            tempWriter.Write(seqCount);
                            if (assets?.Sequences != null)
                            {
                                foreach (var seq in assets.Sequences)
                                {
                                    tempWriter.Write(stringTable.GetOrAdd(seq.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(seq.Sequence?.Name?.Content));
                                    tempWriter.Write(seq.X);
                                    tempWriter.Write(seq.Y);
                                    tempWriter.Write(seq.ScaleX);
                                    tempWriter.Write(seq.ScaleY);
                                    tempWriter.Write(seq.Color);
                                    tempWriter.Write(seq.AnimationSpeed);
                                    tempWriter.Write((uint)seq.AnimationSpeedType);
                                    tempWriter.Write(seq.FrameIndex);
                                    tempWriter.Write(seq.Rotation);
                                }
                            }

                            // ParticleSystems
                            int psCount = assets?.ParticleSystems?.Count ?? 0;
                            tempWriter.Write(psCount);
                            if (assets?.ParticleSystems != null)
                            {
                                foreach (var ps in assets.ParticleSystems)
                                {
                                    tempWriter.Write(stringTable.GetOrAdd(ps.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(ps.ParticleSystem?.Name?.Content));
                                    tempWriter.Write(ps.X);
                                    tempWriter.Write(ps.Y);
                                    tempWriter.Write(ps.ScaleX);
                                    tempWriter.Write(ps.ScaleY);
                                    tempWriter.Write(ps.Color);
                                    tempWriter.Write(ps.Rotation);
                                }
                            }

                            // TextItems
                            int textCount = assets?.TextItems?.Count ?? 0;
                            tempWriter.Write(textCount);
                            if (assets?.TextItems != null)
                            {
                                foreach (var txt in assets.TextItems)
                                {
                                    tempWriter.Write(stringTable.GetOrAdd(txt.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(txt.Font?.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(txt.Text?.Content));
                                    tempWriter.Write(txt.X);
                                    tempWriter.Write(txt.Y);
                                    tempWriter.Write(txt.ScaleX);
                                    tempWriter.Write(txt.ScaleY);
                                    tempWriter.Write(txt.Color);
                                    tempWriter.Write(txt.Rotation);
                                    tempWriter.Write(txt.OriginX);
                                    tempWriter.Write(txt.OriginY);
                                    tempWriter.Write(txt.Alignment);
                                    tempWriter.Write(txt.CharSpacing);
                                    tempWriter.Write(txt.LineSpacing);
                                    tempWriter.Write(txt.FrameWidth);
                                    tempWriter.Write(txt.FrameHeight);
                                    tempWriter.Write(txt.Wrap);
                                }
                            }
                            break;
                        }
                        case UndertaleRoom.LayerType.Background:
                        {
                            var bgData = layer.BackgroundData;
                            tempWriter.Write(stringTable.GetOrAdd(bgData?.Sprite?.Name?.Content));
                            tempWriter.Write(bgData?.Visible ?? true);
                            tempWriter.Write(bgData?.Foreground ?? false);
                            tempWriter.Write(bgData?.TiledHorizontally ?? false);
                            tempWriter.Write(bgData?.TiledVertically ?? false);
                            tempWriter.Write(bgData?.Stretch ?? false);
                            tempWriter.Write(bgData?.Color ?? 0xFF000000);
                            tempWriter.Write(bgData?.FirstFrame ?? 0f);
                            tempWriter.Write(bgData?.AnimationSpeed ?? 0f);
                            tempWriter.Write((uint)(bgData?.AnimationSpeedType ?? AnimationSpeedType.FPS));
                            break;
                        }
                        case UndertaleRoom.LayerType.Effect:
                        {
                            var eff = layer.EffectData;
                            tempWriter.Write(stringTable.GetOrAdd(eff?.EffectType?.Content));
                            int propCount = eff?.Properties?.Count ?? 0;
                            tempWriter.Write(propCount);
                            if (eff?.Properties != null)
                            {
                                foreach (var p in eff.Properties)
                                {
                                    tempWriter.Write((uint)p.Kind);
                                    tempWriter.Write(stringTable.GetOrAdd(p.Name?.Content));
                                    tempWriter.Write(stringTable.GetOrAdd(p.Value?.Content));
                                }
                            }
                            break;
                        }
                        default:
                            break;
                    }
                }
            }

            stringTable.WriteTo(writer);
            tempStream.Position = 0;
            tempStream.CopyTo(defStream);
        }

        byte[] compressedData = memoryStream.ToArray();

        using var resultStream = new MemoryStream();
        using var finalWriter = new BinaryWriter(resultStream);
        finalWriter.Write(MAGIC);
        finalWriter.Write(FORMAT_VERSION);
        finalWriter.Write(isGms2);
        finalWriter.Write(compressedData.Length);
        finalWriter.Write(compressedData);

        return resultStream.ToArray();
    }

    private static void SerializeGameObjects(IList<UndertaleRoom.GameObject> list, BinaryWriter writer, StringTable stringTable, UndertaleData data)
    {
        writer.Write(list.Count);
        foreach (var go in list)
        {
            writer.Write(go.X);
            writer.Write(go.Y);
            writer.Write(stringTable.GetOrAdd(go.ObjectDefinition?.Name?.Content));
            writer.Write(go.InstanceID);
            writer.Write(go.ScaleX);
            writer.Write(go.ScaleY);
            writer.Write(go.Color);
            writer.Write(go.Rotation);
            writer.Write(go.ImageSpeed);
            writer.Write(go.ImageIndex);

            bool hasCc = go.CreationCode != null;
            writer.Write(hasCc);
            if (hasCc)
            {
                writer.Write(stringTable.GetOrAdd(go.CreationCode.Name?.Content));
                writer.Write(DecompileCode(data, go.CreationCode));
            }

            bool hasPre = go.PreCreateCode != null;
            writer.Write(hasPre);
            if (hasPre)
            {
                writer.Write(stringTable.GetOrAdd(go.PreCreateCode.Name?.Content));
                writer.Write(DecompileCode(data, go.PreCreateCode));
            }
        }
    }

    private static void SerializeTiles(IList<UndertaleRoom.Tile> list, BinaryWriter writer, StringTable stringTable)
    {
        writer.Write(list.Count);
        foreach (var tile in list)
        {
            writer.Write(tile.X);
            writer.Write(tile.Y);
            writer.Write(tile.spriteMode);
            string? defName = tile.spriteMode ? tile.SpriteDefinition?.Name?.Content : tile.BackgroundDefinition?.Name?.Content;
            writer.Write(stringTable.GetOrAdd(defName));
            writer.Write(tile.SourceX);
            writer.Write(tile.SourceY);
            writer.Write(tile.Width);
            writer.Write(tile.Height);
            writer.Write(tile.TileDepth);
            writer.Write(tile.InstanceID);
            writer.Write(tile.ScaleX);
            writer.Write(tile.ScaleY);
            writer.Write(tile.Color);
        }
    }

    private static void WriteRle(List<uint> values, BinaryWriter writer)
    {
        writer.Write(values.Count);
        if (values.Count == 0)
            return;

        int i = 0;
        var runs = new List<(uint Val, int Len)>();
        while (i < values.Count)
        {
            uint current = values[i];
            int len = 1;
            while (i + 1 < values.Count && values[i + 1] == current && len < int.MaxValue)
            {
                len++;
                i++;
            }
            runs.Add((current, len));
            i++;
        }

        writer.Write(runs.Count);
        foreach (var run in runs)
        {
            writer.Write(run.Val);
            writer.Write(run.Len);
        }
    }
}

