using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Stalker2ModManager.Services;

namespace Stalker2ModManager.Views
{
    public partial class DlcModLoaderWindow : Window
    {
        private readonly LocalizationService _localization;

        public DlcModLoaderWindow()
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
            Title = _localization.GetString("DlcModLoaderTitle");
            TitleTextBlock.Text = _localization.GetString("DlcModLoaderTitle");
            DescriptionTextBlock.Text = _localization.GetString("DlcModLoaderDescription");
            RunButton.Content = _localization.GetString("DlcModLoaderRun");
            AuthorLabelRun.Text = _localization.GetString("DlcModLoaderAuthor");
            GitHubButton.ToolTip = _localization.GetString("DlcModLoaderGitHubTooltip");
            NexusModsButton.ToolTip = _localization.GetString("DlcModLoaderNexusTooltip");
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var configService = new ConfigService();
                var runner = new HerbatasDLCModLoaderRunner();
                var result = runner.RunUsingConfig(configService);

                if (result.Success)
                {
                    WarningWindow.Show($"PAK created/copied to:\n{result.Message}", _localization.GetString("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    WarningWindow.Show(result.Message, _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                WarningWindow.Show(ex.Message, _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GitHubImage_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/herbatka/HerbatasDLCModLoader",
                UseShellExecute = true
            });
        }

        private void NexusModsImage_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.nexusmods.com/stalker2heartofchornobyl/mods/664",
                UseShellExecute = true
            });
        }

        private void AuthorLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
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

