using System.ComponentModel;
using System.IO;

namespace Helinstaller.Models
{
    public class UsbDriveItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string DriveLetter { get; set; } = string.Empty;
        public string VolumeLabel { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        private long _totalBytes;
        public long TotalBytes
        {
            get => _totalBytes;
            set { _totalBytes = value; OnProp(nameof(TotalBytes)); }
        }

        private long _freeBytes;
        public long FreeBytes
        {
            get => _freeBytes;
            set { _freeBytes = value; OnProp(nameof(FreeBytes)); }
        }

        private double _usedPercent;
        public double UsedPercent
        {
            get => _usedPercent;
            set { _usedPercent = value; OnProp(nameof(UsedPercent)); }
        }

        private void OnProp(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public DriveInfo? ToDriveInfo()
        {
            try
            {
                var letter = DriveLetter;
                if (!letter.EndsWith(":"))
                    letter = letter.Length == 1 ? letter + ":" : letter.TrimEnd('\\');
                return new DriveInfo(letter);
            }
            catch { return null; }
        }
    }
}
