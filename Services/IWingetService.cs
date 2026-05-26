using System.Collections.Generic;
using System.Threading.Tasks;
using WGetNET;

namespace Helinstaller.Services
{
    public interface IWingetService
    {
        Task<List<WinGetPackage>> SearchPackageAsync(string query);
        Task<List<WinGetPackage>> GetInstalledPackagesAsync();
        Task<bool> InstallPackageAsync(string packageId, System.IProgress<string>? progress = null, System.IProgress<double>? percentProgress = null, bool force = false);
    }
}
