using System;
using System.Threading;
using System.Threading.Tasks;

namespace Helinstaller.Services
{
    public interface IDownloadService
    {
        Task DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
        Task CopyFileAsync(string sourcePath, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> IsDirectDownloadLinkAsync(string url);
    }
}
