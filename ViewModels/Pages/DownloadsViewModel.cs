using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helinstaller.Models;
using System.Collections.ObjectModel;

namespace Helinstaller.ViewModels.Pages
{
    public partial class DownloadsViewModel : ObservableObject
    {
        public ObservableCollection<DownloadTask> DownloadTasks => DownloadTaskManager.Instance.Tasks;

        [RelayCommand]
        private void ClearHistory() => DownloadTaskManager.Instance.ClearHistory();

        [RelayCommand]
        private void RemoveTask(DownloadTask task) => DownloadTaskManager.Instance.Tasks.Remove(task);
    }
}