using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocScanner.Models;
using OpenCvSharp;
using Rect = System.Windows.Rect;
using Point = System.Windows.Point;

namespace DocScanner.Controls;

/// <summary>
/// Custom WPF control rendering an interactive quadrilateral crop overlay with draggable handles.
/// </summary>
public class InteractiveCropCanvas : FrameworkElement
{
    private enum DragTarget
    {
        None,
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft,
        TopEdge,
        RightEdge,
        BottomEdge,
        LeftEdge
    }

    public static readonly DependencyProperty SourceImageProperty =
        DependencyProperty.Register(
            nameof(SourceImage),
            typeof(BitmapSource),
            typeof(InteractiveCropCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornersProperty =
        DependencyProperty.Register(
            nameof(Corners),
            typeof(DocumentCorners),
            typeof(InteractiveCropCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ImagePixelWidthProperty =
        DependencyProperty.Register(
            nameof(ImagePixelWidth),
            typeof(double),
            typeof(InteractiveCropCanvas),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ImagePixelHeightProperty =
        DependencyProperty.Register(
            nameof(ImagePixelHeight),
            typeof(double),
            typeof(InteractiveCropCanvas),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public BitmapSource? SourceImage
    {
        get => (BitmapSource?)GetValue(SourceImageProperty);
        set => SetValue(SourceImageProperty, value);
    }

    public DocumentCorners? Corners
    {
        get => (DocumentCorners?)GetValue(CornersProperty);
        set => SetValue(CornersProperty, value);
    }

    public double ImagePixelWidth
    {
        get => (double)GetValue(ImagePixelWidthProperty);
        set => SetValue(ImagePixelWidthProperty, value);
    }

    public double ImagePixelHeight
    {
        get => (double)GetValue(ImagePixelHeightProperty);
        set => SetValue(ImagePixelHeightProperty, value);
    }

    public event EventHandler? CornersManipulated;

    private DragTarget _activeDrag = DragTarget.None;
    private System.Windows.Point _lastMouseCanvasPos;

    private readonly Pen _quadPen;
    private readonly Pen _gridPen;
    private readonly Brush _handleBrush;
    private readonly Pen _handleBorderPen;
    private readonly Brush _innerHandleBrush;
    private readonly Brush _dimmedOverlayBrush;

    public InteractiveCropCanvas()
    {
        ClipToBounds = true;
        Focusable = true;

        Color accentColor = Color.FromRgb(0, 122, 204); // #007acc VS Code blue
        _quadPen = new Pen(new SolidColorBrush(accentColor), 2.5);
        _quadPen.Freeze();

        _gridPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 122, 204)), 1.0)
        {
            DashStyle = DashStyles.Dash
        };
        _gridPen.Freeze();

        _handleBrush = new SolidColorBrush(accentColor);
        _handleBrush.Freeze();

        _handleBorderPen = new Pen(Brushes.White, 2.0);
        _handleBorderPen.Freeze();

        _innerHandleBrush = Brushes.White;

        _dimmedOverlayBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
        _dimmedOverlayBrush.Freeze();
    }

    private System.Windows.Rect GetImageDisplayRect()
    {
        if (ImagePixelWidth <= 0 || ImagePixelHeight <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return new System.Windows.Rect(0, 0, ActualWidth, ActualHeight);
        }

        double imgAspect = ImagePixelWidth / ImagePixelHeight;
        double canvasAspect = ActualWidth / ActualHeight;

        double dispW, dispH, dispX, dispY;
        if (imgAspect > canvasAspect)
        {
            dispW = ActualWidth;
            dispH = ActualWidth / imgAspect;
            dispX = 0;
            dispY = (ActualHeight - dispH) / 2.0;
        }
        else
        {
            dispH = ActualHeight;
            dispW = ActualHeight * imgAspect;
            dispX = (ActualWidth - dispW) / 2.0;
            dispY = 0;
        }

        return new System.Windows.Rect(dispX, dispY, dispW, dispH);
    }

    private System.Windows.Point ImageToCanvas(Point2f imgPt, System.Windows.Rect dispRect)
    {
        double normX = ImagePixelWidth > 0 ? imgPt.X / ImagePixelWidth : 0;
        double normY = ImagePixelHeight > 0 ? imgPt.Y / ImagePixelHeight : 0;

        return new System.Windows.Point(
            dispRect.X + normX * dispRect.Width,
            dispRect.Y + normY * dispRect.Height
        );
    }

    private Point2f CanvasToImage(System.Windows.Point canvasPt, System.Windows.Rect dispRect)
    {
        double relX = (canvasPt.X - dispRect.X) / Math.Max(1.0, dispRect.Width);
        double relY = (canvasPt.Y - dispRect.Y) / Math.Max(1.0, dispRect.Height);

        relX = Math.Clamp(relX, 0.0, 1.0);
        relY = Math.Clamp(relY, 0.0, 1.0);

        return new Point2f(
            (float)(relX * ImagePixelWidth),
            (float)(relY * ImagePixelHeight)
        );
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        Rect dispRect = GetImageDisplayRect();

        // 1. Draw source image
        if (SourceImage != null)
        {
            dc.DrawImage(SourceImage, dispRect);
        }
        else
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 30)), null, new Rect(0, 0, ActualWidth, ActualHeight));
            return;
        }

