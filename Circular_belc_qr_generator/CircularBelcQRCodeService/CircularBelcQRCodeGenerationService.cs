using Net.Codecrete.QrCodeGenerator;
using SkiaSharp;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Circular_belc_qr_generator.Circular_belc_qr_service
{
    public class CircularQrCodeGenerationService : ICircularBelcQRCodeGenerationService
    {
        // Output configuration
        private static readonly int[] RESOLUTIONS = { 240, 360, 480 };
        private static readonly string[] FILE_FORMATS = { "jpeg", "png", "svg" };
        private const string QR_FOLDER_NAME = "qr_codes";
        
        // QR Code structure constants
        private const int BORDER_MODULES = 4;
        private const int FINDER_PATTERN_SIZE = 7; // 7x7 finder pattern
        private const double LOGO_SIZE_RATIO = 0.22; // Center logo as ~22% of QR size so it stays scannable
        private const double DEFAULT_DOT_SIZE_FACTOR = 0.75;
        private const double MIN_DOT_SIZE_FACTOR = 0.65;
        private const double MAX_DOT_SIZE_FACTOR = 0.82;
        private const double MAX_DOT_SIZE_VARIANCE = 0.04;
        
        // Text rendering constants
        private const int TEXT_PADDING_MULTIPLIER = 3; // Padding between QR and text
        private const int TEXT_HEIGHT_MULTIPLIER = 7; // Height for text area
        private const float TEXT_FONT_SIZE_MULTIPLIER = 2.0f;
        private const float TEXT_MIN_FONT_SIZE = 30f;
        private const float TEXT_MAX_FONT_SIZE = 60f;
        private const float TEXT_LINE_HEIGHT_MULTIPLIER = 1.4f;
        private const float TEXT_MAX_WIDTH_RATIO = 0.9f; // 90% of QR width
        private const float TEXT_BG_PADDING_RATIO = 0.5f;
        
        // Eye frame constants
        private const float EYE_FRAME_OUTER_RADIUS_RATIO = 1.0f;
        private const float EYE_FRAME_MID_RADIUS_RATIO = 0.8f;
        private const float EYE_FRAME_PUPIL_RADIUS_RATIO = 0.6f;
        private const int EYE_FRAME_OUTER_SIZE = 7;
        private const int EYE_FRAME_MID_INSET = 1;
        private const int EYE_FRAME_MID_SIZE = 5;
        private const int EYE_FRAME_PUPIL_INSET = 2;
        private const int EYE_FRAME_PUPIL_SIZE = 3;
        
        // SVG constants
        private const double SVG_TEXT_PADDING = 3.0;
        private const double SVG_TEXT_HEIGHT = 6.0;
        private const double SVG_FONT_SIZE = 1.2;
        private const double SVG_LINE_HEIGHT_MULTIPLIER = 1.4;
        private const double SVG_TEXT_MAX_WIDTH_RATIO = 0.85;
        private const double SVG_BG_PADDING = 0.5;
        private const double SVG_CHAR_WIDTH_ESTIMATE = 0.6;

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
                dotSizeFactor: dotSizeFactor,
                dotSizeVariance: dotSizeVariance,
                batchSeed: batchSeed,
                eyeFrameMidRadius: eyeFrameMidRadius,
                eyeFramePupilRadius: eyeFramePupilRadius,
                logoPath: resolvedLogoPath
            );

            // Calculate aspect ratios
            double aspectRatio = (double)baseBitmap.Height / baseBitmap.Width;
            double svgAspectRatio = CalculateSvgAspectRatio(originalSvgContent, aspectRatio);
            
            // Generate files for each format and resolution
            GenerateBitmapFiles(baseBitmap, baseOutputPath, configuration.QrId, aspectRatio, "jpeg", EncodeAsJpeg);
            GenerateBitmapFiles(baseBitmap, baseOutputPath, configuration.QrId, aspectRatio, "png", EncodeAsPng);
            GenerateSvgFiles(originalSvgContent, baseOutputPath, configuration.QrId, svgAspectRatio);

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
            
            // Calculate text height and padding
            int textPadding = pixelsPerModule * TEXT_PADDING_MULTIPLIER;
            int textHeight = pixelsPerModule * TEXT_HEIGHT_MULTIPLIER;
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
                        factor = Math.Clamp(factor, MIN_DOT_SIZE_FACTOR, MAX_DOT_SIZE_FACTOR);
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
            int eyeFrameOffset = (BORDER_MODULES * ppm);
            int eyeFrameSize = FINDER_PATTERN_SIZE * ppm;
            DrawEyeFrame(canvas, eyeFrameOffset, eyeFrameOffset, ppm, eyeFrameColor, eyeFrameMidRadius, eyeFramePupilRadius);
            DrawEyeFrame(canvas, eyeFrameOffset + (size - FINDER_PATTERN_SIZE) * ppm, eyeFrameOffset, ppm, eyeFrameColor, eyeFrameMidRadius, eyeFramePupilRadius);
            DrawEyeFrame(canvas, eyeFrameOffset, eyeFrameOffset + (size - FINDER_PATTERN_SIZE) * ppm, ppm, eyeFrameColor, eyeFrameMidRadius, eyeFramePupilRadius);

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
            if (x < FINDER_PATTERN_SIZE && y < FINDER_PATTERN_SIZE) return true;
            // Top-right corner
            if (x >= size - FINDER_PATTERN_SIZE && y < FINDER_PATTERN_SIZE) return true;
            // Bottom-left corner
            if (x < FINDER_PATTERN_SIZE && y >= size - FINDER_PATTERN_SIZE) return true;
            return false;
        }

        /// <summary>
        /// Draws rounded finder pattern matching the reference style.
        /// Uses rounded rectangles with proper proportions for scanning.
        /// </summary>
        private void DrawEyeFrame(SKCanvas canvas, float leftPx, float topPx, int pixelsPerModule,
                                 SKColor eyeFrameColor,
                                 double eyeFrameMidRadius = 0.4, double eyeFramePupilRadius = 0.35)
        {
            float ppm = pixelsPerModule;
            float outerSize = EYE_FRAME_OUTER_SIZE * ppm;
            float midInset = EYE_FRAME_MID_INSET * ppm;
            float midSize = EYE_FRAME_MID_SIZE * ppm;
            float pupilInset = EYE_FRAME_PUPIL_INSET * ppm;
            float pupilSize = EYE_FRAME_PUPIL_SIZE * ppm;

            // Rounded corner radii
            float outerRadius = ppm * EYE_FRAME_OUTER_RADIUS_RATIO;
            float midRadius = ppm * EYE_FRAME_MID_RADIUS_RATIO;
            float pupilRadius = ppm * EYE_FRAME_PUPIL_RADIUS_RATIO;

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
        /// Wraps a URL into multiple lines based on maximum width, preserving URL structure
        /// </summary>
        private static List<string> WrapUrl(string url, Func<string, float> measureText, float maxWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(url)) return lines;

            float textWidth = measureText(url);
            if (textWidth <= maxWidth)
            {
                lines.Add(url);
                return lines;
            }

            string currentLine = "";
            int protocolIndex = url.IndexOf("://");

            if (protocolIndex > 0)
            {
                // Handle URL with protocol (e.g., "https://")
                int nextSlash = url.IndexOf('/', protocolIndex + 3);
                if (nextSlash < 0)
                {
                    lines.Add(url);
                    return lines;
                }

                currentLine = url.Substring(0, nextSlash);
                string remaining = url.Substring(nextSlash + 1);
                string[] pathParts = remaining.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string part in pathParts)
                {
                    string testLine = currentLine + "/" + part;
                    if (measureText(testLine) > maxWidth && !string.IsNullOrEmpty(currentLine))
                    {
                        lines.Add(currentLine);
                        currentLine = "/" + part;
                    }
                    else
                    {
                        currentLine = testLine;
                    }
                }
            }
            else
            {
                // No protocol, split by "/"
                string[] parts = url.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                bool isFirst = true;

                foreach (string part in parts)
                {
                    string testLine = isFirst ? part : currentLine + "/" + part;
                    if (measureText(testLine) > maxWidth && !string.IsNullOrEmpty(currentLine))
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

            return lines;
        }

        /// <summary>
        /// Draws the URL text below the QR code with improved readability
        /// </summary>
        private void DrawUrlText(SKCanvas canvas, string url, int qrSize, int totalHeight, int textPadding, int pixelsPerModule)
        {
            if (string.IsNullOrEmpty(url)) return;

            // Calculate font size with constraints for readability
            float fontSize = Math.Clamp(pixelsPerModule * TEXT_FONT_SIZE_MULTIPLIER, TEXT_MIN_FONT_SIZE, TEXT_MAX_FONT_SIZE);

            using var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = fontSize,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                SubpixelText = true
            };

            // Calculate text area dimensions
            float maxWidth = qrSize * TEXT_MAX_WIDTH_RATIO;
            float lineHeight = fontSize * TEXT_LINE_HEIGHT_MULTIPLIER;
            float textAreaTop = qrSize + textPadding;
            float textAreaHeight = totalHeight - textAreaTop;
            float textX = qrSize / 2f;

            // Draw white background rectangle for better contrast
            float bgPadding = pixelsPerModule * TEXT_BG_PADDING_RATIO;
            using var bgPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(new SKRect(
                qrSize * 0.05f,
                textAreaTop - bgPadding,
                qrSize * 0.95f,
                totalHeight - bgPadding),
                bgPaint);

            // Wrap URL into lines
            List<string> lines = WrapUrl(url, text => textPaint.MeasureText(text), maxWidth);

            // Draw text lines
            if (lines.Count > 1)
            {
                // Multiple lines: center vertically
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
                // Single line: center vertically
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
            
            // Calculate text height and padding (in module units)
            double textPadding = SVG_TEXT_PADDING;
            double textHeight = SVG_TEXT_HEIGHT;
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
                        factor = Math.Clamp(factor, MIN_DOT_SIZE_FACTOR, MAX_DOT_SIZE_FACTOR);
                    }

                    // Calculate position and radius in module coordinates
                    double cx = BORDER_MODULES + x + 0.5;
                    double cy = BORDER_MODULES + y + 0.5;
                    double radius = 0.5 * factor;

                    svg.Append($"<circle cx=\"{cx:F2}\" cy=\"{cy:F2}\" r=\"{radius:F2}\" fill=\"{moduleColorHex}\"/>");
                }
            }

            // 6. Draw custom eye frames
            DrawEyeFrameSvg(svg, BORDER_MODULES, BORDER_MODULES, eyeColorHex, eyeFrameMidRadius, eyeFramePupilRadius);
            DrawEyeFrameSvg(svg, BORDER_MODULES + size - FINDER_PATTERN_SIZE, BORDER_MODULES, eyeColorHex, eyeFrameMidRadius, eyeFramePupilRadius);
            DrawEyeFrameSvg(svg, BORDER_MODULES, BORDER_MODULES + size - FINDER_PATTERN_SIZE, eyeColorHex, eyeFrameMidRadius, eyeFramePupilRadius);

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
                                    string eyeFrameColor,
                                    double eyeFrameMidRadius = 0.4, double eyeFramePupilRadius = 0.35)
        {
            double outer = EYE_FRAME_OUTER_SIZE;
            double midInset = EYE_FRAME_MID_INSET;
            double midSize = EYE_FRAME_MID_SIZE;
            double pupilInset = EYE_FRAME_PUPIL_INSET;
            double pupilSize = EYE_FRAME_PUPIL_SIZE;

            // Rounded corner radii (in module units)
            double outerRadius = EYE_FRAME_OUTER_RADIUS_RATIO;
            double midRadius = EYE_FRAME_MID_RADIUS_RATIO;
            double pupilRadius = EYE_FRAME_PUPIL_RADIUS_RATIO;

            // Outermost: 7x7 rounded rectangle (colored frame)
            svg.Append($"<rect x=\"{left}\" y=\"{top}\" width=\"{outer}\" height=\"{outer}\" rx=\"{outerRadius}\" ry=\"{outerRadius}\" fill=\"{eyeFrameColor}\"/>");

            // Middle: 5x5 white rounded rectangle (creates white border)
            svg.Append($"<rect x=\"{left + midInset}\" y=\"{top + midInset}\" width=\"{midSize}\" height=\"{midSize}\" rx=\"{midRadius}\" ry=\"{midRadius}\" fill=\"white\"/>");

            // Innermost: 3x3 rounded rectangle (the pupil/center)
            svg.Append($"<rect x=\"{left + pupilInset}\" y=\"{top + pupilInset}\" width=\"{pupilSize}\" height=\"{pupilSize}\" rx=\"{pupilRadius}\" ry=\"{pupilRadius}\" fill=\"{eyeFrameColor}\"/>");
        }

        /// <summary>
        /// Escapes XML special characters
        /// </summary>
        private static string EscapeXml(string text)
        {
            return text.Replace("&", "&amp;")
                      .Replace("<", "&lt;")
                      .Replace(">", "&gt;")
                      .Replace("\"", "&quot;")
                      .Replace("'", "&apos;");
        }

        /// <summary>
        /// Wraps a URL for SVG based on character count estimation
        /// </summary>
        private static List<string> WrapUrlForSvg(string url, double maxWidth, double fontSize)
        {
            float estimatedCharsPerLine = (float)(maxWidth / (fontSize * SVG_CHAR_WIDTH_ESTIMATE));
            return WrapUrl(url, text => (float)text.Length, estimatedCharsPerLine);
        }

        /// <summary>
        /// Draws the URL text below the QR code in SVG with improved readability
        /// </summary>
        private void DrawUrlTextSvg(StringBuilder svg, string url, double qrSize, double totalHeight, double textPadding)
        {
            if (string.IsNullOrEmpty(url)) return;

            // Calculate font size and dimensions
            double fontSize = SVG_FONT_SIZE;
            double lineHeight = fontSize * SVG_LINE_HEIGHT_MULTIPLIER;
            double textAreaTop = qrSize + textPadding;
            double textAreaHeight = totalHeight - textAreaTop;
            double textX = qrSize / 2.0;
            double maxWidth = qrSize * SVG_TEXT_MAX_WIDTH_RATIO;

            // Draw white background rectangle
            svg.Append($"<rect x=\"{qrSize * 0.05:F2}\" y=\"{textAreaTop - SVG_BG_PADDING:F2}\" width=\"{qrSize * 0.9:F2}\" height=\"{textAreaHeight:F2}\" fill=\"white\" rx=\"0.3\" ry=\"0.3\"/>");

            // Wrap URL into lines
            List<string> lines = WrapUrlForSvg(url, maxWidth, fontSize);

            // Draw text lines
            if (lines.Count > 1)
            {
                // Multiple lines: center vertically
                double totalTextHeight = lines.Count * lineHeight;
                double startY = textAreaTop + (textAreaHeight - totalTextHeight) / 2.0 + fontSize;

                foreach (string line in lines)
                {
                    svg.Append($"<text x=\"{textX:F2}\" y=\"{startY:F2}\" font-family=\"Arial, sans-serif\" font-size=\"{fontSize:F2}\" font-weight=\"bold\" fill=\"black\" text-anchor=\"middle\">{EscapeXml(line)}</text>");
                    startY += lineHeight;
                }
            }
            else
            {
                // Single line: center vertically
                double textY = textAreaTop + (textAreaHeight / 2.0) + (fontSize / 3.0);
                svg.Append($"<text x=\"{textX:F2}\" y=\"{textY:F2}\" font-family=\"Arial, sans-serif\" font-size=\"{fontSize:F2}\" font-weight=\"bold\" fill=\"black\" text-anchor=\"middle\">{EscapeXml(url)}</text>");
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

        /// <summary>
        /// Calculates SVG aspect ratio from SVG content, with fallback to bitmap aspect ratio
        /// </summary>
        private static double CalculateSvgAspectRatio(string svgContent, double fallbackAspectRatio)
        {
            try
            {
                var svgDoc = new XmlDocument();
                svgDoc.LoadXml(svgContent);
                var svgRoot = svgDoc.DocumentElement;
                if (svgRoot != null &&
                    double.TryParse(svgRoot.GetAttribute("width"), out double svgWidth) &&
                    double.TryParse(svgRoot.GetAttribute("height"), out double svgHeight) &&
                    svgWidth > 0)
                {
                    return svgHeight / svgWidth;
                }
            }
            catch
            {
                // Fall through to return fallback
            }
            return fallbackAspectRatio;
        }

        /// <summary>
        /// Generates bitmap files (JPEG/PNG) for all resolutions
        /// </summary>
        private void GenerateBitmapFiles(SKBitmap baseBitmap, string baseOutputPath, string qrId, 
            double aspectRatio, string format, Func<SKBitmap, MemoryStream> encoder)
        {
            Console.WriteLine($"Generated {format.ToUpper()} files:");
            foreach (int resolution in RESOLUTIONS)
            {
                int height = (int)(resolution * aspectRatio);
                using SKBitmap resizedImage = ResizeBitmap(baseBitmap, resolution, height);
                string fileName = BuildQrCodeName(qrId, resolution, format);
                string filePath = Path.Combine(baseOutputPath, format, fileName);

                using MemoryStream stream = encoder(resizedImage);
                stream.Position = 0;
                File.WriteAllBytes(filePath, stream.ToArray());
                Console.WriteLine($"  - {Path.GetFullPath(filePath)}");
            }
        }

        /// <summary>
        /// Generates SVG files for all resolutions
        /// </summary>
        private void GenerateSvgFiles(string originalSvgContent, string baseOutputPath, string qrId, double svgAspectRatio)
        {
            Console.WriteLine("\nGenerated SVG files:");
            foreach (int resolution in RESOLUTIONS)
            {
                int height = (int)(resolution * svgAspectRatio);
                string resizedSvg = ResizeSVG(originalSvgContent, resolution, height);
                string fileName = BuildQrCodeName(qrId, resolution, "svg");
                string filePath = Path.Combine(baseOutputPath, "svg", fileName);

                File.WriteAllText(filePath, resizedSvg);
                Console.WriteLine($"  - {Path.GetFullPath(filePath)}");
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
