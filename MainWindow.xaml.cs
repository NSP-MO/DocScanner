using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using DocScanner.ViewModels;

namespace DocScanner;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[]? files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                string[] validExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff" };
                string[] imageFiles = files
                    .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToArray();

                if (imageFiles.Length > 0 && DataContext is MainViewModel vm)
                {
                    await vm.LoadFilesAsync(imageFiles);
                }
            }
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            vm.AddFromClipboardCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            vm.DeletePageCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void CropCanvas_CornersManipulated(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.TriggerPageReprocess();
        }
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is MainViewModel vm && IsLoaded)
        {
            vm.OnSliderChanged();
        }
    }
}