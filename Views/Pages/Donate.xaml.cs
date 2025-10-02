using Helinstaller.ViewModels.Pages;
using System.Diagnostics;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace Helinstaller.Views.Pages
{
    public partial class Donate : INavigableView<DonateViewmodel>
    {
        public DonateViewmodel ViewModel { get; }
        private readonly INavigationService _navigationService;

        public Donate(DonateViewmodel viewModel, INavigationService navigationService)
        {
            ViewModel = viewModel;
            _navigationService = navigationService;
            DataContext = this;
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            String url = "https://www.tbank.ru/rm/r_fOUxBdtVtS.hjMfQVROGt/pUbCK61988/";
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        }
    }
}
