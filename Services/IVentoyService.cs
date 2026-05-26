using System.Threading.Tasks;
using Helinstaller.Models;

namespace Helinstaller.Services
{
    public interface IVentoyService
    {
        bool IsVentoyInstalled(UsbDriveItem drive);
        Task<bool> InstallOrUpdateVentoyAsync(UsbDriveItem drive, bool install);
        Task<bool> CopyAutounattendAsync(string driveRootPath);
        Task<bool> InjectOobeAutoAsync(string usbRootPath);
    }
}
