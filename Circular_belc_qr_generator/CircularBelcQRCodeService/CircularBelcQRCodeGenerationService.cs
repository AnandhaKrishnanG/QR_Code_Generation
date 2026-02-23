using Net.Codecrete.QrCodeGenerator;
using SkiaSharp;
using System.Text;
using System.Xml;

namespace Circular_belc_qr_generator.Circular_belc_qr_service
{
    public class CircularQrCodeGenerationService : ICircularBelcQRCodeGenerationService
    {
        private static readonly int[] RESOLUTIONS = { 240, 360, 480 };
        private static readonly string[] FILE_FORMATS = { "jpeg", "png", "svg" };
        private const string QR_FOLDER_NAME = "qr_codes";
        private const int BORDER_MODULES = 4;
        private const double LOGO_SIZE_RATIO = 0.22; // Center logo as ~22% of QR size so it stays scannable
        private const double DEFAULT_DOT_SIZE_FACTOR = 0.75;

        public async Task GenerateAsync(string? logoPath, CircularQRCodeDto configuration)
        {
            // Ensure required properties are set
            if (string.IsNullOrEmpty(configuration.ShortUrl))
                throw new ArgumentException("ShortUrl is required in configuration", nameof(configuration));
            if (string.IsNullOrEmpty(configuration.QrId))
                throw new ArgumentException("QrId is required in configuration", nameof(configuration));

            // Default options
            int pixelsPerModule = configuration.PixelsPerModule > 0 ? configuration.PixelsPerModule : CircularQRCodeDto.BASE_PIXELS_PER_MODULE;
            SKColor moduleColor = !string.IsNullOrEmpty(configuration.ModuleColor) 
                ? ParseColor(configuration.ModuleColor) 
                : SKColors.Black;
            SKColor eyeFrameColor = !string.IsNullOrEmpty(configuration.EyeFrameColor) 
                ? ParseColor(configuration.EyeFrameColor) 
                : SKColors.Black;
            string? eyeFrameLetter = configuration.EyeFrameLetter;
            double dotSizeFactor = configuration.DotSizeFactor > 0 ? configuration.DotSizeFactor : DEFAULT_DOT_SIZE_FACTOR;
            double dotSizeVariance = configuration.DotSizeVariance;
            int? batchSeed = configuration.BatchSeed;
            double eyeFrameMidRadius = configuration.EyeFrameMidRadius;
            double eyeFramePupilRadius = configuration.EyeFramePupilRadius;
            string? resolvedLogoPath = ResolveLogoPath(logoPath);

            // Ensure output directory exists
            string baseOutputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, QR_FOLDER_NAME);
            Directory.CreateDirectory(baseOutputPath);
            foreach (string format in FILE_FORMATS)
            {
                Directory.CreateDirectory(Path.Combine(baseOutputPath, format));
            }

            // Print base output location
            Console.WriteLine($"QR Code Output Directory: {Path.GetFullPath(baseOutputPath)}");
            if (!string.IsNullOrEmpty(resolvedLogoPath))
                Console.WriteLine($"Using logo: {resolvedLogoPath}");
            Console.WriteLine();

            // Generate base circular QR code bitmap (using High ECC for better scanning)
            using SKBitmap baseBitmap = GenerateCircularQRCodeBitmap(
                content: configuration.ShortUrl,
                pixelsPerModule: pixelsPerModule,
                moduleColor: moduleColor,
                eyeFrameColor: eyeFrameColor,
                eyeFrameLetter: eyeFrameLetter,
                dotSizeFactor: dotSizeFactor,
                dotSizeVariance: dotSizeVariance,
                batchSeed: batchSeed,
                eyeFrameMidRadius: eyeFrameMidRadius,
                eyeFramePupilRadius: eyeFramePupilRadius,
                logoPath: resolvedLogoPath
            );

