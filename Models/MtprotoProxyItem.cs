using CommunityToolkit.Mvvm.ComponentModel;

namespace Helinstaller.Models
{
    public enum ProxyStatus
    {
        Unknown,
        Checking,
        Online,
        Offline
    }

    public partial class MtprotoProxyItem : ObservableObject
    {
        public string Server { get; init; } = string.Empty;
        public int Port { get; init; }
        public string Secret { get; init; } = string.Empty;
        public string RawUrl { get; init; } = string.Empty;

        [ObservableProperty] private ProxyStatus _status = ProxyStatus.Unknown;
        [ObservableProperty] private long _ping = -1;
        [ObservableProperty] private string _pingText = "—";

        partial void OnPingChanged(long value)
        {
            PingText = value >= 0 ? $"{value} мс" : "Таймаут";
        }
    }
}