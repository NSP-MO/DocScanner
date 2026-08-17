namespace DocScanner.Models;

/// <summary>
/// Preset enhancement filter types for document processing.
/// </summary>
public enum EnhancementFilterType
{
    /// <summary>
    /// Original unenhanced document image.
    /// </summary>
    Original,

    /// <summary>
    /// Enhanced Color: Illumination normalization, shadow suppression, adaptive LAB contrast, and character edge sharpening.
    /// </summary>
    EnhancedColor,

    /// <summary>
    /// High-contrast binary document: Clean pure white background with deep black text.
    /// </summary>
    BlackAndWhite,

    /// <summary>
    /// Grayscale document with adaptive histogram equalization and edge sharpening.
    /// </summary>
    GrayscaleHighContrast,

    /// <summary>
    /// Detail sharpness enhancement without tonal alteration.
    /// </summary>
    SharpenOnly,

    /// <summary>
    /// Custom user-defined adjustments (manual brightness, contrast, shadow suppression, threshold).
    /// </summary>
    Custom
}
