namespace Circular_belc_qr_generator.Circular_belc_qr_service
{
    public class CircularQRCodeDto
    {
        public const int BASE_PIXELS_PER_MODULE = 30;
        
        // QR Code Model Properties
        public string DepartmentId { get; set; } = string.Empty;
        public string QrId { get; set; } = string.Empty;
        public string ShortUrl { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public string LastRequestType { get; set; } = string.Empty;
        
        // QR Code Generation Properties
        public string ModuleColor { get; set; } = "blue";
        public string EyeFrameColor { get; set; } = "red";
        public string? EyeFrameLetter { get; set; }
        public int PixelsPerModule { get; set; } = BASE_PIXELS_PER_MODULE;
        public double DotSizeFactor { get; set; } = 0.75;
        public double DotSizeVariance { get; set; } = 0;
        public int? BatchSeed { get; set; }
        public double EyeFrameMidRadius { get; set; } = 0.4;
        public double EyeFramePupilRadius { get; set; } = 0.35;
    }
}
