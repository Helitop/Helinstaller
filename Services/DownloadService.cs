using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Helinstaller.Services
{
    public class DownloadService : IDownloadService
    {
        public async Task DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromHours(3) };
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalRead += bytesRead;

                if (totalBytes.HasValue && progress != null)
                {
                    double percent = (double)totalRead / totalBytes.Value * 100.0;
                    progress.Report(percent);
                }
            }
        }

        public async Task CopyFileAsync(string sourcePath, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            const int bufferSize = 1024 * 1024; // 1 MB
            long totalBytes = new FileInfo(sourcePath).Length;
            long totalRead = 0;

            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
            using var dest = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);

            byte[] buffer = new byte[bufferSize];
            int bytesRead;

            while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await dest.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalRead += bytesRead;

                if (progress != null)
                {
                    double percent = totalRead * 100.0 / totalBytes;
                    progress.Report(percent);
                }
            }
        }

        public async Task<bool> IsDirectDownloadLinkAsync(string url)
        {
            try
            {
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                
                if (response.StatusCode == HttpStatusCode.Found ||
                    response.StatusCode == HttpStatusCode.MovedPermanently ||
                    response.StatusCode == HttpStatusCode.SeeOther)
                {
                    return false;
                }

                var contentType = response.Content.Headers.ContentType;
                if (contentType == null) return false;

                string? type = contentType.MediaType?.ToLower();
                if (type == null) return false;

                return type.Contains("octet-stream") || type.Contains("iso") || type.Contains("application/x-msdownload");
            }
            catch
            {
                return false;
            }
        }
    }
}
