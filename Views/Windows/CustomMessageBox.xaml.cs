using System.Windows.Controls;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
namespace Helinstaller
{
    public partial class CustomMessageBox : FluentWindow
    {
        public enum MessageBoxResult
        {
            OK,
            Cancel,
            Yes,
            No,
            None
        }

        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public CustomMessageBox(string message, string title, MessageBoxButton buttons)
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;
            CreateButtons(buttons);
        }

        private void CreateButtons(MessageBoxButton buttons)
        {
            if (buttons == MessageBoxButton.OK || buttons == MessageBoxButton.OKCancel)
            {
                var okButton = new Wpf.Ui.Controls.Button { Content = "ОК", Margin = new Thickness(5, 0, 0, 0), Appearance = ControlAppearance.Secondary };
                okButton.Click += (sender, e) => { Result = MessageBoxResult.OK; Close(); };
                (FindName("ButtonPanel") as StackPanel)?.Children.Add(okButton);
            }
            if (buttons == MessageBoxButton.OKCancel)
            {
                var cancelButton = new Wpf.Ui.Controls.Button { Content = "Отмена", Margin = new Thickness(5, 0, 0, 0), Appearance = ControlAppearance.Secondary };
                cancelButton.Click += (sender, e) => { Result = MessageBoxResult.Cancel; Close(); };
                (FindName("ButtonPanel") as StackPanel)?.Children.Add(cancelButton);
            }
            if (buttons == MessageBoxButton.YesNo || buttons == MessageBoxButton.YesNoCancel)
            {
                var yesButton = new Wpf.Ui.Controls.Button { Content = "Да", Margin = new Thickness(5, 0, 0, 0), Appearance = ControlAppearance.Secondary };
                yesButton.Click += (sender, e) => { Result = MessageBoxResult.Yes; Close(); };
                (FindName("ButtonPanel") as StackPanel)?.Children.Add(yesButton);

                var noButton = new Wpf.Ui.Controls.Button { Content = "Нет", Margin = new Thickness(5, 0, 0, 0), Appearance = ControlAppearance.Secondary };
                noButton.Click += (sender, e) => { Result = MessageBoxResult.No; Close(); };
                (FindName("ButtonPanel") as StackPanel)?.Children.Add(noButton);
            }
            if (buttons == MessageBoxButton.YesNoCancel)
            {
                var cancelButton = new Wpf.Ui.Controls.Button { Content = "Отмена", Margin = new Thickness(5, 0, 0, 0), Appearance = ControlAppearance.Secondary };
                cancelButton.Click += (sender, e) => { Result = MessageBoxResult.Cancel; Close(); };
                (FindName("ButtonPanel") as StackPanel)?.Children.Add(cancelButton);
            }
        }

        public static MessageBoxResult Show(string message, string title = "Сообщение", MessageBoxButton buttons = MessageBoxButton.OK)
        {
            var dialog = new CustomMessageBox(message, title, buttons);
            dialog.ShowDialog();
            return dialog.Result;
        }
    }
}