        if (Corners == null) return;

        // 2. Compute screen points
        System.Windows.Point p0 = ImageToCanvas(Corners.TopLeft, dispRect);
        System.Windows.Point p1 = ImageToCanvas(Corners.TopRight, dispRect);
        System.Windows.Point p2 = ImageToCanvas(Corners.BottomRight, dispRect);
        System.Windows.Point p3 = ImageToCanvas(Corners.BottomLeft, dispRect);

        // 3. Dim outer background via CombinedGeometry
        PathGeometry quadPath = new PathGeometry();
        PathFigure figure = new PathFigure { StartPoint = p0, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(p1, true));
        figure.Segments.Add(new LineSegment(p2, true));
        figure.Segments.Add(new LineSegment(p3, true));
        quadPath.Figures.Add(figure);

        RectangleGeometry fullRectGeom = new RectangleGeometry(dispRect);
        CombinedGeometry maskGeom = new CombinedGeometry(GeometryCombineMode.Exclude, fullRectGeom, quadPath);
        dc.DrawGeometry(_dimmedOverlayBrush, null, maskGeom);

        // 4. Draw grid inside quad
        System.Windows.Point midTop = MidPoint(p0, p1);
        System.Windows.Point midBottom = MidPoint(p3, p2);
        System.Windows.Point midLeft = MidPoint(p0, p3);
        System.Windows.Point midRight = MidPoint(p1, p2);

        dc.DrawLine(_gridPen, midTop, midBottom);
        dc.DrawLine(_gridPen, midLeft, midRight);

        // 5. Draw quad contour boundary
        dc.DrawLine(_quadPen, p0, p1);
        dc.DrawLine(_quadPen, p1, p2);
        dc.DrawLine(_quadPen, p2, p3);
        dc.DrawLine(_quadPen, p3, p0);

        // 6. Draw edge midpoint handles
        DrawEdgeHandle(dc, midTop);
        DrawEdgeHandle(dc, midRight);
        DrawEdgeHandle(dc, midBottom);
        DrawEdgeHandle(dc, midLeft);

