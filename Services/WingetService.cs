using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using WGetNET;

namespace Helinstaller.Services
{
    public class WingetService : IWingetService
    {
        private readonly WinGetPackageManager _packageManager;

        public WingetService()
        {
            _packageManager = new WinGetPackageManager();
        }

        public async Task<List<WinGetPackage>> SearchPackageAsync(string query)
        {
            return await _packageManager.SearchPackageAsync(query);
        }

        public async Task<List<WinGetPackage>> GetInstalledPackagesAsync()
        {
            return await _packageManager.GetInstalledPackagesAsync();
        }
        private static double ToBytes(double value, string unit)
        {
            return unit switch
            {
                "KB" => value * 1024,
                "MB" => value * 1024 * 1024,
                "GB" => value * 1024 * 1024 * 1024,
                "TB" => value * 1024 * 1024 * 1024 * 1024,
                _ => value // Байт (B) или неопределенный
            };
        }
        public async Task<bool> InstallPackageAsync(string packageId, IProgress<string>? progress = null, IProgress<double>? percentProgress = null, bool force = false)
        {
            try
            {
                string args = $"install --id {packageId} --silent --accept-package-agreements --accept-source-agreements";
                if (force)
                {
                    args += " --force";
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                // Читаем вывод посимвольно для перехвата \r и \n
                _ = Task.Run(async () =>
                {
                    var reader = process.StandardOutput;
                    var charBuffer = new char[1024];
                    var lineBuilder = new StringBuilder();

                    while (!reader.EndOfStream)
                    {
                        int readCount = await reader.ReadAsync(charBuffer, 0, charBuffer.Length);
                        if (readCount == 0) break;

                        for (int i = 0; i < readCount; i++)
                        {
                            char c = charBuffer[i];
                            if (c == '\r' || c == '\n')
                            {
                                string line = lineBuilder.ToString().Trim();
                                if (!string.IsNullOrEmpty(line))
                                {
                                    // 1. Отправляем строку в текстовый прогресс
                                    progress?.Report(line);

                                    // 2. Если строка содержит дробь '/', делим её на две части (текущий размер и общий)
                                    if (line.Contains("/"))
                                    {
                                        var parts = line.Split('/');
                                        if (parts.Length == 2)
                                        {
                                            // Ищем число и единицу во 2-й части (общий размер): " 41.2 MB"
                                            var matchTotal = Regex.Match(parts[1], @"(?<val>\d+(?:\.\d+)?)\s*(?<unit>[KMGT]?B)", RegexOptions.IgnoreCase);

                                            // Ищем число и единицу в 1-й части, начиная СПРАВА НАЛЕВО (чтобы игнорировать мусор из блоков ██▒▒ в начале): "  1024 KB "
                                            var matchCurr = Regex.Match(parts[0], @"(?<val>\d+(?:\.\d+)?)\s*(?<unit>[KMGT]?B)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

                                            if (matchCurr.Success && matchTotal.Success)
                                            {
                                                if (double.TryParse(matchCurr.Groups["val"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double curr) &&
                                                    double.TryParse(matchTotal.Groups["val"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double total))
                                                {
                                                    string currUnit = matchCurr.Groups["unit"].Value.ToUpper();
                                                    string totalUnit = matchTotal.Groups["unit"].Value.ToUpper();

                                                    double currBytes = ToBytes(curr, currUnit);
                                                    double totalBytes = ToBytes(total, totalUnit);

                                                    if (totalBytes > 0)
                                                    {
                                                        double pct = Math.Clamp((currBytes / totalBytes) * 100.0, 0, 100);

                                                        // Выводим в дебаг посчитанный процент, чтобы вы сразу его увидели
                                                        System.Diagnostics.Debug.WriteLine($"[DEBUG CALC PERCENT]: {curr} {currUnit} / {total} {totalUnit} = {pct:F1}%");

                                                        percentProgress?.Report(pct);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // 3. Запасной вариант для классических процентов "45%" (например, для Microsoft Store)
                                        var matchPercent = Regex.Match(line, @"(\d+)\s*%");
                                        if (matchPercent.Success && double.TryParse(matchPercent.Groups[1].Value, out double pct))
                                        {
                                            percentProgress?.Report(pct);
                                        }
                                    }
                                }
                                lineBuilder.Clear();
                            }
                            else
                            {
                                lineBuilder.Append(c);
                            }
                        }
                    }
                });

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
