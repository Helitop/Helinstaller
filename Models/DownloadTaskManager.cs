using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace Helinstaller.Models
{
    public class DownloadTaskManager
    {
        private static readonly DownloadTaskManager _instance = new();
        public static DownloadTaskManager Instance => _instance;

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