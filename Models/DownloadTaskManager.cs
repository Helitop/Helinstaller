using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Helinstaller.Models
{
    public class DownloadTaskManager
    {
        private static readonly DownloadTaskManager _instance = new();
        public static DownloadTaskManager Instance => _instance;

        // Поток-безопасная коллекция для UI
        public ObservableCollection<DownloadTask> Tasks { get; } = new();

        private readonly Queue<QueueItem> _queue = new();
        private bool _isProcessing = false;
        private readonly object _lock = new();

        private class QueueItem
        {
            public Func<Task> Action { get; init; } = null!;
            public TaskCompletionSource Tcs { get; init; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void AddTask(DownloadTask task)
        {
            Application.Current.Dispatcher.Invoke(() => Tasks.Insert(0, task));
        }

        public async Task EnqueueAsync(DownloadTask task, Func<Task> action)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var item = new QueueItem { Action = action, Tcs = tcs };

            lock (_lock)
            {
                _queue.Enqueue(item);
                task.Status = "В очереди..."; // Сразу ставим статус ожидания

                if (!_isProcessing)
                {
                    _isProcessing = true;
                    // Запускаем асинхронную обработку очереди прямо в контексте UI-потока.
                    // Поскольку методы асинхронные, UI зависать не будет.
                    _ = ProcessQueueAsync();
                }
            }

            // Ожидаем завершения конкретной задачи
            await tcs.Task;
        }

        private async Task ProcessQueueAsync()
        {
            while (true)
            {
                QueueItem? nextItem = null;
                lock (_lock)
                {
                    if (_queue.Count > 0)
                    {
                        nextItem = _queue.Dequeue();
                    }
                    else
                    {
                        _isProcessing = false;
                        break;
                    }
                }

                if (nextItem != null)
                {
                    try
                    {
                        // Выполняем задачу напрямую. При каждом внутреннем await (скачивание/процессы)
                        // она будет возвращать управление UI-потоку, сохраняя приложение отзывчивым.
                        await nextItem.Action();
                        nextItem.Tcs.SetResult();
                    }
                    catch (Exception ex)
                    {
                        nextItem.Tcs.SetException(ex);
                    }
                }
            }
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