using Circular_belc_qr_generator.Circular_belc_qr_service;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddScoped<ICircularBelcQRCodeGenerationService, CircularQrCodeGenerationService>();
var serviceProvider = services.BuildServiceProvider();

var qrGenService = serviceProvider.GetRequiredService<ICircularBelcQRCodeGenerationService>();

var configuration = new CircularQRCodeDto
{
    DepartmentId = "dept1",
    QrId = "cqr123",
    ShortUrl = "https://www.belc.com/qwertyuiopasdfghjklzxcvbnmqwerty",
    Title = "Demo circular QR",
    Active = true,
    CreatedAt = DateTime.Now,
    UpdatedAt = DateTime.Now,
    LastRequestType = "QR_CREATE",
    
    // QR Code Generation Properties - matching reference image style
    ModuleColor = "black",
    EyeFrameColor = "black",
    EyeFrameLetter = "",          // No letter in finder patterns
    PixelsPerModule = CircularQRCodeDto.BASE_PIXELS_PER_MODULE,
    DotSizeFactor = 0.7,          // Circular dot size (like reference)
    DotSizeVariance = 0,
    EyeFrameMidRadius = 0.8,      // Rounded corners
    EyeFramePupilRadius = 0.6     // Rounded pupil
};
var logoPath = @"C:\MyDevLab\Backend\Circular_belc_qr_generator\Circular_belc_qr_generator\Logos\logo.png";
await qrGenService.GenerateAsync(logoPath, configuration);