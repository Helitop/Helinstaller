using System.Threading.Tasks;

namespace Helinstaller.Services
{
    public interface ITweaksService
    {
        Task RunYandexAnnihilatorAsync();
        bool ToggleTaskbarEndTask(bool enable);
        bool ToggleStickyKeys(bool enable);
        Task ActivateWinRARAsync();
        Task ReplaceHostsFileAsync(string url);
        
        bool IsStickyKeysEnabled();
        bool IsTaskbarEndTaskEnabled();
    }
}
