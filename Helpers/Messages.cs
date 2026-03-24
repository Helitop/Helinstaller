using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helinstaller.Helpers
{
    // Сообщение о изменении статуса визуализатора
    public class VisualizerStatusChangedMessage : ValueChangedMessage<bool>
    {
        public VisualizerStatusChangedMessage(bool value) : base(value) { }
    }
}