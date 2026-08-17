# DocScanner - High-Performance Document Scanning & Contrast Enhancement

DocScanner is a modern Windows desktop application built with .NET 8 WPF and OpenCvSharp4. It provides mobile-grade document scanning capabilities—including perspective unwarping, illumination normalization (shadow elimination), high-contrast CamScanner-style enhancement filters, and multi-page PDF compilation.

---

## Architectural Overview

The application is structured around a modular MVVM architecture coupled with high-performance C++ OpenCV computer vision bindings:

```
[ Input: Image / Drag-Drop / Clipboard ]
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│                 Document Processing Pipeline                │
├─────────────────────────────────────────────────────────────┤
│ 1. Boundary & Quad Detection (Canny + Convex Hull + Approx) │
│ 2. Perspective Homography Warping (4-Corner Unwarping)      │
│ 3. Illumination Normalization (Morphological BG Division)   │
│ 4. Adaptive Contrast & Sharpening Filters (LAB CLAHE)       │
│ 5. Multi-Page Session Queue & PDF Compilation (PdfSharp)    │
└─────────────────────────────────────────────────────────────┘
                   │
                   ▼
[ Output: Multi-Page PDF / High-Res PNG / JPEG / TIFF / WebP ]
```

---

## Key Features

### 1. Optical Illumination Normalization & Shadow Removal
- Employs morphological dilation and Gaussian background division model (`Image / Dilated_Background * 255`).
- Eliminates phone shadows, finger shadows, gradient lighting, and yellowed paper tones.

### 2. Enhancement Presets
- **Enhanced Color**: High-performance illumination normalization. Flattens backgrounds to pure paper white, enhances character edge contrast via high-pass unsharp masking, and preserves vivid color on stamps, signatures, and markers in LAB color space.
- **B&W Clean Document**: Adaptive Gaussian binarization (`Cv2.AdaptiveThreshold`) with morphological noise reduction, generating pure `#000000` text on `#FFFFFF` paper suitable for high-speed printing and OCR.
- **Grayscale High-Contrast**: Contrast-Limited Adaptive Histogram Equalization (CLAHE) for monochrome legal and academic records.
- **Detail Sharpening**: High-frequency edge boosting for soft-focus or motion-blurred captures.
- **Custom Mode**: Real-time slider controls for Shadow Suppression, Contrast, Brightness, Sharpening, and Binarization Threshold.

### 3. Lightweight OCR Text-Aware & Perspective Crop
- **Lightweight OCR Text Saliency**: High-speed morphological character stroke detection (Sobel + Otsu + horizontal kernel bridging) that accurately identifies the bounding envelope of text lines, signatures, and printed paragraphs.
- **Hybrid Boundary Fusion**: Validates paper candidate contours against text containment (>80%) to avoid cropping into text or capturing extraneous desk background.
- **Interactive 4-Point Quadrilateral Manipulation**: Vector-based WPF overlay (`InteractiveCropCanvas`) with draggable corner handles, midpoint edge sliders, and dimmed boundary masks.

### 4. Multi-Page Document Management & PDF Export
- Thumbnail sidebar for page reordering (Move Up, Move Down, Delete).
- Asynchronous non-blocking background rendering.
- Direct multi-page PDF document compilation with A4 page fitting and DPI scaling.
- Batch export to local directory.

---

## System Requirements & Prerequisites

- **Operating System**: Windows 10 / Windows 11 (x64)
- **Runtime**: .NET 8.0 Windows Desktop Runtime (`Microsoft.WindowsDesktop.App 8.0+`)
- **Dependencies**: OpenCvSharp4 (4.13+), OpenCvSharp4.runtime.win, PdfSharpCore, CommunityToolkit.Mvvm

---

## Build & Execution Instructions

### Building the Application
To build the solution in Release mode:

```bash
cd DocScanner
dotnet build -c Release
```

### Running the Application
To launch the desktop application:

```bash
cd DocScanner
dotnet run -c Release
```

### Publishing Standalone Binary
To generate a self-contained, single-file executable:

```bash
cd DocScanner
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```

---

## File Structure

```
DocScanner/
├── Controls/
│   └── InteractiveCropCanvas.cs    # Custom WPF vector overlay for 4-point quadrilateral dragging
├── Converters/
│   └── ValueConverters.cs          # WPF XAML UI bindings converters
├── Models/
│   ├── DocumentCorners.cs          # Quad coordinate data model (TopLeft, TopRight, BR, BL)
│   ├── EnhancementFilterType.cs    # Filter presets enum (EnhancedColor, B&W, Grayscale, etc.)
│   ├── FilterParameters.cs         # Numerical tuning parameters (Contrast, Sharpness, etc.)
│   └── ScannedPage.cs              # Multi-page document model and thumbnail cache
├── Services/
│   ├── IImageProcessingService.cs  # Computer vision processing contract
│   ├── ImageProcessingService.cs   # OpenCV processing algorithms & filter implementations
│   ├── IPdfExportService.cs        # PDF export service contract
│   └── PdfExportService.cs         # PdfSharp multi-page document compiler
├── Themes/
│   └── DarkTheme.xaml              # VS Code Dark Modern (#1f1f1f) theme dictionary
├── ViewModels/
│   └── MainViewModel.cs            # MVVM ViewModel with reactive debounced processing
├── App.xaml / App.xaml.cs          # Application entry point
├── MainWindow.xaml / .cs           # 3-panel UI layout (Sidebar, Canvas, Controls)
└── DocScanner.csproj               # .NET 8 WPF project file
```