            // Generate base SVG (once, then resize for each resolution)
            string originalSvgContent = GenerateCircularQRCodeAsSvg(
                content: configuration.ShortUrl,
                pixelsPerModule: pixelsPerModule,
                moduleColor: moduleColor,
                eyeFrameColor: eyeFrameColor,
                eyeFrameLetter: eyeFrameLetter,
                dotSizeFactor: dotSizeFactor,
                dotSizeVariance: dotSizeVariance,
                batchSeed: batchSeed,
                eyeFrameMidRadius: eyeFrameMidRadius,
                eyeFramePupilRadius: eyeFramePupilRadius,
                logoPath: resolvedLogoPath
            );

            // jpeg image
            Console.WriteLine("Generated JPEG files:");
            foreach (int resolution in RESOLUTIONS)
            {
                using SKBitmap resizedImage = ResizeBitmap(baseBitmap, resolution, resolution);
                string fileName = BuildQrCodeName(configuration.QrId, resolution, "jpeg");
                string filePath = Path.Combine(baseOutputPath, "jpeg", fileName);

                using MemoryStream stream = EncodeAsJpeg(resizedImage);
                stream.Position = 0;
                File.WriteAllBytes(filePath, stream.ToArray());
                Console.WriteLine($"  - {Path.GetFullPath(filePath)}");
            }

            // png image
            Console.WriteLine("\nGenerated PNG files:");
            foreach (int resolution in RESOLUTIONS)
            {
                using SKBitmap resizedImage = ResizeBitmap(baseBitmap, resolution, resolution);
                string fileName = BuildQrCodeName(configuration.QrId, resolution, "png");
                string filePath = Path.Combine(baseOutputPath, "png", fileName);

                using MemoryStream stream = EncodeAsPng(resizedImage);
                stream.Position = 0;
                File.WriteAllBytes(filePath, stream.ToArray());
                Console.WriteLine($"  - {Path.GetFullPath(filePath)}");
            }

            // svg image
            Console.WriteLine("\nGenerated SVG files:");
            foreach (int resolution in RESOLUTIONS)
            {
                string resizedSvg = ResizeSVG(originalSvgContent, resolution, resolution);
                string fileName = BuildQrCodeName(configuration.QrId, resolution, "svg");
                string filePath = Path.Combine(baseOutputPath, "svg", fileName);

                File.WriteAllText(filePath, resizedSvg);
                Console.WriteLine($"  - {Path.GetFullPath(filePath)}");
            }

            Console.WriteLine();

            await Task.CompletedTask;
        }

