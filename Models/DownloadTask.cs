using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Helinstaller.Models
{
    public partial class DownloadTask : ObservableObject
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string Title { get; init; } = string.Empty;
        public string IconPath { get; init; } = string.Empty;
        public string AppName { get; init; } = string.Empty;

        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _status = "Ожидание...";
        [ObservableProperty] private bool _isIndeterminate = true; // По умолчанию крутим анимацию
        [ObservableProperty] private bool _isCompleted;
        [ObservableProperty] private bool _isError;
        [ObservableProperty] private string _errorMessage = string.Empty;

        public DateTime StartTime { get; } = DateTime.Now;

        // Авто-переключение: если прогресс 0 или меньше -> бегущая полоска
        partial void OnProgressChanged(double value)
        {
            if (value <= 0 && !IsCompleted && !IsError)
            {
                IsIndeterminate = true;
            }
            else if (value > 0)
            {
                IsIndeterminate = false;
            }
        }
    }
}