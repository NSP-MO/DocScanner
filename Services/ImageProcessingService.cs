using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using DocScanner.Models;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace DocScanner.Services;

/// <summary>
/// Digital image processing and computer vision service for document scanning.
/// Implements perspective correction, illumination normalization, and CamScanner-grade contrast filters.
/// </summary>
public class ImageProcessingService : IImageProcessingService
{
    public Mat LoadImage(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException($"Image file not found: {filePath}");
        }

        // Use ImDecode with File.ReadAllBytes to support Unicode paths on Windows safely
        byte[] bytes = File.ReadAllBytes(filePath);
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }

    public Mat LoadImageFromBytes(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image byte array is empty.", nameof(imageBytes));
        }

        return Cv2.ImDecode(imageBytes, ImreadModes.Color);
    }

    public DocumentCorners DetectDocumentCorners(Mat sourceImage)
    {
        if (sourceImage == null || sourceImage.Empty())
        {
            throw new ArgumentNullException(nameof(sourceImage));
        }

        int origWidth = sourceImage.Width;
        int origHeight = sourceImage.Height;

        // Scale down for rapid analysis
        double targetScale = Math.Min(1.0, 900.0 / Math.Max(origWidth, origHeight));
        int workingWidth = (int)Math.Round(origWidth * targetScale);
        int workingHeight = (int)Math.Round(origHeight * targetScale);

        using Mat resized = new Mat();
        if (targetScale < 0.999)
        {
            Cv2.Resize(sourceImage, resized, new OpenCvSharp.Size(workingWidth, workingHeight), interpolation: InterpolationFlags.Area);
        }
        else
        {
            sourceImage.CopyTo(resized);
        }

        using Mat gray = new Mat();
        Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

        // 1. Lightweight OCR / Text Saliency: Detect text and glyph regions
        List<Point2f> textPoints = ExtractTextRegionPoints(gray, targetScale, origWidth, origHeight, workingWidth, workingHeight);

        // 2. Extract Candidate Paper Quadrilaterals via Multi-Threshold Geometric Contours
        List<Point2f[]> candidateQuads = FindCandidatePaperQuads(gray, targetScale, workingWidth, workingHeight);

        // 3. Hybrid Fusion: Evaluate candidate paper quads against text envelope
        if (textPoints.Count >= 4)
        {
            Point2f[]? bestTextValidatedQuad = null;
            double bestScore = -1.0;

            foreach (Point2f[] quad in candidateQuads)
            {
                double containment = CalculatePointContainmentRatio(quad, textPoints);
                if (containment >= 0.80) // At least 80% of text blocks enclosed
                {
                    double quadArea = Math.Abs(Cv2.ContourArea(quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray()));
                    double totalArea = (double)origWidth * origHeight;
                    // Score favors higher containment with natural document size
                    double score = (containment * 1000.0) - Math.Abs((quadArea / totalArea) - 0.70) * 100.0;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTextValidatedQuad = quad;
                    }
                }
            }

            if (bestTextValidatedQuad != null)
            {
                return SortCornersClockwise(bestTextValidatedQuad);
            }

            // If no geometric contour cleanly contained text, compute envelope directly from text layout
            Point2f[]? textEnvelope = ComputeTextEnvelopeQuad(textPoints, origWidth, origHeight);
            if (textEnvelope != null && textEnvelope.Length == 4)
            {
                return SortCornersClockwise(textEnvelope);
            }
        }

        // 4. Fallback to best geometric quad if no text detected
        if (candidateQuads.Count > 0)
        {
            return SortCornersClockwise(candidateQuads[0]);
        }

        // 5. Default fallback: 4% inner border of source image
        float marginX = origWidth * 0.04f;
        float marginY = origHeight * 0.04f;
        return new DocumentCorners(
            new Point2f(marginX, marginY),
            new Point2f(origWidth - marginX, marginY),
            new Point2f(origWidth - marginX, origHeight - marginY),
            new Point2f(marginX, origHeight - marginY)
        );
    }

    /// <summary>
    /// Lightweight OCR / text saliency detector.
    /// Extracts character and line bounding vertices to accurately locate document content.
    /// </summary>
    private List<Point2f> ExtractTextRegionPoints(Mat gray, double targetScale, int origWidth, int origHeight, int workWidth, int workHeight)
    {
        List<Point2f> points = new List<Point2f>();

        // High-pass vertical gradient for character stroke detection
        using Mat gradX = new Mat();
        Cv2.Sobel(gray, gradX, MatType.CV_32F, 1, 0, 3);

        using Mat absGradX = new Mat();
        Cv2.ConvertScaleAbs(gradX, absGradX);

        using Mat blurred = new Mat();
        Cv2.GaussianBlur(absGradX, blurred, new OpenCvSharp.Size(5, 5), 0);

        using Mat thresh = new Mat();
        Cv2.Threshold(blurred, thresh, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        // Horizontal structuring element to bridge words into cohesive text lines
        int kernelW = Math.Max(12, (int)(workWidth * 0.035));
        using Mat lineKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(kernelW, 3));
        using Mat connected = new Mat();
        Cv2.MorphologyEx(thresh, connected, MorphTypes.Close, lineKernel);

        Cv2.FindContours(connected, out Point[][] textContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        double minArea = (workWidth * workHeight) * 0.0001;
        double maxArea = (workWidth * workHeight) * 0.40;
        double invScale = 1.0 / targetScale;

        foreach (Point[] cnt in textContours)
        {
            double area = Cv2.ContourArea(cnt);
            if (area < minArea || area > maxArea) continue;

            OpenCvSharp.Rect bRect = Cv2.BoundingRect(cnt);
            double aspect = (double)bRect.Width / Math.Max(1, bRect.Height);

            if (aspect > 0.6 && bRect.Height < workHeight * 0.28 && bRect.Width >= 8)
            {
                float x1 = (float)(bRect.X * invScale);
                float y1 = (float)(bRect.Y * invScale);
                float x2 = (float)((bRect.X + bRect.Width) * invScale);
                float y2 = (float)((bRect.Y + bRect.Height) * invScale);

                points.Add(new Point2f(x1, y1));
                points.Add(new Point2f(x2, y1));
                points.Add(new Point2f(x2, y2));
                points.Add(new Point2f(x1, y2));
            }
        }

        return points;
    }

    /// <summary>
    /// Multi-threshold candidate paper quadrilateral extractor.
    /// </summary>
    private List<Point2f[]> FindCandidatePaperQuads(Mat gray, double targetScale, int workWidth, int workHeight)
    {
        List<Point2f[]> quads = new List<Point2f[]>();
        double totalArea = workWidth * workHeight;
        double minDocArea = totalArea * 0.10;
        double invScale = 1.0 / targetScale;

        int[][] thresholds = new[]
        {
            new[] { 35, 120 },
            new[] { 50, 160 },
            new[] { 20, 80 }
        };

        using Mat blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(5, 5), 0);

        foreach (int[] th in thresholds)
        {
            using Mat edges = new Mat();
            Cv2.Canny(blurred, edges, th[0], th[1]);

            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(5, 5));
            using Mat closedEdges = new Mat();
            Cv2.MorphologyEx(edges, closedEdges, MorphTypes.Close, kernel);

            Cv2.FindContours(closedEdges, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            foreach (Point[] contour in contours.OrderByDescending(c => Cv2.ContourArea(c)))
            {
                double area = Cv2.ContourArea(contour);
                if (area < minDocArea) break;

                Point[] hull = Cv2.ConvexHull(contour);
                double perimeter = Cv2.ArcLength(hull, true);

                for (double epsFactor = 0.015; epsFactor <= 0.045; epsFactor += 0.01)
                {
                    Point[] approx = Cv2.ApproxPolyDP(hull, epsFactor * perimeter, true);
                    if (approx.Length == 4 && Cv2.IsContourConvex(approx))
                    {
                        Point2f[] quad = approx.Select(p => new Point2f((float)(p.X * invScale), (float)(p.Y * invScale))).ToArray();
                        quads.Add(quad);
                        break;
                    }
                }
            }
        }

        return quads;
    }

    /// <summary>
    /// Computes the ratio of text points enclosed inside a candidate quadrilateral.
    /// </summary>
    private double CalculatePointContainmentRatio(Point2f[] quad, List<Point2f> points)
    {
        if (points.Count == 0) return 0.0;

        Point2f[] ordered = SortCornersClockwise(quad).ToArray();
        Point[] contour = ordered.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();

        int insideCount = 0;
        foreach (Point2f pt in points)
        {
            double dist = Cv2.PointPolygonTest(contour, new Point2f(pt.X, pt.Y), measureDist: false);
            if (dist >= 0)
            {
                insideCount++;
            }
        }

        return (double)insideCount / points.Count;
    }

    /// <summary>
    /// Constructs an optimal document quadrilateral from text points with natural page margins.
    /// </summary>
    private Point2f[]? ComputeTextEnvelopeQuad(List<Point2f> textPoints, int origWidth, int origHeight)
    {
        if (textPoints.Count < 4) return null;

        RotatedRect minRect = Cv2.MinAreaRect(textPoints.ToArray());
        float rectW = minRect.Size.Width;
        float rectH = minRect.Size.Height;
        float angle = minRect.Angle;

        // Standard page margin padding (12% horizontal margin, 15% vertical margin)
        float marginX = rectW * 0.12f;
        float marginY = rectH * 0.15f;

        float expandedW = Math.Min(origWidth, rectW + (marginX * 2));
        float expandedH = Math.Min(origHeight, rectH + (marginY * 2));

        RotatedRect expandedRect = new RotatedRect(minRect.Center, new Size2f(expandedW, expandedH), angle);
        Point2f[] boxPts = expandedRect.Points();

        // Clamp points to image dimensions
        for (int i = 0; i < boxPts.Length; i++)
        {
            boxPts[i] = new Point2f(
                Math.Clamp(boxPts[i].X, 0f, (float)origWidth),
                Math.Clamp(boxPts[i].Y, 0f, (float)origHeight)
            );
        }

        return boxPts;
    }

    public Mat WarpPerspective(Mat sourceImage, DocumentCorners corners)
    {
        if (sourceImage == null || sourceImage.Empty())
        {
            throw new ArgumentNullException(nameof(sourceImage));
        }

        Point2f[] ordered = corners.ToArray();
        Point2f tl = ordered[0];
        Point2f tr = ordered[1];
        Point2f br = ordered[2];
        Point2f bl = ordered[3];

        // Calculate destination dimensions via Euclidean distances
        double widthTop = Math.Sqrt(Math.Pow(tr.X - tl.X, 2) + Math.Pow(tr.Y - tl.Y, 2));
        double widthBottom = Math.Sqrt(Math.Pow(br.X - bl.X, 2) + Math.Pow(br.Y - bl.Y, 2));
        int maxWidth = Math.Max(1, (int)Math.Round(Math.Max(widthTop, widthBottom)));

        double heightLeft = Math.Sqrt(Math.Pow(bl.X - tl.X, 2) + Math.Pow(bl.Y - tl.Y, 2));
        double heightRight = Math.Sqrt(Math.Pow(br.X - tr.X, 2) + Math.Pow(br.Y - tr.Y, 2));
        int maxHeight = Math.Max(1, (int)Math.Round(Math.Max(heightLeft, heightRight)));

        Point2f[] destination = new[]
        {
            new Point2f(0, 0),
            new Point2f(maxWidth - 1, 0),
            new Point2f(maxWidth - 1, maxHeight - 1),
            new Point2f(0, maxHeight - 1)
        };

        using Mat transformMatrix = Cv2.GetPerspectiveTransform(ordered, destination);
        Mat warped = new Mat();
        Cv2.WarpPerspective(sourceImage, warped, transformMatrix, new OpenCvSharp.Size(maxWidth, maxHeight), InterpolationFlags.Cubic);
        return warped;
    }

    public Mat RotateImage(Mat sourceImage, int angleDegrees)
    {
        if (sourceImage == null || sourceImage.Empty()) return new Mat();

        int normalizedAngle = (angleDegrees % 360 + 360) % 360;
        Mat result = new Mat();

        switch (normalizedAngle)
        {
            case 90:
                Cv2.Rotate(sourceImage, result, RotateFlags.Rotate90Clockwise);
                break;
            case 180:
                Cv2.Rotate(sourceImage, result, RotateFlags.Rotate180);
                break;
            case 270:
                Cv2.Rotate(sourceImage, result, RotateFlags.Rotate90Counterclockwise);
                break;
            default:
                sourceImage.CopyTo(result);
                break;
        }

        return result;
    }

    public Mat EnhanceDocument(Mat warpedImage, EnhancementFilterType filterType, FilterParameters parameters)
    {
        if (warpedImage == null || warpedImage.Empty())
        {
            return new Mat();
        }

        return filterType switch
        {
            EnhancementFilterType.Original => ApplyOriginalAdjustments(warpedImage, parameters),
            EnhancementFilterType.EnhancedColor => ApplyEnhancedColor(warpedImage, parameters),
            EnhancementFilterType.BlackAndWhite => ApplyBlackAndWhite(warpedImage, parameters),
            EnhancementFilterType.GrayscaleHighContrast => ApplyGrayscaleHighContrast(warpedImage, parameters),
            EnhancementFilterType.SharpenOnly => ApplySharpenOnly(warpedImage, parameters),
            EnhancementFilterType.Custom => ApplyCustomFilter(warpedImage, parameters),
            _ => ApplyEnhancedColor(warpedImage, parameters)
        };
    }

    /// <summary>
    /// Enhanced Color: High-performance document enhancement.
    /// 1. Illumination background division to remove shadows and even out paper lighting.
    /// 2. LAB Lightness channel CLAHE + color contrast enhancement.
    /// 3. High-frequency unsharp masking for ultra-crisp text.
    /// </summary>
    private Mat ApplyEnhancedColor(Mat input, FilterParameters p)
    {
        // 1. Illumination Normalization via Background Division
        using Mat normalized = RemoveShadowsColor(input, p.ShadowSuppression);

        // 2. Convert to LAB color space for luminance contrast stretching without color shift
        using Mat lab = new Mat();
        Cv2.CvtColor(normalized, lab, ColorConversionCodes.BGR2Lab);

        Mat[] labPlanes = Cv2.Split(lab);
        using Mat lChannel = labPlanes[0];
        using Mat aChannel = labPlanes[1];
        using Mat bChannel = labPlanes[2];

        // Apply CLAHE on L-channel
        using (CLAHE clahe = Cv2.CreateCLAHE(clipLimit: 2.2, tileGridSize: new OpenCvSharp.Size(8, 8)))
        {
            clahe.Apply(lChannel, lChannel);
        }

        // Linear contrast/brightness fine-tuning on L-channel
        if (Math.Abs(p.Contrast - 1.0) > 0.01 || Math.Abs(p.Brightness) > 0.1)
        {
            lChannel.ConvertTo(lChannel, -1, alpha: p.Contrast, beta: p.Brightness);
        }

        // Merge channels back
        using Mat enhancedLab = new Mat();
        Cv2.Merge(new[] { lChannel, aChannel, bChannel }, enhancedLab);

        Mat enhancedBgr = new Mat();
        Cv2.CvtColor(enhancedLab, enhancedBgr, ColorConversionCodes.Lab2BGR);

        // Dispose split planes
        foreach (Mat plane in labPlanes) plane.Dispose();

        // 3. Unsharp Masking for character sharpness
        if (p.Sharpening > 0.05)
        {
            Mat sharpened = ApplyUnsharpMask(enhancedBgr, p.Sharpening);
            enhancedBgr.Dispose();
            return sharpened;
        }

        return enhancedBgr;
    }

    /// <summary>
    /// Black & White: Crisp binarization for document scanning.
    /// Pure white paper (#FFFFFF) and deep black text (#000000) without background shadow noise.
    /// </summary>
    private Mat ApplyBlackAndWhite(Mat input, FilterParameters p)
    {
        // 1. Convert to Grayscale
        using Mat gray = new Mat();
        if (input.Channels() > 1)
        {
            Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            input.CopyTo(gray);
        }

        // 2. Background Division / Shadow Removal on Grayscale
        using Mat bgDivided = RemoveShadowsGrayscale(gray, p.ShadowSuppression);

        // 3. Adaptive Thresholding (Gaussian C)
        int minDim = Math.Min(bgDivided.Width, bgDivided.Height);
        int blockSize = Math.Max(21, (minDim / 60) | 1); // Must be odd
        if (blockSize % 2 == 0) blockSize++;

        int cOffset = 8 + p.BinarizationThresholdOffset;

        Mat binary = new Mat();
        Cv2.AdaptiveThreshold(bgDivided, binary, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, blockSize, cOffset);

        // 4. Morphological noise reduction if requested
        if (p.DenoiseStrength > 0)
        {
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(2, 2));
            using Mat cleaned = new Mat();
            Cv2.MorphologyEx(binary, cleaned, MorphTypes.Open, kernel);
            cleaned.CopyTo(binary);
        }

        return binary;
    }

    /// <summary>
    /// Grayscale High Contrast: Adaptive CLAHE with illumination normalization and unsharp masking.
    /// </summary>
    private Mat ApplyGrayscaleHighContrast(Mat input, FilterParameters p)
    {
        using Mat gray = new Mat();
        if (input.Channels() > 1)
        {
            Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            input.CopyTo(gray);
        }

        using Mat bgDivided = RemoveShadowsGrayscale(gray, p.ShadowSuppression);

        Mat claheResult = new Mat();
        using (CLAHE clahe = Cv2.CreateCLAHE(clipLimit: 2.8, tileGridSize: new OpenCvSharp.Size(8, 8)))
        {
            clahe.Apply(bgDivided, claheResult);
        }

        if (Math.Abs(p.Contrast - 1.0) > 0.01 || Math.Abs(p.Brightness) > 0.1)
        {
            claheResult.ConvertTo(claheResult, -1, alpha: p.Contrast, beta: p.Brightness);
        }

        if (p.Sharpening > 0.05)
        {
            Mat sharpened = ApplyUnsharpMask(claheResult, p.Sharpening);
            claheResult.Dispose();
            return sharpened;
        }

        return claheResult;
    }

    private Mat ApplySharpenOnly(Mat input, FilterParameters p)
    {
        double strength = p.Sharpening > 0.05 ? p.Sharpening : 0.8;
        return ApplyUnsharpMask(input, strength);
    }

    private Mat ApplyOriginalAdjustments(Mat input, FilterParameters p)
    {
        Mat result = new Mat();
        if (Math.Abs(p.Contrast - 1.0) > 0.01 || Math.Abs(p.Brightness) > 0.1)
        {
            input.ConvertTo(result, -1, alpha: p.Contrast, beta: p.Brightness);
        }
        else
        {
            input.CopyTo(result);
        }
        return result;
    }

    private Mat ApplyCustomFilter(Mat input, FilterParameters p)
    {
        using Mat shadowNormalized = p.ShadowSuppression > 0.1
            ? (input.Channels() > 1 ? RemoveShadowsColor(input, p.ShadowSuppression) : RemoveShadowsGrayscale(input, p.ShadowSuppression))
            : input.Clone();

        Mat result = new Mat();
        shadowNormalized.ConvertTo(result, -1, alpha: p.Contrast, beta: p.Brightness);

        if (p.Sharpening > 0.05)
        {
            Mat sharpened = ApplyUnsharpMask(result, p.Sharpening);
            result.Dispose();
            return sharpened;
        }

        return result;
    }

    /// <summary>
    /// Optical background estimation & division for color images to eliminate phone camera shadows.
    /// Division model: Output = (Source / Dilated_Background) * 255.
    /// </summary>
    private Mat RemoveShadowsColor(Mat input, double suppressionStrength)
    {
        if (suppressionStrength <= 0.01) return input.Clone();

        Mat[] channels = Cv2.Split(input);
        Mat[] normalizedChannels = new Mat[channels.Length];

        int kernelSize = Math.Max(15, (Math.Max(input.Width, input.Height) / 25) | 1);
        if (kernelSize % 2 == 0) kernelSize++;

        using Mat structElement = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(kernelSize, kernelSize));

        for (int i = 0; i < channels.Length; i++)
        {
            using Mat plane = channels[i];
            using Mat dilated = new Mat();
            Cv2.Dilate(plane, dilated, structElement);

            using Mat bg = new Mat();
            Cv2.GaussianBlur(dilated, bg, new OpenCvSharp.Size(kernelSize, kernelSize), 0);

            double scaleVal = 255.0 * Math.Min(1.25, Math.Max(1.0, suppressionStrength));
            Mat divided = new Mat();
            Cv2.Divide(plane, bg, divided, scale: scaleVal);

            if (suppressionStrength < 0.99)
            {
                // Blend with original according to strength
                Mat blended = new Mat();
                Cv2.AddWeighted(divided, suppressionStrength, plane, 1.0 - suppressionStrength, 0, blended);
                divided.Dispose();
                normalizedChannels[i] = blended;
            }
            else
            {
                normalizedChannels[i] = divided;
            }

            plane.Dispose();
        }

        Mat result = new Mat();
        Cv2.Merge(normalizedChannels, result);

        foreach (Mat ch in normalizedChannels) ch.Dispose();
        return result;
    }

    /// <summary>
    /// Optical background estimation & division for grayscale images.
    /// </summary>
    private Mat RemoveShadowsGrayscale(Mat gray, double suppressionStrength)
    {
        if (suppressionStrength <= 0.01) return gray.Clone();

        int kernelSize = Math.Max(15, (Math.Max(gray.Width, gray.Height) / 25) | 1);
        if (kernelSize % 2 == 0) kernelSize++;

        using Mat structElement = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(kernelSize, kernelSize));
        using Mat dilated = new Mat();
        Cv2.Dilate(gray, dilated, structElement);

        using Mat bg = new Mat();
        Cv2.GaussianBlur(dilated, bg, new OpenCvSharp.Size(kernelSize, kernelSize), 0);

        double scaleVal = 255.0 * Math.Min(1.25, Math.Max(1.0, suppressionStrength));
        Mat divided = new Mat();
        Cv2.Divide(gray, bg, divided, scale: scaleVal);

        if (suppressionStrength < 0.99)
        {
            Mat blended = new Mat();
            Cv2.AddWeighted(divided, suppressionStrength, gray, 1.0 - suppressionStrength, 0, blended);
            divided.Dispose();
            return blended;
        }

        return divided;
    }

    /// <summary>
    /// Unsharp masking to boost text character edge high frequencies.
    /// </summary>
    private Mat ApplyUnsharpMask(Mat input, double strength)
    {
        using Mat blurred = new Mat();
        Cv2.GaussianBlur(input, blurred, new OpenCvSharp.Size(0, 0), sigmaX: 1.5, sigmaY: 1.5);

        double alpha = 1.0 + strength;
        double beta = -strength;

        Mat sharpened = new Mat();
        Cv2.AddWeighted(input, alpha, blurred, beta, 0, sharpened);
        return sharpened;
    }

    public BitmapSource MatToBitmapSource(Mat mat)
    {
        if (mat == null || mat.Empty())
        {
            return new BitmapImage();
        }

        BitmapSource bs = mat.ToBitmapSource();
        if (bs.CanFreeze)
        {
            bs.Freeze();
        }
        return bs;
    }

    public BitmapSource CreateThumbnail(Mat mat, int maxDimension = 180)
    {
        if (mat == null || mat.Empty())
        {
            return new BitmapImage();
        }

        double scale = Math.Min(1.0, (double)maxDimension / Math.Max(mat.Width, mat.Height));
        int thumbW = Math.Max(1, (int)Math.Round(mat.Width * scale));
        int thumbH = Math.Max(1, (int)Math.Round(mat.Height * scale));

        using Mat thumbMat = new Mat();
        Cv2.Resize(mat, thumbMat, new OpenCvSharp.Size(thumbW, thumbH), interpolation: InterpolationFlags.Area);

        BitmapSource bs = thumbMat.ToBitmapSource();
        if (bs.CanFreeze)
        {
            bs.Freeze();
        }
        return bs;
    }

    public void SaveImage(Mat mat, string destinationPath, int quality = 95)
    {
        if (mat == null || mat.Empty())
        {
            throw new ArgumentNullException(nameof(mat));
        }

        string ext = Path.GetExtension(destinationPath).ToLowerInvariant();
        ImageEncodingParam[] encParams;

        if (ext is ".jpg" or ".jpeg")
        {
            encParams = new[] { new ImageEncodingParam(ImwriteFlags.JpegQuality, quality) };
        }
        else if (ext is ".png")
        {
            encParams = new[] { new ImageEncodingParam(ImwriteFlags.PngCompression, 3) };
        }
        else if (ext is ".webp")
        {
            encParams = new[] { new ImageEncodingParam(ImwriteFlags.WebPQuality, quality) };
        }
        else
        {
            encParams = Array.Empty<ImageEncodingParam>();
        }

        Cv2.ImEncode(ext, mat, out byte[] encodedBytes, encParams);
        File.WriteAllBytes(destinationPath, encodedBytes);
    }

    private DocumentCorners SortCornersClockwise(Point2f[] pts)
    {
        // TopLeft has min (x + y), BottomRight has max (x + y)
        Point2f tl = pts.OrderBy(p => p.X + p.Y).First();
        Point2f br = pts.OrderByDescending(p => p.X + p.Y).First();

        // TopRight has min (y - x), BottomLeft has max (y - x)
        Point2f tr = pts.OrderBy(p => p.Y - p.X).First();
        Point2f bl = pts.OrderByDescending(p => p.Y - p.X).First();

        return new DocumentCorners(tl, tr, br, bl);
    }
}
