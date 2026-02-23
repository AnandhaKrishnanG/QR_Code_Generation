using Net.Codecrete.QrCodeGenerator;
using SkiaSharp;
using System.Collections.Generic;
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

            // Calculate aspect ratio for proper resizing
            double aspectRatio = (double)baseBitmap.Height / baseBitmap.Width;
            
            // Parse SVG to get its aspect ratio (SVG uses module units, not pixels)
            double svgAspectRatio = aspectRatio; // Default to bitmap aspect ratio
            try
            {
                XmlDocument svgDoc = new XmlDocument();
                svgDoc.LoadXml(originalSvgContent);
                XmlElement? svgRoot = svgDoc.DocumentElement;
                if (svgRoot != null)
                {
                    if (double.TryParse(svgRoot.GetAttribute("width"), out double svgWidth) &&
                        double.TryParse(svgRoot.GetAttribute("height"), out double svgHeight) &&
                        svgWidth > 0)
                    {
                        svgAspectRatio = svgHeight / svgWidth;
                    }
                }
            }
            catch
            {
                // Use bitmap aspect ratio as fallback
            }
            
            // jpeg image
            Console.WriteLine("Generated JPEG files:");
            foreach (int resolution in RESOLUTIONS)
            {
                int height = (int)(resolution * aspectRatio);
                using SKBitmap resizedImage = ResizeBitmap(baseBitmap, resolution, height);
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
                int height = (int)(resolution * aspectRatio);
                using SKBitmap resizedImage = ResizeBitmap(baseBitmap, resolution, height);
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
                // SVG dimensions are in pixels when rendered, so use the same aspect ratio
                int height = (int)(resolution * svgAspectRatio);
                string resizedSvg = ResizeSVG(originalSvgContent, resolution, height);
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
            
            // Calculate text height and padding (increased for better readability)
            // Increased to accommodate larger font size for better readability at 240p
            int textPadding = pixelsPerModule * 3; // Padding between QR and text
            int textHeight = pixelsPerModule * 7; // Height for text (increased from 6 to 7 for larger font)
            int totalHeight = pixelSize + textPadding + textHeight;
            
            var bitmap = new SKBitmap(pixelSize, totalHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
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

            // 6. Draw URL text below the QR code
            DrawUrlText(canvas, content, pixelSize, totalHeight, textPadding, pixelsPerModule);

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
        /// Draws the URL text below the QR code with improved readability
        /// </summary>
        private void DrawUrlText(SKCanvas canvas, string url, int qrSize, int totalHeight, int textPadding, int pixelsPerModule)
        {
            if (string.IsNullOrEmpty(url)) return;

            // Calculate text size based on QR code size (larger for better readability)
            // Use a larger multiplier and higher minimum to ensure readability when scaled to 240p
            // Base size is typically 30 pixels per module, so we want font that scales well
            // When scaled to 240p (scale factor ~0.24-0.27), we need base font of ~30-35px to get ~8-9px at 240p
            float fontSize = pixelsPerModule * 2.0f; // Increased to 2.0f for better scaling
            fontSize = Math.Max(30, Math.Min(fontSize, 60)); // Increased minimum to 30px, max to 60px for better readability

            using var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = fontSize,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), // Use bold for better readability
                SubpixelText = true // Better text rendering
            };

            // Measure text to handle long URLs
            float textWidth = textPaint.MeasureText(url);
            float maxWidth = qrSize * 0.9f; // Use 90% of QR width with some margin

            // Calculate text area dimensions
            float lineHeight = fontSize * 1.4f;
            float textAreaTop = qrSize + textPadding;
            float textAreaBottom = totalHeight;
            float textAreaHeight = textAreaBottom - textAreaTop;
            float textX = qrSize / 2f;

            // Draw white background rectangle for better contrast
            using var bgPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            float bgPadding = pixelsPerModule * 0.5f;
            canvas.DrawRect(new SKRect(
                qrSize * 0.05f, 
                textAreaTop - bgPadding, 
                qrSize * 0.95f, 
                textAreaBottom - bgPadding), 
                bgPaint);

            // If text is too long, wrap to multiple lines
            if (textWidth > maxWidth)
            {
                // Break URL into parts for better wrapping, preserving separators
                List<string> lines = new List<string>();
                
                // Use a smarter approach: split by "/" but keep track of separators
                // First, handle the protocol part (e.g., "https://")
                string currentLine = "";
                int protocolIndex = url.IndexOf("://");
                
                if (protocolIndex > 0)
                {
                    // Add protocol and domain together
                    int nextSlash = url.IndexOf('/', protocolIndex + 3);
                    if (nextSlash < 0)
                    {
                        // No path after domain
                        currentLine = url;
                    }
                    else
                    {
                        // Get protocol + domain (e.g., "https://www.belc.com")
                        currentLine = url.Substring(0, nextSlash);
                        string remaining = url.Substring(nextSlash + 1); // Get path after the first "/"
                        
                        // Now process the path parts, preserving "/" separators
                        string[] pathParts = remaining.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        foreach (string part in pathParts)
                        {
                            string testLine = currentLine + "/" + part;
                            float testWidth = textPaint.MeasureText(testLine);
                            
                            if (testWidth > maxWidth && !string.IsNullOrEmpty(currentLine))
                            {
                                lines.Add(currentLine);
                                // Start new line with "/" prefix to preserve the separator
                                currentLine = "/" + part;
                            }
                            else
                            {
                                currentLine = testLine;
                            }
                        }
                    }
                }
                else
                {
                    // No protocol, just split by "/" and preserve separators
                    string[] parts = url.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    bool isFirst = true;
                    
                    foreach (string part in parts)
                    {
                        string testLine = isFirst ? part : currentLine + "/" + part;
                        float testWidth = textPaint.MeasureText(testLine);
                        
                        if (testWidth > maxWidth && !string.IsNullOrEmpty(currentLine))
                        {
                            lines.Add(currentLine);
                            currentLine = part;
                        }
                        else
                        {
                            currentLine = testLine;
                        }
                        isFirst = false;
                    }
                }
                
                if (!string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                }

                // Draw all lines, centered vertically in the text area
                float totalTextHeight = lines.Count * lineHeight;
                float startY = textAreaTop + (textAreaHeight - totalTextHeight) / 2f + fontSize;
                
                foreach (string line in lines)
                {
                    canvas.DrawText(line, textX, startY, textPaint);
                    startY += lineHeight;
                }
            }
            else
            {
                // Text fits on one line - center it vertically
                float textY = textAreaTop + (textAreaHeight / 2f) + (fontSize / 3f);
                canvas.DrawText(url, textX, textY, textPaint);
            }
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
            
            // Calculate text height and padding (convert pixels to module units) - increased for better readability
            double textPadding = 3.0; // Padding between QR and text in module units
            double textHeight = 6.0; // Height for text in module units (allows for larger font and multiple lines)
            double totalHeight = totalSize + textPadding + textHeight;

            // 3. Get colors
            string moduleColorHex = ColorToHex(moduleColor);
            string eyeColorHex = ColorToHex(eyeFrameColor);

            // 4. Build SVG in module coordinates for better scalability
            var svg = new StringBuilder();
            svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"{totalSize}\" height=\"{totalHeight}\" viewBox=\"0 0 {totalSize} {totalHeight}\">");
            svg.Append($"<rect width=\"{totalSize}\" height=\"{totalHeight}\" fill=\"white\"/>");

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

            // 8. Draw URL text below the QR code
            DrawUrlTextSvg(svg, content, totalSize, totalHeight, textPadding);

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

        /// <summary>
        /// Draws the URL text below the QR code in SVG with improved readability
        /// </summary>
        private void DrawUrlTextSvg(StringBuilder svg, string url, double qrSize, double totalHeight, double textPadding)
        {
            if (string.IsNullOrEmpty(url)) return;

            // Escape XML special characters
            string escapedUrl = url.Replace("&", "&amp;")
                                   .Replace("<", "&lt;")
                                   .Replace(">", "&gt;")
                                   .Replace("\"", "&quot;")
                                   .Replace("'", "&apos;");

            // Calculate font size (in module units, will scale with viewBox) - larger for better readability
            double fontSize = 1.2;
            double lineHeight = fontSize * 1.4;
            
            // Calculate text area
            double textAreaTop = qrSize + textPadding;
            double textAreaHeight = totalHeight - textAreaTop;
            double textX = qrSize / 2.0;

            // Draw white background rectangle for better contrast
            double bgPadding = 0.5;
            svg.Append($"<rect x=\"{qrSize * 0.05:F2}\" y=\"{textAreaTop - bgPadding:F2}\" width=\"{qrSize * 0.9:F2}\" height=\"{textAreaHeight:F2}\" fill=\"white\" rx=\"0.3\" ry=\"0.3\"/>");

            // Measure text width to determine if wrapping is needed
            // For SVG, we'll use a simple approach: if URL is very long, break it
            double maxWidth = qrSize * 0.85;
            int estimatedCharsPerLine = (int)(maxWidth / (fontSize * 0.6)); // Rough estimate
            
            if (url.Length > estimatedCharsPerLine)
            {
                // Break into multiple lines, preserving separators
                List<string> lines = new List<string>();
                
                // Use the same smart approach as bitmap version
                string currentLine = "";
                int protocolIndex = url.IndexOf("://");
                
                if (protocolIndex > 0)
                {
                    // Add protocol and domain together
                    int nextSlash = url.IndexOf('/', protocolIndex + 3);
                    if (nextSlash < 0)
                    {
                        // No path after domain
                        currentLine = url;
                    }
                    else
                    {
                        // Get protocol + domain (e.g., "https://www.belc.com")
                        currentLine = url.Substring(0, nextSlash);
                        string remaining = url.Substring(nextSlash + 1); // Get path after the first "/"
                        
                        // Now process the path parts, preserving "/" separators
                        string[] pathParts = remaining.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        foreach (string part in pathParts)
                        {
                            string testLine = currentLine + "/" + part;
                            
                            if (testLine.Length > estimatedCharsPerLine && !string.IsNullOrEmpty(currentLine))
                            {
                                lines.Add(currentLine);
                                // Start new line with "/" prefix to preserve the separator
                                currentLine = "/" + part;
                            }
                            else
                            {
                                currentLine = testLine;
                            }
                        }
                    }
                }
                else
                {
                    // No protocol, just split by "/" and preserve separators
                    string[] parts = url.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    bool isFirst = true;
                    
                    foreach (string part in parts)
                    {
                        string testLine = isFirst ? part : currentLine + "/" + part;
                        
                        if (testLine.Length > estimatedCharsPerLine && !string.IsNullOrEmpty(currentLine))
                        {
                            lines.Add(currentLine);
                            currentLine = part;
                        }
                        else
                        {
                            currentLine = testLine;
                        }
                        isFirst = false;
                    }
                }
                
                if (!string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                }

                // Draw all lines, centered vertically
                double totalTextHeight = lines.Count * lineHeight;
                double startY = textAreaTop + (textAreaHeight - totalTextHeight) / 2.0 + fontSize;
                
                foreach (string line in lines)
                {
                    string escapedLine = line.Replace("&", "&amp;")
                                             .Replace("<", "&lt;")
                                             .Replace(">", "&gt;")
                                             .Replace("\"", "&quot;")
                                             .Replace("'", "&apos;");
                    svg.Append($"<text x=\"{textX:F2}\" y=\"{startY:F2}\" font-family=\"Arial, sans-serif\" font-size=\"{fontSize:F2}\" font-weight=\"bold\" fill=\"black\" text-anchor=\"middle\">{escapedLine}</text>");
                    startY += lineHeight;
                }
            }
            else
            {
                // Text fits on one line - center it vertically
                double textY = textAreaTop + (textAreaHeight / 2.0) + (fontSize / 3.0);
                svg.Append($"<text x=\"{textX:F2}\" y=\"{textY:F2}\" font-family=\"Arial, sans-serif\" font-size=\"{fontSize:F2}\" font-weight=\"bold\" fill=\"black\" text-anchor=\"middle\">{escapedUrl}</text>");
            }
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