        /// <summary>
        /// Generate circular QR code with Net.Codecrete.QrCodeGenerator + SkiaSharp
        /// </summary>
        private SKBitmap GenerateCircularQRCodeBitmap(
            string content,
            int pixelsPerModule,
            SKColor moduleColor,
            SKColor eyeFrameColor,
            string? eyeFrameLetter,
            double dotSizeFactor,
            double dotSizeVariance,
            int? batchSeed,
            double eyeFrameMidRadius,
            double eyeFramePupilRadius,
            string? logoPath)
        {
            // 1. Generate QR code data using Net.Codecrete.QrCodeGenerator with High ECC
            QrCode qr = QrCode.EncodeText(content, QrCode.Ecc.High);
            int size = qr.Size;

            // 2. Create bitmap with SkiaSharp (matching reference structure)
            int totalSize = size + 2 * BORDER_MODULES;
            int pixelSize = totalSize * pixelsPerModule;
            var bitmap = new SKBitmap(pixelSize, pixelSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);

            int seed = batchSeed ?? 0;
            double baseFactor = Math.Clamp(dotSizeFactor, 0.65, 0.82);
            double variance = Math.Clamp(dotSizeVariance, 0, 0.04);

            var modulePaint = new SKPaint { Color = moduleColor, IsAntialias = true };

            // 3. Draw circular dots for data modules (skip finder patterns)
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Skip finder pattern areas (7×7 corners)
                    if (IsInFinderPattern(x, y, size)) continue;

                    // Check if module is "on"
                    if (!qr.GetModule(x, y)) continue;

                    // Calculate dot size (with optional variance)
                    double factor = baseFactor;
                    if (variance > 0)
                    {
                        int idx = y * size + x;
                        double t = (double)(HashCode.Combine(seed, idx) % 10000) / 10000 - 0.5;
                        factor = baseFactor + t * 2 * variance;
                        factor = Math.Clamp(factor, 0.65, 0.82);
                    }

                    int dotDiameter = (int)(pixelsPerModule * factor);
                    dotDiameter = Math.Clamp(dotDiameter, 1, pixelsPerModule - 1);
                    int o = (pixelsPerModule - dotDiameter) / 2; // offset

                    // Calculate pixel position with border offset
                    int pixelX = (BORDER_MODULES + x) * pixelsPerModule;
                    int pixelY = (BORDER_MODULES + y) * pixelsPerModule;
                    var rect = new SKRect(pixelX + o, pixelY + o, pixelX + o + dotDiameter, pixelY + o + dotDiameter);
                    canvas.DrawOval(rect, modulePaint);
                }
            }

            // 4. Draw custom eye frames at three corners
            int ppm = pixelsPerModule;
            DrawEyeFrame(canvas, (BORDER_MODULES + 0) * ppm, (BORDER_MODULES + 0) * ppm, ppm, eyeFrameColor, eyeFrameLetter, eyeFrameMidRadius, eyeFramePupilRadius);
            DrawEyeFrame(canvas, (BORDER_MODULES + size - 7) * ppm, (BORDER_MODULES + 0) * ppm, ppm, eyeFrameColor, eyeFrameLetter, eyeFrameMidRadius, eyeFramePupilRadius);
            DrawEyeFrame(canvas, (BORDER_MODULES + 0) * ppm, (BORDER_MODULES + size - 7) * ppm, ppm, eyeFrameColor, eyeFrameLetter, eyeFrameMidRadius, eyeFramePupilRadius);

            // 5. Draw center logo if provided
            if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
            {
                DrawCenterLogo(canvas, pixelSize, logoPath);
            }

            return bitmap;
        }

        /// <summary>
        /// Checks if a module position is within a finder pattern (7×7 corner areas)
        /// </summary>
        private static bool IsInFinderPattern(int x, int y, int size)
        {
            // Top-left corner
            if (x < 7 && y < 7) return true;
            // Top-right corner
            if (x >= size - 7 && y < 7) return true;
            // Bottom-left corner
            if (x < 7 && y >= size - 7) return true;
            return false;
        }

        /// <summary>
        /// Draws rounded finder pattern matching the reference style.
        /// Uses rounded rectangles with proper proportions for scanning.
        /// </summary>
        private void DrawEyeFrame(SKCanvas canvas, float leftPx, float topPx, int pixelsPerModule,
                                 SKColor eyeFrameColor, string? eyeFrameLetter,
                                 double eyeFrameMidRadius = 0.4, double eyeFramePupilRadius = 0.35)
        {
            float ppm = pixelsPerModule;
            float outerSize = 7 * ppm;
            float midInset = 1 * ppm;
            float midSize = 5 * ppm;
            float pupilInset = 2 * ppm;
            float pupilSize = 3 * ppm;

            // Rounded corner radii - matching reference image style
            float outerRadius = ppm * 1.0f;      // Outer frame: moderate rounding
            float midRadius = ppm * 0.8f;        // White middle: slightly less rounding
            float pupilRadius = ppm * 0.6f;      // Inner pupil: moderate rounding

            var eyePaint = new SKPaint { Color = eyeFrameColor, IsAntialias = true };
            var whitePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };

            // Outermost: 7x7 rounded rectangle (colored frame)
            canvas.DrawRoundRect(
                new SKRect(leftPx, topPx, leftPx + outerSize, topPx + outerSize),
                outerRadius, outerRadius, eyePaint);

            // Middle: 5x5 white rounded rectangle (creates white border)
            canvas.DrawRoundRect(
                new SKRect(leftPx + midInset, topPx + midInset,
                          leftPx + midInset + midSize, topPx + midInset + midSize),
                midRadius, midRadius, whitePaint);

            // Innermost: 3x3 rounded rectangle (the pupil/center)
            canvas.DrawRoundRect(
                new SKRect(leftPx + pupilInset, topPx + pupilInset,
                          leftPx + pupilInset + pupilSize, topPx + pupilInset + pupilSize),
                pupilRadius, pupilRadius, eyePaint);
        }

        /// <summary>
        /// Draws a center logo on the QR code with a white rounded background
        /// </summary>
        private void DrawCenterLogo(SKCanvas canvas, int pixelSize, string logoPath)
        {
            using var logoBitmap = SKBitmap.Decode(logoPath);
            if (logoBitmap == null) return;

            int logoSize = (int)(pixelSize * LOGO_SIZE_RATIO);
            float x = (pixelSize - logoSize) / 2f;
            float y = (pixelSize - logoSize) / 2f;
            var dest = new SKRect(x, y, x + logoSize, y + logoSize);

            // White rounded rect behind logo so it stands out
            using (var bgPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
                canvas.DrawRoundRect(dest, logoSize * 0.2f, logoSize * 0.2f, bgPaint);

            canvas.DrawBitmap(logoBitmap, new SKRect(0, 0, logoBitmap.Width, logoBitmap.Height), dest, new SKPaint { IsAntialias = true });
        }

        /// <summary>
        /// Generate circular QR code as SVG XML
        /// </summary>
        private string GenerateCircularQRCodeAsSvg(
            string content,
            int pixelsPerModule,
            SKColor moduleColor,
            SKColor eyeFrameColor,
            string? eyeFrameLetter,
            double dotSizeFactor,
            double dotSizeVariance,
            int? batchSeed,
            double eyeFrameMidRadius,
            double eyeFramePupilRadius,
            string? logoPath)
        {
            // 1. Generate QR code data
            QrCode qr = QrCode.EncodeText(content, QrCode.Ecc.High);
            int size = qr.Size;

            // 2. Calculate dimensions (in module units for SVG)
            int totalSize = size + 2 * BORDER_MODULES;

            // 3. Get colors
            string moduleColorHex = ColorToHex(moduleColor);
            string eyeColorHex = ColorToHex(eyeFrameColor);

            // 4. Build SVG in module coordinates for better scalability
            var svg = new StringBuilder();
            svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"{totalSize}\" height=\"{totalSize}\" viewBox=\"0 0 {totalSize} {totalSize}\">");
            svg.Append($"<rect width=\"{totalSize}\" height=\"{totalSize}\" fill=\"white\"/>");

            int seed = batchSeed ?? 0;
            double baseFactor = Math.Clamp(dotSizeFactor, 0.65, 0.82);
            double variance = Math.Clamp(dotSizeVariance, 0, 0.04);

            // 5. Draw circular dots for data modules
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Skip finder pattern areas
                    if (IsInFinderPattern(x, y, size)) continue;

                    if (!qr.GetModule(x, y)) continue;

                    // Calculate dot size factor
                    double factor = baseFactor;
                    if (variance > 0)
                    {
                        int idx = y * size + x;
                        double t = (double)(HashCode.Combine(seed, idx) % 10000) / 10000 - 0.5;
                        factor = baseFactor + t * 2 * variance;
                        factor = Math.Clamp(factor, 0.65, 0.82);
                    }

                    // Calculate position and radius in module coordinates
                    double cx = BORDER_MODULES + x + 0.5;
                    double cy = BORDER_MODULES + y + 0.5;
                    double radius = 0.5 * factor;

                    svg.Append($"<circle cx=\"{cx:F2}\" cy=\"{cy:F2}\" r=\"{radius:F2}\" fill=\"{moduleColorHex}\"/>");
                }
            }

            // 6. Draw custom eye frames
            DrawEyeFrameSvg(svg, BORDER_MODULES + 0, BORDER_MODULES + 0, eyeColorHex, eyeFrameLetter, eyeFrameMidRadius, eyeFramePupilRadius);
            DrawEyeFrameSvg(svg, BORDER_MODULES + size - 7, BORDER_MODULES + 0, eyeColorHex, eyeFrameLetter, eyeFrameMidRadius, eyeFramePupilRadius);
            DrawEyeFrameSvg(svg, BORDER_MODULES + 0, BORDER_MODULES + size - 7, eyeColorHex, eyeFrameLetter, eyeFrameMidRadius, eyeFramePupilRadius);

            // 7. Draw center logo if provided
            if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
            {
                string? logoDataUri = GetImageDataUri(logoPath);
                if (!string.IsNullOrEmpty(logoDataUri))
                {
                    double logoSize = totalSize * LOGO_SIZE_RATIO;
                    double logoX = (totalSize - logoSize) / 2;
                    double logoY = (totalSize - logoSize) / 2;
                    double rx = logoSize * 0.2;
                    svg.Append($"<rect x=\"{logoX:F2}\" y=\"{logoY:F2}\" width=\"{logoSize:F2}\" height=\"{logoSize:F2}\" rx=\"{rx:F2}\" ry=\"{rx:F2}\" fill=\"white\"/>");
                    svg.Append($"<image x=\"{logoX:F2}\" y=\"{logoY:F2}\" width=\"{logoSize:F2}\" height=\"{logoSize:F2}\" href=\"{logoDataUri}\" preserveAspectRatio=\"xMidYMid meet\"/>");
                }
            }

            svg.Append("</svg>");
            return svg.ToString();
        }

        /// <summary>
        /// Draws rounded finder pattern in SVG matching the reference style.
        /// </summary>
        private void DrawEyeFrameSvg(StringBuilder svg, double left, double top,
                                    string eyeFrameColor, string? eyeFrameLetter,
                                    double eyeFrameMidRadius = 0.4, double eyeFramePupilRadius = 0.35)
        {
            double outer = 7;
            double midInset = 1;
            double midSize = 5;
            double pupilInset = 2;
            double pupilSize = 3;

            // Rounded corner radii - matching reference image style (in module units)
            double outerRadius = 1.0;    // Outer frame: moderate rounding
            double midRadius = 0.8;      // White middle: slightly less rounding
            double pupilRadius = 0.6;    // Inner pupil: moderate rounding

            // Outermost: 7x7 rounded rectangle (colored frame)
            svg.Append($"<rect x=\"{left}\" y=\"{top}\" width=\"{outer}\" height=\"{outer}\" rx=\"{outerRadius}\" ry=\"{outerRadius}\" fill=\"{eyeFrameColor}\"/>");

            // Middle: 5x5 white rounded rectangle (creates white border)
            svg.Append($"<rect x=\"{left + midInset}\" y=\"{top + midInset}\" width=\"{midSize}\" height=\"{midSize}\" rx=\"{midRadius}\" ry=\"{midRadius}\" fill=\"white\"/>");

            // Innermost: 3x3 rounded rectangle (the pupil/center)
            svg.Append($"<rect x=\"{left + pupilInset}\" y=\"{top + pupilInset}\" width=\"{pupilSize}\" height=\"{pupilSize}\" rx=\"{pupilRadius}\" ry=\"{pupilRadius}\" fill=\"{eyeFrameColor}\"/>");
        }

        private string? GetImageDataUri(string imagePath)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(imagePath);
                string ext = Path.GetExtension(imagePath).ToLowerInvariant();
                string mime = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    _ => "image/png"
                };
                return "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
            }
            catch
            {
                return null;
            }
        }

        private string ColorToHex(SKColor color)
        {
            return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        }

        private string? ResolveLogoPath(string? logoPath)
        {
            if (string.IsNullOrEmpty(logoPath))
                return null;

            // If path is absolute and exists, return it
            if (Path.IsPathRooted(logoPath) && File.Exists(logoPath))
                return logoPath;

            // Try relative paths from base directory
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] possiblePaths = new[]
            {
                logoPath,
                Path.Combine(baseDir, logoPath),
                Path.Combine(baseDir, "Logos", Path.GetFileName(logoPath)),
                Path.Combine(baseDir, "..", "Logos", Path.GetFileName(logoPath)),
                Path.Combine(baseDir, "..", "..", "Logos", Path.GetFileName(logoPath)),
                Path.Combine(baseDir, "..", "..", "..", "Logos", Path.GetFileName(logoPath)),
                Path.Combine(Directory.GetCurrentDirectory(), logoPath),
                Path.Combine(Directory.GetCurrentDirectory(), "Logos", Path.GetFileName(logoPath))
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }

            return null;
        }

        /// <summary>
        /// Parses a color string to SKColor
        /// </summary>
        public static SKColor ParseColor(string? color)
        {
            if (string.IsNullOrWhiteSpace(color)) return SKColors.Black;
            string c = color.Trim().ToLowerInvariant();

            // Named colors
            return c switch
            {
                "black" => SKColors.Black,
                "white" => SKColors.White,
                "navy" => new SKColor(0x1a, 0x1a, 0x2e),
                "darkgreen" => new SKColor(0x0d, 0x3d, 0x2e),
                "blue" => new SKColor(0x00, 0x00, 0xFF),
                "red" => new SKColor(0xFF, 0x00, 0x00),
                "green" => SKColors.Green,
                "yellow" => SKColors.Yellow,
                "cyan" => SKColors.Cyan,
                "magenta" => SKColors.Magenta,
                "gray" or "grey" => SKColors.Gray,
                _ when c.StartsWith("#") && c.Length >= 7 => ParseHexColor(c),
                _ when c.Length == 6 => ParseHexColor("#" + c),
                _ => SKColors.Black
            };
        }

        private static SKColor ParseHexColor(string hex)
        {
            try
            {
                uint hexValue = Convert.ToUInt32(hex[1..], 16);
                return new SKColor((byte)(hexValue >> 16), (byte)(hexValue >> 8), (byte)hexValue);
            }
            catch
            {
                return SKColors.Black;
            }
        }

        private SKBitmap ResizeBitmap(SKBitmap source, int width, int height)
        {
            var resized = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(resized);
            canvas.Clear(SKColors.White);
            #pragma warning disable CS0618
            canvas.DrawBitmap(source, new SKRect(0, 0, source.Width, source.Height), new SKRect(0, 0, width, height), new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High });
            #pragma warning restore CS0618
            return resized;
        }

        private string BuildQrCodeName(string qrId, int resolution, string format)
        {
            return $"{qrId}_{resolution}.{format}";
        }

        private MemoryStream EncodeAsJpeg(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            var stream = new MemoryStream();
            data.SaveTo(stream);
            data.Dispose();
            return stream;
        }

        private MemoryStream EncodeAsPng(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            var data = image.Encode(SKEncodedImageFormat.Png, 100);
            var stream = new MemoryStream();
            data.SaveTo(stream);
            data.Dispose();
            return stream;
        }

        private string ResizeSVG(string originalSvgContent, int newWidth, int newHeight)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(originalSvgContent);

                XmlElement? root = doc.DocumentElement;
                if (root == null) return originalSvgContent;

                int originalWidth = int.TryParse(root.GetAttribute("width"), out int w) ? w : newWidth;
                int originalHeight = int.TryParse(root.GetAttribute("height"), out int h) ? h : newHeight;

                double scaleX = (double)newWidth / originalWidth;
                double scaleY = (double)newHeight / originalHeight;

                root.SetAttribute("width", newWidth.ToString());
                root.SetAttribute("height", newHeight.ToString());
                root.SetAttribute("viewBox", $"0 0 {newWidth} {newHeight}");

                foreach (XmlElement element in root.GetElementsByTagName("*"))
                {
                    string transform = element.GetAttribute("transform");
                    if (!string.IsNullOrEmpty(transform))
                    {
                        transform += $" scale({scaleX}, {scaleY})";
                    }
                    else
                    {
                        transform = $"scale({scaleX}, {scaleY})";
                    }
                    element.SetAttribute("transform", transform);
                }

                StringBuilder sb = new StringBuilder();
                using (StringWriter stringWriter = new StringWriter(sb))
                {
                    XmlTextWriter xmlTextWriter = new XmlTextWriter(stringWriter)
                    {
                        Formatting = Formatting.Indented
                    };
                    doc.WriteTo(xmlTextWriter);
                    xmlTextWriter.Flush();
                }

                return sb.ToString();
            }
            catch
            {
                return originalSvgContent;
            }
        }
    }
}
