using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Circular_belc_qr_generator.Circular_belc_qr_service
{
    public interface ICircularBelcQRCodeGenerationService
    {
        Task GenerateAsync(string? logoPath, CircularQRCodeDto configuration);
    }
}
