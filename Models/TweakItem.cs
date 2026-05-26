using System.ComponentModel;
using System.Runtime.CompilerServices;
using Wpf.Ui.Controls;

namespace Helinstaller.Models
{
    public class TweakItem : INotifyPropertyChanged
    {
        public required string Title { get; set; }
        public required string Description { get; set; }
        public required string Tag { get; set; }
        public bool ShowSwitch { get; set; }
        public SymbolRegular Icon { get; set; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
