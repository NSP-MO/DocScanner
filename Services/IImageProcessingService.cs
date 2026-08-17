using System.Windows.Media.Imaging;
using DocScanner.Models;
using OpenCvSharp;

namespace DocScanner.Services;

/// <summary>
/// Service interface for document computer vision, perspective correction, and contrast enhancement.
/// </summary>
public interface IImageProcessingService
{
    /// <summary>
    /// Loads an image file into an OpenCV Mat object.
    /// </summary>
    Mat LoadImage(string filePath);

    /// <summary>
    /// Loads an image from a byte array or memory stream.
    /// </summary>
    Mat LoadImageFromBytes(byte[] imageBytes);

    /// <summary>
    /// Automatically detects document boundary quadrilateral corners.
    /// </summary>
    DocumentCorners DetectDocumentCorners(Mat sourceImage);

    /// <summary>
    /// Applies 4-point perspective transformation to unwarp a quadrilateral document into a flat rectangle.
    /// </summary>
    Mat WarpPerspective(Mat sourceImage, DocumentCorners corners);

    /// <summary>
    /// Rotates an image by specified degrees (0, 90, 180, 270).
    /// </summary>
    Mat RotateImage(Mat sourceImage, int angleDegrees);

    /// <summary>
    /// Applies document enhancement filter pipeline based on preset and parameters.
    /// </summary>
    Mat EnhanceDocument(Mat warpedImage, EnhancementFilterType filterType, FilterParameters parameters);

    /// <summary>
    /// Converts an OpenCV Mat into a WPF BitmapSource for UI display.
    /// </summary>
    BitmapSource MatToBitmapSource(Mat mat);

    /// <summary>
    /// Creates a lightweight thumbnail BitmapSource from a Mat.
    /// </summary>
    BitmapSource CreateThumbnail(Mat mat, int maxDimension = 180);

    /// <summary>
    /// Saves a Mat to disk with specified format and quality.
    /// </summary>
    void SaveImage(Mat mat, string destinationPath, int quality = 95);
}
