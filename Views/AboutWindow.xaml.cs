using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Stalker2ModManager.Services;

namespace Stalker2ModManager.Views
{
    public partial class AboutWindow : Window
    {
        private readonly LocalizationService _localization;

        public AboutWindow()
        {
            InitializeComponent();
            _localization = LocalizationService.Instance;
            _localization.LanguageChanged += Localization_LanguageChanged;
            UpdateLocalization();
        }

        private void Localization_LanguageChanged(object? sender, System.EventArgs e)
        {
            UpdateLocalization();
        }

        private void UpdateLocalization()
        {
            Title = _localization.GetString("AboutWindowTitle");
            AboutTitleTextBlock.Text = _localization.GetString("AboutWindowTitle");
            AuthorLabelRun.Text = _localization.GetString("AboutAuthor");
            RepositoryLabelRun.Text = _localization.GetString("AboutRepository");
            ThanksRun.Text = _localization.GetString("AboutThanks");
            CloseButton.Content = _localization.GetString("Close");
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}


