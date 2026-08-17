# Aetherlight

A Windows-native RAW photo management and development application inspired by professional desktop photo workflows.

## Vision

Aetherlight is designed around a non-destructive pipeline:

**Import → Catalog → RAW decode → Develop → Mask → Compare → Export**

Supported target RAW families include Canon CR3, Sony ARW and Fujifilm RAF, plus DNG, TIFF and common raster formats.

## Current foundation

- .NET 8 / WPF Windows desktop application
- x64 self-contained publish configuration
- LibRaw runtime dependency selected for camera RAW decoding
- Lightroom-style three-pane workspace
- Library / Develop / Map / Print / Web module navigation shell
- Import dialog with RAW file filtering
- Filmstrip thumbnails and preview selection
- Export foundation for JPEG and PNG
- Basic adjustment control surface for exposure, contrast, highlights, shadows, whites, blacks, temperature, tint, vibrance and saturation
- Tone curve, color grading, detail, optics and masking sections in the UI

## Product architecture to build next

### 1. Catalog
- SQLite catalog
- Folders, collections, smart collections
- Ratings, flags, color labels, keywords and face regions
- EXIF/IPTC/XMP metadata
- Sidecar XMP support
- Duplicate detection
- Fast virtual thumbnails and previews

### 2. RAW engine
- LibRaw-backed CR3 / ARW / RAF decoding
- Camera profiles and DCP/ICC support
- White balance and exposure at RAW stage
- Highlight recovery
- Demosaicing quality controls
- Lens metadata and correction profiles
- GPU preview rendering

### 3. Non-destructive develop pipeline
- Exposure, contrast, highlights, shadows, whites, blacks
- Texture, clarity, dehaze
- Vibrance and saturation
- RGB tone curve with parametric and point modes
- HSL / Color Mixer
- Color Grading with shadows, midtones and highlights
- Detail / sharpening / noise reduction
- Lens corrections and transform
- Crop, rotate, straighten and perspective correction
- Vignette and grain
- Copy/paste/sync settings and presets

### 4. Masking
- Brush masks
- Linear and radial gradients
- Luminance range
- Color range
- Subject selection
- Sky selection
- People/object selection
- Mask intersections, subtract and add
- Feather, flow, density and refinement

AI masks should run as a local service where practical, keeping originals and edits on the user's machine.

### 5. Performance
- Decode/render work on background workers
- GPU-accelerated preview compositor
- Proxy previews for large catalogs
- Cache-aware image pyramid
- Cancellation tokens for expensive operations
- Never block the UI thread on RAW decode or AI inference

## Build

Open `Aetherlight.sln` when added, or the project directly in Visual Studio with the .NET 8 SDK and Windows Desktop workload installed.

```powershell
dotnet restore
dotnet build -c Release
```

For a Windows executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## Important

The current UI is the foundation, not the finished Lightroom replacement. The next implementation milestone is the real RAW/catalog/render pipeline. The UI controls should ultimately drive a non-destructive edit stack rather than directly mutate source pixels.
