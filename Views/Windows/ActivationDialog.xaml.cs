using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Helinstaller.Views.Pages
{
    public partial class ActivationDialog : FluentWindow
    {
        private Process _psProcess;
        private readonly string _scriptUrl = "https://get.activated.win";
        private bool _isSuccessDetected = false;

        public ActivationDialog()
        {
            InitializeComponent();
            Closing += (s, e) => { try { _psProcess?.Kill(); } catch { } };
        }

        private void ActivationBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.Button btn || btn.Tag == null) return;

            string switchArg = btn.Tag.ToString();

            // Сброс состояния перед запуском
            _isSuccessDetected = false;
            ButtonsContainer.IsEnabled = false;
            LoadingRing.Visibility = Visibility.Visible;
            DebugLog.Text = $"--- Запуск: {btn.Content} ({switchArg}) ---\n";
            StatusText.Foreground = (Brush)FindResource("TextFillColorPrimaryBrush");
            StatusText.Text = "Подключение к серверу...";
            SubtitleText.Text = "Выполняется скрипт... Пожалуйста, подождите.";

            RunActivationScript(switchArg);
        }

        private void RunActivationScript(string argument)
        {
            _psProcess = new Process();
            _psProcess.StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; $s = Invoke-RestMethod -Uri '{_scriptUrl}'; $s = [scriptblock]::Create($s); & $s {argument}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            _psProcess.OutputDataReceived += (s, e) => {
                if (e.Data != null) Dispatcher.Invoke(() => {
                    string cleanLine = Regex.Replace(e.Data, @"\x1B\[[^@-~]*[@-~]", "").Trim();

                    if (!string.IsNullOrEmpty(cleanLine))
                    {
                        DebugLog.Text += cleanLine + Environment.NewLine;
                        DebugLog.ScrollToEnd();
                        StatusText.Text = cleanLine;

                        // ИНТЕЛЛЕКТУАЛЬНЫЙ ПАРСИНГ УСПЕХА
                        // MAS обычно пишет "Product activated successfully" или "Permanently Activated"
                        if (cleanLine.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                            cleanLine.Contains("Activated", StringComparison.OrdinalIgnoreCase) ||
                            cleanLine.Contains("Success", StringComparison.OrdinalIgnoreCase))
                        {
                            _isSuccessDetected = true;
                        }
                    }
                });
            };

            _psProcess.ErrorDataReceived += (s, e) => {
                if (e.Data != null) Dispatcher.Invoke(() => {
                    DebugLog.Text += "ОШИБКА: " + e.Data + Environment.NewLine;
                });
            };

            _psProcess.EnableRaisingEvents = true;
            _psProcess.Exited += (s, e) => Dispatcher.Invoke(() => {
                ButtonsContainer.IsEnabled = true;
                LoadingRing.Visibility = Visibility.Collapsed;

                var msg = new Wpf.Ui.Controls.MessageBox();
                msg.Title = "Результат активации";

                if (_isSuccessDetected)
                {
                    StatusText.Text = "Активация завершена успешно!";
                    StatusText.Foreground = Brushes.LightGreen;
                    SubtitleText.Text = "Система готова к использованию.";
                    msg.Content = "Процесс завершен успешно! Лицензия установлена.";
                }
                else
                {
                    StatusText.Text = "Процесс завершен.";
                    StatusText.Foreground = Brushes.Orange;
                    SubtitleText.Text = "Проверьте статус в настройках Windows.";
                    msg.Content = "Скрипт завершил работу. Если активация не прошла, проверьте лог на наличие ошибок.";
                }

                msg.ShowDialogAsync();
            });

            try
            {
                _psProcess.Start();
                _psProcess.BeginOutputReadLine();
                _psProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                var msg = new Wpf.Ui.Controls.MessageBox();
                msg.Content = "Ошибка запуска: " + ex.Message;
                msg.ShowDialogAsync();
                ButtonsContainer.IsEnabled = true;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}