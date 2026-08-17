using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocScanner.Models;
using DocScanner.Services;
using Microsoft.Win32;
using OpenCvSharp;

namespace DocScanner.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IImageProcessingService _imageService;
    private readonly IPdfExportService _pdfService;
    private CancellationTokenSource? _processingCts;

    [ObservableProperty]
    private ObservableCollection<ScannedPage> _pages = new();

    [ObservableProperty]
    private ScannedPage? _selectedPage;

    [ObservableProperty]
    private bool _isCropMode = false;

    [ObservableProperty]
    private bool _isSplitView = false;

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private string _statusText = "Ready. Import images or paste from clipboard to begin.";

    [ObservableProperty]
    private string _detailsText = string.Empty;

    // Filter Sliders
    [ObservableProperty]
    private double _brightness = 0;

    [ObservableProperty]
    private double _contrast = 2.0;

    [ObservableProperty]
    private double _shadowSuppression = 1.0;

    [ObservableProperty]
    private double _sharpening = 2.0;

    [ObservableProperty]
    private int _thresholdOffset = 0;

    [ObservableProperty]
    private int _denoiseStrength = 1;

    [ObservableProperty]
    private EnhancementFilterType _activeFilterType = EnhancementFilterType.EnhancedColor;

    [ObservableProperty]
    private bool _isSuccessDialogOpen;

    [ObservableProperty]
    private string _successDialogTitle = string.Empty;

    [ObservableProperty]
    private string _successDialogMessage = string.Empty;

    [ObservableProperty]
    private string _lastExportedFilePath = string.Empty;

    public bool HasPages => Pages.Count > 0;
    public bool HasSelectedPage => SelectedPage != null;

    public string ActivePresetTitle => ActiveFilterType switch
    {
        EnhancementFilterType.EnhancedColor => "FINE-TUNING (ENHANCED COLOR)",
        EnhancementFilterType.BlackAndWhite => "FINE-TUNING (B&W CLEAN DOCUMENT)",
        EnhancementFilterType.GrayscaleHighContrast => "FINE-TUNING (GRAYSCALE HIGH-CONTRAST)",
        EnhancementFilterType.SharpenOnly => "FINE-TUNING (DETAIL SHARPENING)",
        EnhancementFilterType.Original => "FINE-TUNING (ORIGINAL UNENHANCED)",
        EnhancementFilterType.Custom => "FINE-TUNING (CUSTOM ADJUSTMENTS)",
        _ => "FINE-TUNING CONTROLS"
    };

    public bool ShowShadowSlider => ActiveFilterType is EnhancementFilterType.EnhancedColor 
        or EnhancementFilterType.BlackAndWhite 
        or EnhancementFilterType.GrayscaleHighContrast 
        or EnhancementFilterType.Custom;

    public bool ShowSharpeningSlider => ActiveFilterType is EnhancementFilterType.EnhancedColor 
        or EnhancementFilterType.GrayscaleHighContrast 
        or EnhancementFilterType.SharpenOnly 
        or EnhancementFilterType.Custom;

    public bool ShowThresholdSlider => ActiveFilterType is EnhancementFilterType.BlackAndWhite 
        or EnhancementFilterType.Custom;

    public bool ShowDenoiseSlider => ActiveFilterType is EnhancementFilterType.BlackAndWhite 
        or EnhancementFilterType.GrayscaleHighContrast 
        or EnhancementFilterType.Custom;

    public bool ShowContrastSlider => true;
    public bool ShowBrightnessSlider => true;

    public MainViewModel(IImageProcessingService imageService, IPdfExportService pdfService)
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        _pdfService = pdfService ?? throw new ArgumentNullException(nameof(pdfService));

        Pages.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasPages));
            OnPropertyChanged(nameof(HasSelectedPage));
        };
    }

    public MainViewModel() : this(new ImageProcessingService(), new PdfExportService())
    {
    }

    partial void OnSelectedPageChanged(ScannedPage? value)
    {
        OnPropertyChanged(nameof(HasSelectedPage));

        if (value == null)
        {
            DetailsText = string.Empty;
            return;
        }

        _activeFilterType = value.FilterType;
        OnPropertyChanged(nameof(ActiveFilterType));
        SyncFilterParametersToUI(value.FilterParameters);

        TriggerPageReprocess();
    }

    private void SyncFilterParametersToUI(FilterParameters p)
    {
        Brightness = p.Brightness;
        Contrast = p.Contrast;
        ShadowSuppression = p.ShadowSuppression;
        Sharpening = p.Sharpening;
        ThresholdOffset = p.BinarizationThresholdOffset;
        DenoiseStrength = p.DenoiseStrength;

        OnPropertyChanged(nameof(ActivePresetTitle));
        OnPropertyChanged(nameof(ShowShadowSlider));
        OnPropertyChanged(nameof(ShowSharpeningSlider));
        OnPropertyChanged(nameof(ShowThresholdSlider));
        OnPropertyChanged(nameof(ShowDenoiseSlider));
    }

    private void Dispatch(Action action)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    [RelayCommand]
    private async Task AddImagesAsync()
    {
        OpenFileDialog openDialog = new OpenFileDialog
        {
            Title = "Select Document Photos or Scans",
            Filter = "All Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.tif;*.tiff|JPEG Files|*.jpg;*.jpeg|PNG Files|*.png|WebP Files|*.webp",
            Multiselect = true
        };

        if (openDialog.ShowDialog() == true && openDialog.FileNames.Length > 0)
        {
            await LoadFilesAsync(openDialog.FileNames);
        }
    }

    public async Task LoadFilesAsync(string[] filePaths)
    {
        if (filePaths == null || filePaths.Length == 0) return;

        IsBusy = true;
        StatusText = $"Loading {filePaths.Length} image(s)...";

        try
        {
            foreach (string file in filePaths)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        Mat raw = _imageService.LoadImage(file);
                        DocumentCorners corners = _imageService.DetectDocumentCorners(raw);
                        BitmapSource sourceBmp = _imageService.MatToBitmapSource(raw);

                        ScannedPage page = new ScannedPage
                        {
                            SourceFilePath = file,
                            RawImage = raw,
                            Corners = corners,
                            SourceBitmap = sourceBmp,
                            FilterType = EnhancementFilterType.EnhancedColor,
                            PageNumber = Pages.Count + 1,
                            DimensionsText = $"{raw.Width} x {raw.Height} px"
                        };

                        // Process initial warped and enhanced preview
                        using Mat warped = _imageService.WarpPerspective(raw, corners);
                        using Mat enhanced = _imageService.EnhanceDocument(warped, page.FilterType, page.FilterParameters);

                        page.ProcessedBitmap = _imageService.MatToBitmapSource(enhanced);
                        page.ThumbnailBitmap = _imageService.CreateThumbnail(enhanced);

                        Dispatch(() =>
                        {
                            Pages.Add(page);
                            if (SelectedPage == null)
                            {
                                SelectedPage = page;
                            }
                            OnPropertyChanged(nameof(HasPages));
                            OnPropertyChanged(nameof(HasSelectedPage));
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to load file {file}: {ex.Message}");
                    }
                });
            }

            Dispatch(() =>
            {
                RenumberPages();
                if (SelectedPage == null && Pages.Count > 0)
                {
                    SelectedPage = Pages[0];
                }
                IsCropMode = true;
                OnPropertyChanged(nameof(HasPages));
                OnPropertyChanged(nameof(HasSelectedPage));
            });

            StatusText = $"Loaded {Pages.Count} page(s).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddFromClipboardAsync()
    {
        if (!Clipboard.ContainsImage())
        {
            StatusText = "No image found on clipboard.";
            return;
        }

        BitmapSource? clipBmp = Clipboard.GetImage();
        if (clipBmp == null) return;

        IsBusy = true;
        StatusText = "Importing image from clipboard...";

        try
        {
            await Task.Run(() =>
            {
                using MemoryStream ms = new MemoryStream();
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(clipBmp));
                encoder.Save(ms);
                byte[] bytes = ms.ToArray();

                Mat raw = _imageService.LoadImageFromBytes(bytes);
                DocumentCorners corners = _imageService.DetectDocumentCorners(raw);
                BitmapSource sourceBmp = _imageService.MatToBitmapSource(raw);

                ScannedPage page = new ScannedPage
                {
                    RawImage = raw,
                    Corners = corners,
                    SourceBitmap = sourceBmp,
                    FilterType = EnhancementFilterType.EnhancedColor,
                    PageNumber = Pages.Count + 1,
                    DimensionsText = $"{raw.Width} x {raw.Height} px"
                };

                using Mat warped = _imageService.WarpPerspective(raw, corners);
                using Mat enhanced = _imageService.EnhanceDocument(warped, page.FilterType, page.FilterParameters);

                page.ProcessedBitmap = _imageService.MatToBitmapSource(enhanced);
                page.ThumbnailBitmap = _imageService.CreateThumbnail(enhanced);

                Dispatch(() =>
                {
                    Pages.Add(page);
                    SelectedPage = page;
                    OnPropertyChanged(nameof(HasPages));
                    OnPropertyChanged(nameof(HasSelectedPage));
                });
            });

            Dispatch(() =>
            {
                RenumberPages();
                IsCropMode = true;
                OnPropertyChanged(nameof(HasPages));
                OnPropertyChanged(nameof(HasSelectedPage));
            });

            StatusText = "Image imported from clipboard.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DeletePage()
    {
        if (SelectedPage == null) return;

        int index = Pages.IndexOf(SelectedPage);
        ScannedPage toRemove = SelectedPage;

        Pages.Remove(toRemove);
        toRemove.Dispose();

        if (Pages.Count > 0)
        {
            int nextIndex = Math.Clamp(index, 0, Pages.Count - 1);
            SelectedPage = Pages[nextIndex];
        }
        else
        {
            SelectedPage = null;
        }

        RenumberPages();
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(HasSelectedPage));
        StatusText = "Page removed.";
    }

    [RelayCommand]
    private void MovePageUp()
    {
        if (SelectedPage == null) return;
        int idx = Pages.IndexOf(SelectedPage);
        if (idx > 0)
        {
            Pages.Move(idx, idx - 1);
            RenumberPages();
        }
    }

    [RelayCommand]
    private void MovePageDown()
    {
        if (SelectedPage == null) return;
        int idx = Pages.IndexOf(SelectedPage);
        if (idx >= 0 && idx < Pages.Count - 1)
        {
            Pages.Move(idx, idx + 1);
            RenumberPages();
        }
    }

    [RelayCommand]
    private void AutoDetectCorners()
    {
        if (SelectedPage?.RawImage == null) return;

        using Mat rotated = _imageService.RotateImage(SelectedPage.RawImage, SelectedPage.RotationAngle);
        SelectedPage.Corners = _imageService.DetectDocumentCorners(rotated);
        TriggerPageReprocess();
        StatusText = "Document boundaries auto-detected.";
    }

    [RelayCommand]
    private void ResetCrop()
    {
        if (SelectedPage?.RawImage == null) return;

        using Mat rotated = _imageService.RotateImage(SelectedPage.RawImage, SelectedPage.RotationAngle);
        SelectedPage.Corners = new DocumentCorners(rotated.Width, rotated.Height);
        TriggerPageReprocess();
        StatusText = "Crop boundary reset to full image.";
    }

    [RelayCommand]
    private void RotateLeft()
    {
        if (SelectedPage?.RawImage == null) return;

        SelectedPage.RotationAngle = (SelectedPage.RotationAngle - 90 + 360) % 360;
        using Mat rotated = _imageService.RotateImage(SelectedPage.RawImage, SelectedPage.RotationAngle);
        SelectedPage.SourceBitmap = _imageService.MatToBitmapSource(rotated);
        SelectedPage.Corners = _imageService.DetectDocumentCorners(rotated);

        TriggerPageReprocess();
        StatusText = $"Rotated to {SelectedPage.RotationAngle}°";
    }

    [RelayCommand]
    private void RotateRight()
    {
        if (SelectedPage?.RawImage == null) return;

        SelectedPage.RotationAngle = (SelectedPage.RotationAngle + 90) % 360;
        using Mat rotated = _imageService.RotateImage(SelectedPage.RawImage, SelectedPage.RotationAngle);
        SelectedPage.SourceBitmap = _imageService.MatToBitmapSource(rotated);
        SelectedPage.Corners = _imageService.DetectDocumentCorners(rotated);

        TriggerPageReprocess();
        StatusText = $"Rotated to {SelectedPage.RotationAngle}°";
    }

    [RelayCommand]
    private void SetFilter(string filterName)
    {
        if (!Enum.TryParse(filterName, true, out EnhancementFilterType type)) return;

        ActiveFilterType = type;
        if (SelectedPage != null)
        {
            SelectedPage.FilterType = type;
            SyncFilterParametersToUI(SelectedPage.FilterParameters);
            TriggerPageReprocess();
        }
        else
        {
            SyncFilterParametersToUI(FilterParameters.CreateDefault(type));
        }
    }

    [RelayCommand]
    private void ResetActivePreset()
    {
        if (SelectedPage == null) return;

        SelectedPage.ResetFilterParametersToDefault(ActiveFilterType);
        SyncFilterParametersToUI(SelectedPage.FilterParameters);
        TriggerPageReprocess();
        StatusText = $"Reset {ActiveFilterType} fine-tuning to default values.";
    }

    [RelayCommand]
    private void ToggleCropMode()
    {
        IsCropMode = !IsCropMode;
        if (!IsCropMode)
        {
            TriggerPageReprocess();
        }
    }

    [RelayCommand]
    private void ToggleSplitView()
    {
        IsSplitView = !IsSplitView;
    }

    public void OnSliderChanged()
    {
        if (SelectedPage == null) return;

        SelectedPage.FilterParameters.Brightness = Brightness;
        SelectedPage.FilterParameters.Contrast = Contrast;
        SelectedPage.FilterParameters.ShadowSuppression = ShadowSuppression;
        SelectedPage.FilterParameters.Sharpening = Sharpening;
        SelectedPage.FilterParameters.BinarizationThresholdOffset = ThresholdOffset;
        SelectedPage.FilterParameters.DenoiseStrength = DenoiseStrength;

        TriggerPageReprocess();
    }

    public void TriggerPageReprocess()
    {
        if (SelectedPage?.RawImage == null) return;

        _processingCts?.Cancel();
        _processingCts = new CancellationTokenSource();
        CancellationToken ct = _processingCts.Token;

        ScannedPage page = SelectedPage;
        Mat raw = page.RawImage.Clone();
        int rot = page.RotationAngle;
        DocumentCorners corners = page.Corners.Clone();
        EnhancementFilterType filter = page.FilterType;
        FilterParameters parameters = page.FilterParameters.Clone();

        Stopwatch sw = Stopwatch.StartNew();

        Task.Run(() =>
        {
            try
            {
                if (ct.IsCancellationRequested) { raw.Dispose(); return; }

                using Mat rotated = _imageService.RotateImage(raw, rot);
                using Mat warped = _imageService.WarpPerspective(rotated, corners);

                if (ct.IsCancellationRequested) { raw.Dispose(); return; }

                using Mat enhanced = _imageService.EnhanceDocument(warped, filter, parameters);

                if (ct.IsCancellationRequested) { raw.Dispose(); return; }

                BitmapSource procBmp = _imageService.MatToBitmapSource(enhanced);
                BitmapSource thumbBmp = _imageService.CreateThumbnail(enhanced);

                sw.Stop();
                long elapsedMs = sw.ElapsedMilliseconds;

                Dispatch(() =>
                {
                    if (SelectedPage == page)
                    {
                        page.ProcessedBitmap = procBmp;
                        page.ThumbnailBitmap = thumbBmp;
                        page.DimensionsText = $"{enhanced.Width} x {enhanced.Height} px";
                        DetailsText = $"Resolution: {enhanced.Width} x {enhanced.Height} px | Process Time: {elapsedMs} ms";
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Processing error: {ex.Message}");
            }
            finally
            {
                raw.Dispose();
            }
        }, ct);
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (Pages.Count == 0)
        {
            StatusText = "No document pages available to export.";
            return;
        }

        SaveFileDialog saveDialog = new SaveFileDialog
        {
            Title = "Export Document to PDF",
            Filter = "PDF Document (*.pdf)|*.pdf",
            FileName = $"Scanned_Document_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
        };

        if (saveDialog.ShowDialog() == true)
        {
            IsBusy = true;
            StatusText = "Compiling PDF document...";

            try
            {
                string path = saveDialog.FileName;
                var pagesSnapshot = Pages.ToList();

                await Task.Run(() =>
                {
                    _pdfService.ExportToPdf(pagesSnapshot, path, _imageService, quality: 92, fitToA4: true);
                });

                StatusText = $"PDF successfully exported to: {Path.GetFileName(path)}";
                LastExportedFilePath = path;
                SuccessDialogTitle = "PDF Document Exported";
                SuccessDialogMessage = $"Your document has been compiled and saved successfully:\n\n{path}";
                IsSuccessDialogOpen = true;
            }
            catch (Exception ex)
            {
                StatusText = $"Export error: {ex.Message}";
                MessageBox.Show($"Failed to export PDF: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task ExportCurrentPageImageAsync()
    {
        if (SelectedPage?.RawImage == null)
        {
            StatusText = "No page selected for export.";
            return;
        }

        SaveFileDialog saveDialog = new SaveFileDialog
        {
            Title = "Export Current Page Image",
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|TIFF Image (*.tif)|*.tif|WebP Image (*.webp)|*.webp",
            FileName = $"Page_{SelectedPage.PageNumber:D2}_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (saveDialog.ShowDialog() == true)
        {
            IsBusy = true;
            StatusText = "Exporting page image...";

            try
            {
                string path = saveDialog.FileName;
                ScannedPage page = SelectedPage;
                Mat raw = page.RawImage.Clone();
                int rot = page.RotationAngle;
                DocumentCorners corners = page.Corners.Clone();
                EnhancementFilterType filter = page.FilterType;
                FilterParameters parameters = page.FilterParameters.Clone();

                await Task.Run(() =>
                {
                    try
                    {
                        using Mat rotated = _imageService.RotateImage(raw, rot);
                        using Mat warped = _imageService.WarpPerspective(rotated, corners);
                        using Mat enhanced = _imageService.EnhanceDocument(warped, filter, parameters);

                        _imageService.SaveImage(enhanced, path);
                    }
                    finally
                    {
                        raw.Dispose();
                    }
                });

                StatusText = $"Image exported to: {Path.GetFileName(path)}";
                LastExportedFilePath = path;
                SuccessDialogTitle = "Image Exported";
                SuccessDialogMessage = $"Your document page image has been saved successfully:\n\n{path}";
                IsSuccessDialogOpen = true;
            }
            catch (Exception ex)
            {
                StatusText = $"Export error: {ex.Message}";
                MessageBox.Show($"Failed to export image: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task BatchExportAllImagesAsync()
    {
        if (Pages.Count == 0) return;

        OpenFolderDialog folderDialog = new OpenFolderDialog
        {
            Title = "Select Destination Folder for Batch Export"
        };

        if (folderDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(folderDialog.FolderName))
        {
            string outDir = folderDialog.FolderName;
            IsBusy = true;
            StatusText = $"Exporting {Pages.Count} pages to folder...";

            try
            {
                var pagesSnapshot = Pages.ToList();

                await Task.Run(() =>
                {
                    int i = 1;
                    foreach (ScannedPage page in pagesSnapshot)
                    {
                        if (page.RawImage == null) continue;

                        using Mat rotated = _imageService.RotateImage(page.RawImage, page.RotationAngle);
                        using Mat warped = _imageService.WarpPerspective(rotated, page.Corners);
                        using Mat enhanced = _imageService.EnhanceDocument(warped, page.FilterType, page.FilterParameters);

                        string filename = Path.Combine(outDir, $"Page_{i:D2}.png");
                        _imageService.SaveImage(enhanced, filename);
                        i++;
                    }
                });

                StatusText = $"Successfully exported {Pages.Count} pages to {Path.GetFileName(outDir)}.";
                LastExportedFilePath = outDir;
                SuccessDialogTitle = "Batch Export Completed";
                SuccessDialogMessage = $"Successfully exported {pagesSnapshot.Count} document pages to the directory:\n\n{outDir}";
                IsSuccessDialogOpen = true;
            }
            catch (Exception ex)
            {
                StatusText = $"Batch export error: {ex.Message}";
                MessageBox.Show($"Failed to batch export: {ex.Message}", "Batch Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private void CloseSuccessDialog()
    {
        IsSuccessDialogOpen = false;
    }

    [RelayCommand]
    private void OpenExportedFile()
    {
        if (!string.IsNullOrWhiteSpace(LastExportedFilePath) && File.Exists(LastExportedFilePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = LastExportedFilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open file: {ex.Message}");
            }
        }
        IsSuccessDialogOpen = false;
    }

    [RelayCommand]
    private void OpenExportedFolder()
    {
        if (!string.IsNullOrWhiteSpace(LastExportedFilePath))
        {
            try
            {
                string target = File.Exists(LastExportedFilePath) ? LastExportedFilePath : (Directory.Exists(LastExportedFilePath) ? LastExportedFilePath : string.Empty);
                if (!string.IsNullOrWhiteSpace(target))
                {
                    if (File.Exists(target))
                    {
                        Process.Start("explorer.exe", $"/select,\"{target}\"");
                    }
                    else
                    {
                        Process.Start("explorer.exe", $"\"{target}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open folder: {ex.Message}");
            }
        }
        IsSuccessDialogOpen = false;
    }

    private void RenumberPages()
    {
        for (int i = 0; i < Pages.Count; i++)
        {
            Pages[i].PageNumber = i + 1;
        }
    }
}
