using System.Windows;
using OpenCvSharp;

namespace DocScanner.Models;

/// <summary>
/// Represents the four corner vertices of a document quadrilateral in pixel coordinates.
/// Order is clockwise: TopLeft -> TopRight -> BottomRight -> BottomLeft.
/// </summary>
public class DocumentCorners
{
    public Point2f TopLeft { get; set; }
    public Point2f TopRight { get; set; }
    public Point2f BottomRight { get; set; }
    public Point2f BottomLeft { get; set; }

    public DocumentCorners()
    {
    }

    public DocumentCorners(Point2f topLeft, Point2f topRight, Point2f bottomRight, Point2f bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    public DocumentCorners(float width, float height)
    {
        TopLeft = new Point2f(0, 0);
        TopRight = new Point2f(width, 0);
        BottomRight = new Point2f(width, height);
        BottomLeft = new Point2f(0, height);
    }

    public Point2f[] ToArray()
    {
        return new[] { TopLeft, TopRight, BottomRight, BottomLeft };
    }

    public static DocumentCorners FromArray(Point2f[] points)
    {
        if (points == null || points.Length != 4)
        {
            throw new ArgumentException("Corners array must contain exactly 4 points.", nameof(points));
        }

        return new DocumentCorners(points[0], points[1], points[2], points[3]);
    }

    public DocumentCorners Clone()
    {
        return new DocumentCorners(TopLeft, TopRight, BottomRight, BottomLeft);
    }
}
