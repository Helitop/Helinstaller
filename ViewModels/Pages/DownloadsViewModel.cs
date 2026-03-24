using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helinstaller.Models;
using Helinstaller.Services;
using Helinstaller.Views.Pages;
using System.Collections.ObjectModel;

namespace Helinstaller.ViewModels.Pages
{
    public partial class DownloadsViewModel : ObservableObject
    {
        public ObservableCollection<DownloadTask> DownloadTasks => DownloadService.Instance.Tasks;

        [RelayCommand]
        private void ClearHistory() => DownloadService.Instance.Tasks.Clear();

        [RelayCommand]
        private void RemoveTask(DownloadTask task) => DownloadService.Instance.Tasks.Remove(task);
    }
}