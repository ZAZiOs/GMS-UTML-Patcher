// Script for UndertaleModTool to export shaders
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using UndertaleModLib;
using UndertaleModLib.Models;

EnsureDataLoaded();

List<UndertaleShader> shadersToExport = new();

bool exportAll = ScriptQuestion("Do you want to export ALL shaders? (Click 'No' to pick specific shaders from a list)");
if (exportAll)
{
    foreach (var sh in Data.Shaders)
    {
        if (sh != null)
            shadersToExport.Add(sh);
    }
}
else
{
    var picker = new ShaderPickerWindow(Data.Shaders.Where(s => s != null).ToList());
    bool? dialogResult = picker.ShowDialog();
    if (dialogResult != true || picker.SelectedShaders.Count == 0)
    {
        ScriptMessage("No shaders selected. Export canceled.");
        return;
    }
    shadersToExport = picker.SelectedShaders;
}

string exportFolder = PromptChooseDirectory();
if (string.IsNullOrEmpty(exportFolder))
    throw new ScriptException("Export folder was not selected.");

Directory.CreateDirectory(exportFolder);

SetProgressBar(null, "Shaders Exported", 0, shadersToExport.Count);
StartProgressBarUpdater();

int exportedCount = 0;
await Task.Run(() =>
{
    foreach (var sh in shadersToExport)
    {
        string shName = sh.Name?.Content ?? "unnamed_shader";
        string targetDir = Path.Combine(exportFolder, shName);
        Directory.CreateDirectory(targetDir);

        // GLSL ES / GLSL
        string vGlsl = sh.GLSL_ES_Vertex?.Content ?? sh.GLSL_Vertex?.Content;
        if (!string.IsNullOrEmpty(vGlsl))
            File.WriteAllText(Path.Combine(targetDir, "vertex.glsl"), vGlsl);

        string fGlsl = sh.GLSL_ES_Fragment?.Content ?? sh.GLSL_Fragment?.Content;
        if (!string.IsNullOrEmpty(fGlsl))
            File.WriteAllText(Path.Combine(targetDir, "fragment.glsl"), fGlsl);

        // HLSL9
        if (!string.IsNullOrEmpty(sh.HLSL9_Vertex?.Content))
            File.WriteAllText(Path.Combine(targetDir, "vertex.hlsl"), sh.HLSL9_Vertex.Content);
        if (!string.IsNullOrEmpty(sh.HLSL9_Fragment?.Content))
            File.WriteAllText(Path.Combine(targetDir, "pixel.hlsl"), sh.HLSL9_Fragment.Content);

        // HLSL11 binary
        if (sh.HLSL11_VertexData?.Data != null && !sh.HLSL11_VertexData.IsNull)
            File.WriteAllBytes(Path.Combine(targetDir, "vertex.cso"), sh.HLSL11_VertexData.Data);
        if (sh.HLSL11_PixelData?.Data != null && !sh.HLSL11_PixelData.IsNull)
            File.WriteAllBytes(Path.Combine(targetDir, "pixel.cso"), sh.HLSL11_PixelData.Data);

        // Metadata
        var meta = new
        {
            type = sh.Type.ToString(),
            version = sh.Version,
            attributes = sh.VertexShaderAttributes?.Select(a => a.Name?.Content ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>()
        };

        string metaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(targetDir, "shader.json"), metaJson);

        exportedCount++;
        IncrementProgress();
    }
});

await StopProgressBarUpdater();
HideProgressBar();

ScriptMessage($"Successfully exported {exportedCount} shader(s) to:\n{exportFolder}");

public class ShaderPickerItem
{
    public bool IsSelected { get; set; }
    public UndertaleShader Shader { get; set; }
    public string Name => Shader.Name?.Content ?? "<unnamed>";
}

public class ShaderPickerWindow : Window
{
    public List<UndertaleShader> SelectedShaders { get; } = new();
    private readonly List<ShaderPickerItem> _items;
    private readonly ListBox _listBox;
    private readonly TextBox _searchBox;

    public ShaderPickerWindow(List<UndertaleShader> shaders)
    {
        Title = "Select Shaders to Export";
        Width = 450;
        Height = 550;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _items = shaders.Select(s => new ShaderPickerItem { IsSelected = false, Shader = s }).ToList();

        var rootGrid = new Grid { Margin = new Thickness(10) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _searchBox = new TextBox { Margin = new Thickness(0, 0, 0, 8), FontSize = 14 };
        _searchBox.TextChanged += (s, e) => FilterItems();
        Grid.SetRow(_searchBox, 0);
        rootGrid.Children.Add(_searchBox);

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
            SelectedShaders.Clear();
            SelectedShaders.AddRange(_items.Where(x => x.IsSelected).Select(x => x.Shader));
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

