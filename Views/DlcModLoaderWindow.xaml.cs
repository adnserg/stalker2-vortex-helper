using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Stalker2ModManager.Services;

namespace Stalker2ModManager.Views
{
    public partial class DlcModLoaderWindow : Window
    {
        public DlcModLoaderWindow()
        {
            InitializeComponent();
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
                    WarningWindow.Show($"PAK created/copied to:\n{result.Message}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    WarningWindow.Show(result.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                WarningWindow.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

