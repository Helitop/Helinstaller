using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Helinstaller.Models
{
    public partial class DownloadTask : ObservableObject
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string Title { get; init; }
        public string IconPath { get; init; }
        public string AppName { get; init; } // Для проверки установки по завершению

        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _status = "Ожидание...";
        [ObservableProperty] private bool _isIndeterminate;
        [ObservableProperty] private bool _isCompleted;
        [ObservableProperty] private bool _isError;
        [ObservableProperty] private string _errorMessage;

        // Время запуска для сортировки в журнале
        public DateTime StartTime { get; } = DateTime.Now;
    }
}