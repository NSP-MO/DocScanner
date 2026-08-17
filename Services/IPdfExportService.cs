using System.Collections.Generic;
using DocScanner.Models;

namespace DocScanner.Services;

/// <summary>
/// Service interface for compiling scanned document pages into PDF documents.
/// </summary>
public interface IPdfExportService
{
    /// <summary>
    /// Exports a collection of scanned pages to a multi-page PDF document.
    /// </summary>
    /// <param name="pages">List of scanned pages.</param>
    /// <param name="destinationPdfPath">Destination file path.</param>
    /// <param name="imageProcessingService">Image processing service for obtaining processed Mats.</param>
    /// <param name="quality">JPEG compression quality (1-100).</param>
    /// <param name="fitToA4">Whether to scale pages to standard A4 format or match image aspect ratio.</param>
    void ExportToPdf(
        IEnumerable<ScannedPage> pages,
        string destinationPdfPath,
        IImageProcessingService imageProcessingService,
        int quality = 90,
        bool fitToA4 = true);
}
