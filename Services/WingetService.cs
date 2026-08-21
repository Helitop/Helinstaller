// FILE [CS]: .\Services\WingetService.cs

using Helinstaller.Helpers; // Используем наш Logger
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Globalization;
using Helinstaller.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
            Logger.LogInfo($"WinGet: Запрос поиска пакета '{query}'");
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return new List<WinGetPackage>();

                return await _packageManager.SearchPackageAsync(query);
            }
            catch (Exception ex)
            {
                Logger.LogError($"WinGet: Ошибка при поиске '{query}'", ex);
                return new List<WinGetPackage>();
            }
        }

        public async Task<bool> UninstallPackageAsync(string packageId)
        {
            try
            {
                Logger.LogInfo($"WinGet: Удаление пакета '{packageId}'");
                return await _packageManager.UninstallPackageAsync(packageId);
            }
            catch (Exception ex)
            {
                Logger.LogError($"WinGet: Ошибка при удалении '{packageId}'", ex);
                return false;
            }
        }

        public async Task<bool> UpgradePackageAsync(string packageId)
        {
            try
            {
                Logger.LogInfo($"WinGet: Обновление пакета '{packageId}'");
                return await _packageManager.UpgradePackageAsync(packageId);
            }
            catch (Exception ex)
            {
                Logger.LogError($"WinGet: Ошибка при обновлении '{packageId}'", ex);
                return false;
            }
        }

        public async Task<List<WinGetPackage>> GetInstalledPackagesAsync()
        {
            try
            {
                return await _packageManager.GetInstalledPackagesAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError("WinGet: Не удалось получить список установленных пакетов", ex);
                return new List<WinGetPackage>();
            }
        }

        private static double ToBytes(double value, string unit)
        {
            return unit switch
            {
                "KB" => value * 1024,
                "MB" => value * 1024 * 1024,
                "GB" => value * 1024 * 1024 * 1024,
                "TB" => value * 1024 * 1024 * 1024 * 1024,
                _ => value
            };
        }

        public async Task<bool> IsWingetAvailableAsync()
        {
            string localAppsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WindowsApps\winget.exe"
            );

            if (File.Exists(localAppsPath))
            {
                Logger.LogInfo("WinGet: Обнаружен псевдоним winget.exe в локальной папке WindowsApps.");
                return true;
            }

            return await Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = "--version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    using var process = Process.Start(psi);
                    if (process == null) return false;
                    process.WaitForExit();
                    bool isOk = process.ExitCode == 0;
                    Logger.LogInfo($"WinGet: Команда winget --version завершена с кодом {process.ExitCode} (Доступна: {isOk})");
                    return isOk;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"WinGet: Не удалось вызвать winget. Резервный запуск провален: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<bool> InstallOrUpdateWingetAsync(IProgress<double>? progress = null, IProgress<string>? statusProgress = null)
        {
            Logger.LogInfo("WinGet: Запуск принудительного развертывания WinGet...");
            try
            {
                statusProgress?.Report("Поиск актуального релиза Winget...");
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("HelinstallerApp/1.0");

                string downloadUrl = "";
                try
                {
                    var response = await client.GetAsync("https://api.github.com/repos/microsoft/winget-cli/releases/latest");
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("assets", out var assets))
                        {
                            foreach (var asset in assets.EnumerateArray())
                            {
                                string name = asset.GetProperty("name").GetString() ?? "";
                                if (name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase))
                                {
                                    downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"WinGet: Ошибка парсинга GitHub релиза: {ex.Message}");
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Logger.LogInfo("WinGet: Будет использована резервная прямая ссылка Microsoft.");
                    downloadUrl = "https://aka.ms/getwinget";
                }

                statusProgress?.Report("Загрузка установщика пакетов...");
                string tempFile = Path.Combine(Path.GetTempPath(), "winget_installer.msixbundle");

                using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long? totalBytes = response.Content.Headers.ContentLength;

                    using var remoteStream = await response.Content.ReadAsStreamAsync();
                    using var localStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await remoteStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await localStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (totalBytes.HasValue && progress != null)
                        {
                            double percent = (double)totalRead / totalBytes.Value * 100.0;
                            progress.Report(percent);
                        }
                    }
                }

                statusProgress?.Report("Регистрация пакета AppInstaller в системе...");
                Logger.LogInfo("WinGet: Запуск PowerShell инсталляции пакета AppInstaller...");
                return await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '{tempFile}'\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null) return false;

                    string output = proc.StandardOutput.ReadToEnd();
                    string error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(output)) Logger.LogInfo($"[PowerShell OUT]: {output.Trim()}");
                    if (!string.IsNullOrWhiteSpace(error)) Logger.LogError($"[PowerShell ERR]: {error.Trim()}");

                    Logger.LogInfo($"WinGet: PowerShell процесс завершен с кодом {proc.ExitCode}");
                    return proc.ExitCode == 0;
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("WinGet: Не удалось завершить автоматическую установку пакета", ex);
                statusProgress?.Report($"Ошибка: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Принудительно убивает все зависшие экземпляры WinGet и их дочерние установщики
        /// </summary>
        public static void KillStuckProcesses()
        {
            string[] targets = { "winget", "WindowsPackageManagerServer" };
            foreach (var name in targets)
            {
                try
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            proc.Kill(entireProcessTree: true);
                            proc.WaitForExit(1000);
                        }
                        catch { }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                }
                catch { }
            }
        }

        public async Task<List<string>> GetUpgradablePackageIdsAsync()
        {
            Logger.LogInfo("WinGet: Запрос списка доступных обновлений через CLI...");
            return await Task.Run(async () =>
            {
                var result = new List<string>();
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = "upgrade",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    using var process = Process.Start(psi);
                    if (process == null) return result;

                    string output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    bool tableStarted = false;

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("---") || line.Contains("------"))
                        {
                            tableStarted = true;
                            continue;
                        }

                        if (tableStarted && !string.IsNullOrWhiteSpace(line))
                        {
                            var parts = Regex.Split(line.Trim(), @"\s{2,}");
                            if (parts.Length >= 2)
                            {
                                string name = parts[0].Trim();
                                string id = parts[1].Trim();

                                if (!string.IsNullOrEmpty(name)) result.Add(name);
                                if (!string.IsNullOrEmpty(id)) result.Add(id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("WinGet: Ошибка при поиске обновлений через CLI", ex);
                }
                return result;
            });
        }

        public async Task<bool> InstallPackageAsync(
            string packageId,
            IProgress<string>? progress = null,
            IProgress<double>? percentProgress = null,
            bool force = false,
            string? source = null)
        {
            // 1. Авто-зачистка старых зависших экземпляров WinGet перед стартом
            KillStuckProcesses();

            Logger.LogInfo($"WinGet: Инициирована установка пакета '{packageId}' (Force: {force}, Source: {source ?? "auto"})");

            try
            {
                // 2. Формирование аргументов команды
                string sourceArg = !string.IsNullOrWhiteSpace(source) ? $" --source {source}" : "";
                string args = $"install --id {packageId}{sourceArg} --silent --accept-package-agreements --accept-source-agreements";
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
                if (process == null)
                {
                    Logger.LogError($"WinGet: Не удалось запустить системный процесс для ID '{packageId}'");
                    return false;
                }

                // 3. Асинхронное чтение и логирование потока ошибок (StandardError)
                var stdErrTask = Task.Run(async () =>
                {
                    try
                    {
                        using var errReader = process.StandardError;
                        while (!errReader.EndOfStream)
                        {
                            string? errLine = await errReader.ReadLineAsync();
                            if (!string.IsNullOrWhiteSpace(errLine))
                            {
                                Logger.LogWarning($"[WinGet STDERR] [{packageId}]: {errLine.Trim()}");
                            }
                        }
                    }
                    catch { }
                });

                // 4. Посимвольное чтение вывода (StandardOutput) и парсинг прогресса
                var stdOutTask = Task.Run(async () =>
                {
                    try
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
                                        string cleanLine = Regex.Replace(line, @"[█░▄▀■►─\-|=+*#•·]|\[|\]", "").Trim();
                                        if (!string.IsNullOrEmpty(cleanLine) && cleanLine.Length > 2)
                                        {
                                            Logger.LogInfo($"[WinGet STDOUT] [{packageId}]: {cleanLine}");
                                        }

                                        progress?.Report(line);

                                        if (line.Contains("/"))
                                        {
                                            var parts = line.Split('/');
                                            if (parts.Length == 2)
                                            {
                                                var matchTotal = Regex.Match(parts[1], @"(?<val>\d+(?:\.\d+)?)\s*(?<unit>[KMGT]?B)", RegexOptions.IgnoreCase);
                                                var matchCurr = Regex.Match(parts[0], @"(?<val>\d+(?:\.\d+)?)\s*(?<unit>[KMGT]?B)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

                                                if (matchCurr.Success && matchTotal.Success)
                                                {
                                                    if (double.TryParse(matchCurr.Groups["val"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double curr) &&
                                                        double.TryParse(matchTotal.Groups["val"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double total))
                                                    {
                                                        string currUnit = matchCurr.Groups["unit"].Value.ToUpper();
                                                        string totalUnit = matchTotal.Groups["unit"].Value.ToUpper();

                                                        double currBytes = ToBytes(curr, currUnit);
                                                        double totalBytes = ToBytes(total, totalUnit);

                                                        if (totalBytes > 0)
                                                        {
                                                            double pct = Math.Clamp((currBytes / totalBytes) * 100.0, 0, 100);
                                                            percentProgress?.Report(pct);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
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
                    }
                    catch { }
                });

                // 5. Контроль таймаута
                int timeoutSec = AppSettings.InstallTimeoutSeconds >= 15 ? AppSettings.InstallTimeoutSeconds : 900;

                var waitTask = process.WaitForExitAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSec));

                if (await Task.WhenAny(waitTask, timeoutTask) == timeoutTask)
                {
                    Logger.LogWarning($"WinGet: Превышен таймаут установки ({timeoutSec} сек) для '{packageId}'. Принудительное завершение дерева процессов...");
                    try { process.Kill(entireProcessTree: true); } catch { }
                    try { await Task.WhenAll(stdOutTask, stdErrTask); } catch { }
                    return false;
                }

                await waitTask;
                try { await Task.WhenAll(stdOutTask, stdErrTask); } catch { }

                Logger.LogInfo($"WinGet: Установка для '{packageId}' завершена с кодом {process.ExitCode}");

                // 6. Проверка кодов успешного завершения:
                // 0 — успешно установлено
                // -1978335189 (0x8A15002B) — приложение уже установлено и обновлений нет
                // 3010 / 1641 — успешно установлено (требуется перезагрузка ОС)
                bool isSuccess = process.ExitCode == 0
                              || process.ExitCode == -1978335189
                              || process.ExitCode == 3010
                              || process.ExitCode == 1641;

                return isSuccess;
            }
            catch (Exception ex)
            {
                Logger.LogError($"WinGet: Сбой при попытке вызова процесса установки для '{packageId}'", ex);
                return false;
            }
        }
    }
}