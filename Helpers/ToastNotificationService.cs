using System;
using System.IO;
using System.Xml;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Helinstaller.Helpers
{
    public static class ToastNotificationService
    {
        // AppUserModelId, который создается и регистрируется Velopack по умолчанию
        private static readonly string AppId = "velopack.Helinstaller";
        private static int _sequenceNumber = 1;

        // Потокобезопасные структуры для предотвращения дублирования и троттлинга
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (double pct, DateTime time)> _throttles = new();
        private static readonly System.Collections.Generic.HashSet<string> _activeTags = new();

        /// <summary>
        /// Показывает обычное текстовое уведомление (например, об успехе или ошибке).
        /// </summary>
        public static void ShowToast(string title, string message)
        {
            try
            {
                string xmlString = $@"
                    <toast>
                        <visual>
                            <binding template=""ToastGeneric"">
                                <text id=""1"">{System.Security.SecurityElement.Escape(title)}</text>
                                <text id=""2"">{System.Security.SecurityElement.Escape(message)}</text>
                            </binding>
                        </visual>
                    </toast>";

                var xmlDoc = new Windows.Data.Xml.Dom.XmlDocument();
                xmlDoc.LoadXml(xmlString);

                var toast = new ToastNotification(xmlDoc);
                GetNotifier().Show(toast);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toast error]: {ex.Message}");
            }
        }

        /// <summary>
        /// Создает и отображает первоначальное уведомление с прогресс-баром.
        /// </summary>
        public static void ShowProgressToast(string tag, string group, string title, string initialStatus)
        {
            lock (_activeTags)
            {
                // Защита от дублирования вызова создания
                if (_activeTags.Contains(tag)) return;
                _activeTags.Add(tag);
            }

            try
            {
                string escapedTitle = System.Security.SecurityElement.Escape(title);
                string xmlString = $@"
                    <toast>
                        <visual>
                            <binding template=""ToastGeneric"">
                                <text id=""1"">{escapedTitle}</text>
                                <progress title=""{{progressTitle}}"" value=""{{progressValue}}"" valueStringOverride=""{{progressValueString}}"" status=""{{progressStatus}}""/>
                            </binding>
                        </visual>
                    </toast>";

                var xmlDoc = new Windows.Data.Xml.Dom.XmlDocument();
                xmlDoc.LoadXml(xmlString);

                var toast = new ToastNotification(xmlDoc)
                {
                    Tag = tag,
                    Group = group
                };

                // Инициализируем привязку данных
                toast.Data = new NotificationData();
                toast.Data.Values["progressTitle"] = title;
                toast.Data.Values["progressValue"] = "0.0";
                toast.Data.Values["progressValueString"] = "0%";
                toast.Data.Values["progressStatus"] = initialStatus;
                toast.Data.SequenceNumber = (uint)_sequenceNumber++;

                GetNotifier().Show(toast);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toast progress show error]: {ex.Message}");
            }
        }

        /// <summary>
        /// Обновляет существующий прогресс-бар в центре уведомлений без вызова звукового сигнала и повторного появления баннера.
        /// </summary>
        public static void UpdateProgressToast(string tag, string group, string title, double progressPercent, string status)
        {
            // Троттлинг: обновляем тост только при изменении прогресса более чем на 1.5% 
            // ИЛИ если с предыдущего обновления прошло более 1.2 секунд.
            if (_throttles.TryGetValue(tag, out var last))
            {
                bool progressChangedSignificantly = Math.Abs(progressPercent - last.pct) >= 1.5;
                bool enoughTimePassed = (DateTime.Now - last.time).TotalMilliseconds >= 1200;

                if (!progressChangedSignificantly && !enoughTimePassed && progressPercent < 99.9)
                {
                    return;
                }
            }

            _throttles[tag] = (progressPercent, DateTime.Now);

            try
            {
                double normalizedValue = Math.Clamp(progressPercent / 100.0, 0.0, 1.0);

                var data = new NotificationData();
                data.Values["progressTitle"] = title;
                data.Values["progressValue"] = normalizedValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                data.Values["progressValueString"] = $"{progressPercent:F0}%";
                data.Values["progressStatus"] = status;
                data.SequenceNumber = (uint)_sequenceNumber++;

                GetNotifier().Update(data, tag, group);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toast progress update error]: {ex.Message}");
            }
        }

        /// <summary>
        /// Заменяет уведомление с прогресс-баром на финальный тост (успех или ошибка).
        /// </summary>
        public static void CompleteProgressToast(string tag, string group, string title, string message, bool isSuccess)
        {
            lock (_activeTags)
            {
                _activeTags.Remove(tag);
                _throttles.TryRemove(tag, out _);
            }

            try
            {
                // Удаляем тост с прогресс-баром из центра уведомлений
                try
                {
                    ToastNotificationManager.History.Remove(tag, group, AppId);
                }
                catch { }

                // Показываем финальное уведомление
                string statusEmoji = isSuccess ? "✅" : "❌";
                ShowToast($"{statusEmoji} {title}", message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toast progress complete error]: {ex.Message}");
            }
        }

        private static ToastNotifier GetNotifier()
        {
            try
            {
                return ToastNotificationManager.CreateToastNotifier(AppId);
            }
            catch
            {
                // Резервный вариант, если AUMID не распознан вне среды установки
                return ToastNotificationManager.CreateToastNotifier();
            }
        }
    }
}