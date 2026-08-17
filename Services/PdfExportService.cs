using System;
using System.Collections.Generic;
using System.IO;
using DocScanner.Models;
using OpenCvSharp;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace DocScanner.Services;

/// <summary>
/// Compiles scanned document pages into clean, compressed, multi-page PDF files.
/// </summary>
public class PdfExportService : IPdfExportService
{
    public void ExportToPdf(
        IEnumerable<ScannedPage> pages,
        string destinationPdfPath,
        IImageProcessingService imageProcessingService,
        int quality = 90,
        bool fitToA4 = true)
    {
        if (pages == null) throw new ArgumentNullException(nameof(pages));
        if (string.IsNullOrWhiteSpace(destinationPdfPath)) throw new ArgumentException("Destination path is empty.", nameof(destinationPdfPath));

        using PdfDocument document = new PdfDocument();
        document.Info.Title = Path.GetFileNameWithoutExtension(destinationPdfPath);
        document.Info.Creator = "DocScanner Desktop (.NET 8)";

        // Standard A4 dimensions in points (72 DPI standard: 595.28 x 841.89 points)
        const double a4Width = 595.28;
        const double a4Height = 841.89;

        foreach (ScannedPage page in pages)
        {
            if (page.RawImage == null || page.RawImage.Empty()) continue;

            // Generate full-resolution enhanced Mat
            using Mat rotated = imageProcessingService.RotateImage(page.RawImage, page.RotationAngle);
            using Mat warped = imageProcessingService.WarpPerspective(rotated, page.Corners);
            using Mat enhanced = imageProcessingService.EnhanceDocument(warped, page.FilterType, page.FilterParameters);

            // Encode to high-quality JPEG byte array in-memory
            var encParams = new[] { new ImageEncodingParam(ImwriteFlags.JpegQuality, quality) };
            Cv2.ImEncode(".jpg", enhanced, out byte[] jpegBytes, encParams);

            PdfPage pdfPage = document.AddPage();

            if (fitToA4)
            {
                pdfPage.Width = XUnit.FromPoint(a4Width);
                pdfPage.Height = XUnit.FromPoint(a4Height);

                using var xgr = XGraphics.FromPdfPage(pdfPage);
                using var xImage = XImage.FromStream(() => new MemoryStream(jpegBytes));

                // Maintain aspect ratio centered on A4 page with margin
                double margin = 20.0;
                double availW = a4Width - (margin * 2);
                double availH = a4Height - (margin * 2);

                double imgAspect = (double)enhanced.Width / enhanced.Height;
                double pageAspect = availW / availH;

                double drawW, drawH, drawX, drawY;

                if (imgAspect > pageAspect)
                {
                    drawW = availW;
                    drawH = availW / imgAspect;
                    drawX = margin;
                    drawY = margin + (availH - drawH) / 2.0;
                }
                else
                {
                    drawH = availH;
                    drawW = availH * imgAspect;
                    drawX = margin + (availW - drawW) / 2.0;
                    drawY = margin;
                }

                xgr.DrawImage(xImage, drawX, drawY, drawW, drawH);
            }
            else
            {
                // Exact pixel-to-point aspect ratio match (72 points = 1 inch @ 150-300 dpi scaling)
                double scaleFactor = 72.0 / 150.0; // 150 DPI point mapping
                double ptWidth = enhanced.Width * scaleFactor;
                double ptHeight = enhanced.Height * scaleFactor;

                pdfPage.Width = XUnit.FromPoint(ptWidth);
                pdfPage.Height = XUnit.FromPoint(ptHeight);

                using var xgr = XGraphics.FromPdfPage(pdfPage);
                using var xImage = XImage.FromStream(() => new MemoryStream(jpegBytes));
                xgr.DrawImage(xImage, 0, 0, ptWidth, ptHeight);
            }
        }

        document.Save(destinationPdfPath);
    }
}
