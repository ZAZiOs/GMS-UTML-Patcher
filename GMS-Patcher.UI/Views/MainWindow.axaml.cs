using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GMS_Patcher.UI.Services;
using GMS_Patcher.UI.ViewModels;

namespace GMS_Patcher.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

#if DEBUG
        this.AttachDevTools();
#endif

        var dropBorder = this.FindControl<Border>("DropZoneBorder");
        if (dropBorder != null)
        {
            dropBorder.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
            dropBorder.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            dropBorder.AddHandler(DragDrop.DropEvent, OnDrop);
        }

        var logsTextBox = this.FindControl<TextBox>("LogsTextBox");
        if (logsTextBox != null)
        {
            logsTextBox.PropertyChanged += (sender, e) =>
            {
                if (e.Property == TextBox.TextProperty || e.Property == Visual.IsVisibleProperty)
                {
                    if (logsTextBox.IsVisible && logsTextBox.Text != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            logsTextBox.CaretIndex = logsTextBox.Text?.Length ?? 0;
                            var sv = logsTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                            sv?.ScrollToEnd();
                        }, DispatcherPriority.Loaded);
                    }
                }
            };
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = Avalonia.Media.Brushes.GhostWhite;
            border.BorderBrush = Avalonia.Media.Brushes.CornflowerBlue;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = Avalonia.Media.Brushes.White;
            border.BorderBrush = Avalonia.Media.Brushes.DarkGray;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = Avalonia.Media.Brushes.White;
            border.BorderBrush = Avalonia.Media.Brushes.DarkGray;
        }

        var files = e.Data.GetFiles();
        if (files != null)
        {
            var firstItem = files.FirstOrDefault();
            if (firstItem != null)
            {
                string localPath = firstItem.Path.LocalPath;
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.HandleDroppedFile(localPath);
                }
            }
        }
    }

    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку с игрой DELTARUNE",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            string selectedPath = folders[0].Path.LocalPath;
            if (DataContext is MainWindowViewModel vm)
            {
                vm.PathFieldText = selectedPath;
            }
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnFinishClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.OnFinishClicked(Close);
        }
        else
        {
            Close();
        }
    }
}

