namespace DocScanner.Models;

/// <summary>
/// Numerical parameters for fine-tuning document enhancement filters.
/// </summary>
public class FilterParameters
{
    /// <summary>
    /// Brightness adjustment offset (-80 to 80).
    /// </summary>
    public double Brightness { get; set; } = 0.0;

    /// <summary>
    /// Contrast multiplier (0.5 to 3.5x).
    /// </summary>
    public double Contrast { get; set; } = 2.0;

    /// <summary>
    /// Intensity of illumination normalization and shadow removal (0.0 to 1.5, where 1.0 = 100%).
    /// </summary>
    public double ShadowSuppression { get; set; } = 1.0;

    /// <summary>
    /// High-pass unsharp mask sharpness intensity (0.0 to 4.0).
    /// </summary>
    public double Sharpening { get; set; } = 2.0;

    /// <summary>
    /// Binarization threshold tuning offset for Black & White filter (-30 to 30).
    /// </summary>
    public int BinarizationThresholdOffset { get; set; } = 0;

    /// <summary>
    /// Morphological noise reduction strength (0 to 4).
    /// </summary>
    public int DenoiseStrength { get; set; } = 1;

    /// <summary>
    /// Factory creating distinct, optimized default parameters tailored for each specific preset.
    /// </summary>
    public static FilterParameters CreateDefault(EnhancementFilterType type)
    {
        return type switch
        {
            EnhancementFilterType.Original => new FilterParameters
            {
                Brightness = 0.0,
                Contrast = 1.0,
                ShadowSuppression = 0.0,
                Sharpening = 0.0,
                BinarizationThresholdOffset = 0,
                DenoiseStrength = 0
            },
            EnhancementFilterType.EnhancedColor => new FilterParameters
            {
                Brightness = 0.0,
                Contrast = 2.0,
                ShadowSuppression = 1.0,
                Sharpening = 2.0,
                BinarizationThresholdOffset = 0,
                DenoiseStrength = 1
            },
            EnhancementFilterType.BlackAndWhite => new FilterParameters
            {
                Brightness = 0.0,
                Contrast = 1.5,
                ShadowSuppression = 1.0,
                Sharpening = 1.0,
                BinarizationThresholdOffset = 0,
                DenoiseStrength = 1
            },
            EnhancementFilterType.GrayscaleHighContrast => new FilterParameters
            {
                Brightness = 0.0,
                Contrast = 1.8,
                ShadowSuppression = 1.0,
                Sharpening = 1.2,
                BinarizationThresholdOffset = 0,
                DenoiseStrength = 1
            },
            EnhancementFilterType.SharpenOnly => new FilterParameters
            {
                Brightness = 0.0,
                Contrast = 1.0,
                ShadowSuppression = 0.0,
                Sharpening = 2.5,
                BinarizationThresholdOffset = 0,
                DenoiseStrength = 0
            },
            EnhancementFilterType.Custom => new FilterParameters
            {
                Brightness = 0.0,
                Contrast = 1.5,
                ShadowSuppression = 0.8,
                Sharpening = 1.5,
                BinarizationThresholdOffset = 0,
                DenoiseStrength = 1
            },
            _ => new FilterParameters()
        };
    }

    /// <summary>
    /// Creates a deep clone of this parameter set.
    /// </summary>
    public FilterParameters Clone()
    {
        return new FilterParameters
        {
            Brightness = this.Brightness,
            Contrast = this.Contrast,
            ShadowSuppression = this.ShadowSuppression,
            Sharpening = this.Sharpening,
            BinarizationThresholdOffset = this.BinarizationThresholdOffset,
            DenoiseStrength = this.DenoiseStrength
        };
    }
}
