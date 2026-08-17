namespace DocScanner.Models;

/// <summary>
/// Numerical parameters for fine-tuning document enhancement filters.
/// </summary>
public class FilterParameters
{
    /// <summary>
    /// Brightness adjustment offset (-100 to 100).
    /// </summary>
    public double Brightness { get; set; } = 0.0;

    /// <summary>
    /// Contrast multiplier (0.5 to 3.5, default 2.0x).
    /// </summary>
    public double Contrast { get; set; } = 2.0;

    /// <summary>
    /// Intensity of illumination normalization and shadow removal (0.0 to 1.5, default 1.0 = 100%).
    /// </summary>
    public double ShadowSuppression { get; set; } = 1.0;

    /// <summary>
    /// High-pass unsharp mask sharpness intensity (0.0 to 4.0, default 2.0).
    /// </summary>
    public double Sharpening { get; set; } = 2.0;

    /// <summary>
    /// Binarization threshold tuning offset for Black & White filter (-50 to 50, default 0).
    /// </summary>
    public int BinarizationThresholdOffset { get; set; } = 0;

    /// <summary>
    /// Morphological noise reduction strength (0 to 5, default 1).
    /// </summary>
    public int DenoiseStrength { get; set; } = 1;

    /// <summary>
    /// Clones the parameter set.
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
