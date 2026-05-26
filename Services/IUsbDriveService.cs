using System.Collections.Generic;
using System.Threading.Tasks;
using Helinstaller.Models;

namespace Helinstaller.Services
{
    public interface IUsbDriveService
    {
        Task<List<UsbDriveItem>> GetUsbDrivesAsync();
        Task<bool> FormatDriveAsync(UsbDriveItem drive);
        string? GetDiskNumber(string driveLetter);
    }
}