        // 7. Draw 4 Corner Handles
        DrawCornerHandle(dc, p0);
        DrawCornerHandle(dc, p1);
        DrawCornerHandle(dc, p2);
        DrawCornerHandle(dc, p3);
    }

    private void DrawCornerHandle(DrawingContext dc, System.Windows.Point center)
    {
        const double outerRadius = 10.0;
        const double innerRadius = 3.5;

        dc.DrawEllipse(_handleBrush, _handleBorderPen, center, outerRadius, outerRadius);
        dc.DrawEllipse(_innerHandleBrush, null, center, innerRadius, innerRadius);
    }

    private void DrawEdgeHandle(DrawingContext dc, System.Windows.Point center)
    {
        const double radius = 5.5;
        dc.DrawEllipse(Brushes.White, _quadPen, center, radius, radius);
    }

    private System.Windows.Point MidPoint(System.Windows.Point a, System.Windows.Point b)
    {
        return new System.Windows.Point((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Left || Corners == null) return;

        Rect dispRect = GetImageDisplayRect();
        System.Windows.Point mousePos = e.GetPosition(this);
        _lastMouseCanvasPos = mousePos;

        System.Windows.Point p0 = ImageToCanvas(Corners.TopLeft, dispRect);
        System.Windows.Point p1 = ImageToCanvas(Corners.TopRight, dispRect);
        System.Windows.Point p2 = ImageToCanvas(Corners.BottomRight, dispRect);
        System.Windows.Point p3 = ImageToCanvas(Corners.BottomLeft, dispRect);

        const double hitThreshold = 22.0;

        if (Distance(mousePos, p0) <= hitThreshold) _activeDrag = DragTarget.TopLeft;
        else if (Distance(mousePos, p1) <= hitThreshold) _activeDrag = DragTarget.TopRight;
        else if (Distance(mousePos, p2) <= hitThreshold) _activeDrag = DragTarget.BottomRight;
        else if (Distance(mousePos, p3) <= hitThreshold) _activeDrag = DragTarget.BottomLeft;
        else if (Distance(mousePos, MidPoint(p0, p1)) <= hitThreshold) _activeDrag = DragTarget.TopEdge;
        else if (Distance(mousePos, MidPoint(p1, p2)) <= hitThreshold) _activeDrag = DragTarget.RightEdge;
        else if (Distance(mousePos, MidPoint(p2, p3)) <= hitThreshold) _activeDrag = DragTarget.BottomEdge;
        else if (Distance(mousePos, MidPoint(p3, p0)) <= hitThreshold) _activeDrag = DragTarget.LeftEdge;
        else _activeDrag = DragTarget.None;

        if (_activeDrag != DragTarget.None)
        {
            CaptureMouse();
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Rect dispRect = GetImageDisplayRect();
        System.Windows.Point mousePos = e.GetPosition(this);

        if (_activeDrag != DragTarget.None && IsMouseCaptured && Corners != null)
        {
            Point2f imgPos = CanvasToImage(mousePos, dispRect);
            Point2f lastImgPos = CanvasToImage(_lastMouseCanvasPos, dispRect);
            float deltaX = imgPos.X - lastImgPos.X;
            float deltaY = imgPos.Y - lastImgPos.Y;

            switch (_activeDrag)
            {
                case DragTarget.TopLeft:
                    Corners.TopLeft = imgPos;
                    break;
                case DragTarget.TopRight:
                    Corners.TopRight = imgPos;
                    break;
                case DragTarget.BottomRight:
                    Corners.BottomRight = imgPos;
                    break;
                case DragTarget.BottomLeft:
                    Corners.BottomLeft = imgPos;
                    break;
                case DragTarget.TopEdge:
                    Corners.TopLeft = ClampPoint(new Point2f(Corners.TopLeft.X + deltaX, Corners.TopLeft.Y + deltaY));
                    Corners.TopRight = ClampPoint(new Point2f(Corners.TopRight.X + deltaX, Corners.TopRight.Y + deltaY));
                    break;
                case DragTarget.RightEdge:
                    Corners.TopRight = ClampPoint(new Point2f(Corners.TopRight.X + deltaX, Corners.TopRight.Y + deltaY));
                    Corners.BottomRight = ClampPoint(new Point2f(Corners.BottomRight.X + deltaX, Corners.BottomRight.Y + deltaY));
                    break;
                case DragTarget.BottomEdge:
                    Corners.BottomRight = ClampPoint(new Point2f(Corners.BottomRight.X + deltaX, Corners.BottomRight.Y + deltaY));
                    Corners.BottomLeft = ClampPoint(new Point2f(Corners.BottomLeft.X + deltaX, Corners.BottomLeft.Y + deltaY));
                    break;
                case DragTarget.LeftEdge:
                    Corners.BottomLeft = ClampPoint(new Point2f(Corners.BottomLeft.X + deltaX, Corners.BottomLeft.Y + deltaY));
                    Corners.TopLeft = ClampPoint(new Point2f(Corners.TopLeft.X + deltaX, Corners.TopLeft.Y + deltaY));
                    break;
            }

            _lastMouseCanvasPos = mousePos;
            InvalidateVisual();
            return;
        }

        // Update cursor on hover
        if (Corners != null)
        {
            System.Windows.Point p0 = ImageToCanvas(Corners.TopLeft, dispRect);
            System.Windows.Point p1 = ImageToCanvas(Corners.TopRight, dispRect);
            System.Windows.Point p2 = ImageToCanvas(Corners.BottomRight, dispRect);
            System.Windows.Point p3 = ImageToCanvas(Corners.BottomLeft, dispRect);

            const double hoverRadius = 22.0;
            if (Distance(mousePos, p0) <= hoverRadius || Distance(mousePos, p1) <= hoverRadius ||
                Distance(mousePos, p2) <= hoverRadius || Distance(mousePos, p3) <= hoverRadius)
            {
                Cursor = Cursors.Cross;
            }
            else if (Distance(mousePos, MidPoint(p0, p1)) <= hoverRadius || Distance(mousePos, MidPoint(p2, p3)) <= hoverRadius)
            {
                Cursor = Cursors.SizeNS;
            }
            else if (Distance(mousePos, MidPoint(p1, p2)) <= hoverRadius || Distance(mousePos, MidPoint(p3, p0)) <= hoverRadius)
            {
                Cursor = Cursors.SizeWE;
            }
            else
            {
                Cursor = Cursors.Arrow;
            }
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            _activeDrag = DragTarget.None;
            InvalidateVisual();
            CornersManipulated?.Invoke(this, EventArgs.Empty);
        }
    }

    private Point2f ClampPoint(Point2f pt)
    {
        return new Point2f(
            Math.Clamp(pt.X, 0f, (float)ImagePixelWidth),
            Math.Clamp(pt.Y, 0f, (float)ImagePixelHeight)
        );
    }

    private double Distance(System.Windows.Point a, System.Windows.Point b)
    {
        return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    }
}
