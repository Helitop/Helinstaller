// FILE [CS]: .\Models\DiskInfoItem.cs

using CommunityToolkit.Mvvm.ComponentModel;

namespace Helinstaller.Models
{
    public partial class DiskInfoItem : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _volumeLabel = string.Empty;
        [ObservableProperty] private string _driveLetter = string.Empty;
        [ObservableProperty] private double _usedPercent;
        [ObservableProperty] private string _freeSpaceText = string.Empty;
        [ObservableProperty] private string _totalSpaceText = string.Empty;
        [ObservableProperty] private string _usedSpaceText = string.Empty;

        // Новые параметры аппаратного мониторинга:
        [ObservableProperty] private string _healthStatus = "OK";
        [ObservableProperty] private int _healthPercent = 100;
        [ObservableProperty] private string _temperatureText = "—";
        [ObservableProperty] private string _mediaType = "SSD"; // SSD / HDD / NVMe
    }
}