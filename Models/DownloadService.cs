using System.Collections.ObjectModel;
using System.Windows;
using Helinstaller.Models;

namespace Helinstaller.Services
{
    public class DownloadService
    {
        private static readonly DownloadService _instance = new();
        public static DownloadService Instance => _instance;

        // Поток-безопасная коллекция для UI
        public ObservableCollection<DownloadTask> Tasks { get; } = new();

        public void AddTask(DownloadTask task)
        {
            Application.Current.Dispatcher.Invoke(() => Tasks.Insert(0, task));
        }

        public void ClearHistory()
        {
            // Выполняем изменения в UI-потоке
            Application.Current.Dispatcher.Invoke(() =>
            {
                var toRemove = Tasks.Where(t => t.IsCompleted).ToList();
                foreach (var task in toRemove)
                {
                    Tasks.Remove(task);
                }
            });
        }
    }
}