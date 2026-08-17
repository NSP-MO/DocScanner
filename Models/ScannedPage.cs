using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace DocScanner.Models;

/// <summary>
/// Represents a single document page in a scanning and enhancement session.
/// </summary>
public class ScannedPage : INotifyPropertyChanged, IDisposable
{
    private int _pageNumber;
    private DocumentCorners _corners = new();
    private int _rotationAngle = 0;
    private EnhancementFilterType _filterType = EnhancementFilterType.EnhancedColor;
    private FilterParameters _filterParameters = new();
    private BitmapSource? _sourceBitmap;
    private BitmapSource? _processedBitmap;
    private BitmapSource? _thumbnailBitmap;
    private bool _isProcessing;
    private string _statusMessage = string.Empty;
    private string _dimensionsText = string.Empty;

    private readonly Dictionary<EnhancementFilterType, FilterParameters> _presetParameters = new()
    {
        [EnhancementFilterType.Original] = FilterParameters.CreateDefault(EnhancementFilterType.Original),
        [EnhancementFilterType.EnhancedColor] = FilterParameters.CreateDefault(EnhancementFilterType.EnhancedColor),
        [EnhancementFilterType.BlackAndWhite] = FilterParameters.CreateDefault(EnhancementFilterType.BlackAndWhite),
        [EnhancementFilterType.GrayscaleHighContrast] = FilterParameters.CreateDefault(EnhancementFilterType.GrayscaleHighContrast),
        [EnhancementFilterType.SharpenOnly] = FilterParameters.CreateDefault(EnhancementFilterType.SharpenOnly),
        [EnhancementFilterType.Custom] = FilterParameters.CreateDefault(EnhancementFilterType.Custom)
    };

    public Guid Id { get; } = Guid.NewGuid();
    public string? SourceFilePath { get; set; }

    /// <summary>
    /// Master uncropped raw image matrix in memory.
    /// </summary>
    public Mat? RawImage { get; set; }

    public int PageNumber
    {
        get => _pageNumber;
        set => SetField(ref _pageNumber, value);
    }

    public DocumentCorners Corners
    {
        get => _corners;
        set => SetField(ref _corners, value);
    }

    public int RotationAngle
    {
        get => _rotationAngle;
        set => SetField(ref _rotationAngle, value);
    }

    public EnhancementFilterType FilterType
    {
        get => _filterType;
        set
        {
            if (SetField(ref _filterType, value))
            {
                FilterParameters = GetParametersForFilter(value);
            }
        }
    }

    public FilterParameters FilterParameters
    {
        get => _filterParameters;
        set => SetField(ref _filterParameters, value);
    }

    /// <summary>
    /// Gets the dedicated fine-tuning parameter set for a given filter preset.
    /// </summary>
    public FilterParameters GetParametersForFilter(EnhancementFilterType type)
    {
        if (!_presetParameters.TryGetValue(type, out var p))
        {
            p = FilterParameters.CreateDefault(type);
            _presetParameters[type] = p;
        }
        return p;
    }

    /// <summary>
    /// Resets the parameters of the specified filter type back to its factory default.
    /// </summary>
    public void ResetFilterParametersToDefault(EnhancementFilterType type)
    {
        _presetParameters[type] = FilterParameters.CreateDefault(type);
        if (FilterType == type)
        {
            FilterParameters = _presetParameters[type];
        }
    }

    public int ImageWidth => RawImage?.Width ?? (SourceBitmap?.PixelWidth ?? 100);
    public int ImageHeight => RawImage?.Height ?? (SourceBitmap?.PixelHeight ?? 100);

    public BitmapSource? SourceBitmap
    {
        get => _sourceBitmap;
        set
        {
            if (SetField(ref _sourceBitmap, value))
            {
                OnPropertyChanged(nameof(ImageWidth));
                OnPropertyChanged(nameof(ImageHeight));
            }
        }
    }

    public BitmapSource? ProcessedBitmap
    {
        get => _processedBitmap;
        set => SetField(ref _processedBitmap, value);
    }

    public BitmapSource? ThumbnailBitmap
    {
        get => _thumbnailBitmap;
        set => SetField(ref _thumbnailBitmap, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetField(ref _isProcessing, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string DimensionsText
    {
        get => _dimensionsText;
        set => SetField(ref _dimensionsText, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public void Dispose()
    {
        RawImage?.Dispose();
        RawImage = null;
    }
}
