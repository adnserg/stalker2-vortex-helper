using System;
using System.Threading.Tasks;
using System.Windows;
using Stalker2ModManager.Models;
using Stalker2ModManager.Services;

namespace Stalker2ModManager
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfo _updateInfo;
        private readonly string _currentVersion;
        private readonly string _downloadUrl;
        private UpdateInstallerService _installerService;

        public UpdateWindow(UpdateInfo updateInfo, string currentVersion, string downloadUrl)
        {
            InitializeComponent();
            _updateInfo = updateInfo;
            _currentVersion = currentVersion;
            _downloadUrl = downloadUrl;
            _installerService = new UpdateInstallerService();

            LoadUpdateInfo();
        }

        private void LoadUpdateInfo()
        {
            CurrentVersionTextBlock.Text = _currentVersion;
            NewVersionTextBlock.Text = _updateInfo.Version ?? "Unknown";

            if (!string.IsNullOrEmpty(_updateInfo.ReleaseNotes))
            {
                ReleaseNotesTextBlock.Text = _updateInfo.ReleaseNotes;
            }
            else
            {
                ReleaseNotesTextBlock.Text = "No release notes available.";
            }

            if (!string.IsNullOrEmpty(_downloadUrl))
            {
                DownloadUrlTextBlock.Text = "Click 'Install Update' to download and install automatically";
            }
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_downloadUrl))
            {
                MessageBox.Show("Download URL is not available.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Блокируем кнопки
            InstallButton.IsEnabled = false;
            LaterButton.IsEnabled = false;
            CloseButton.IsEnabled = false;

            // Показываем прогресс
            ProgressGrid.Visibility = Visibility.Visible;
            ButtonsPanel.Visibility = Visibility.Collapsed;
            UpdateProgressBar.Value = 0;
            ProgressTextBlock.Text = "Downloading update...";

            try
            {
                // Скачиваем обновление
                var progress = new Progress<double>(percentage =>
                {
                    UpdateProgressBar.Value = percentage;
                    ProgressTextBlock.Text = $"Downloading update... {percentage:F1}%";
                });

                var downloadSuccess = await _installerService.DownloadUpdateAsync(_downloadUrl, progress);
                
                if (!downloadSuccess)
                {
                    MessageBox.Show("Failed to download update. Please try again later.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetUI();
                    return;
                }

                ProgressTextBlock.Text = "Extracting archive...";
                UpdateProgressBar.Value = 50;

                // Распаковываем архив
                var extractSuccess = _installerService.ExtractArchive();
                
                if (!extractSuccess)
                {
                    MessageBox.Show("Failed to extract update archive. Please try again later.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetUI();
                    return;
                }

                ProgressTextBlock.Text = "Preparing update installer...";
                UpdateProgressBar.Value = 75;

                // Подготавливаем установщик обновлений (скачиваем если нужно)
                var installerProgress = new Progress<double>(percentage =>
                {
                    var totalProgress = 75 + (percentage * 0.15); // 75-90%
                    UpdateProgressBar.Value = totalProgress;
                    ProgressTextBlock.Text = $"Downloading UpdateInstaller... {percentage:F1}%";
                });

                var scriptSuccess = await _installerService.PrepareUpdateInstallerAsync(installerProgress);
                
                if (!scriptSuccess)
                {
                    MessageBox.Show("Failed to download or prepare update installer. Please check your internet connection and try again.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetUI();
                    return;
                }

                ProgressTextBlock.Text = "Starting update process...";
                UpdateProgressBar.Value = 100;

                // Запускаем процесс обновления
                var startSuccess = _installerService.StartUpdateProcess();
                
                if (!startSuccess)
                {
                    MessageBox.Show("Failed to start update process. Please try again later.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetUI();
                    return;
                }

                // Закрываем приложение
                await Task.Delay(500);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during update: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ResetUI();
            }
        }

        private void ResetUI()
        {
            InstallButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            CloseButton.IsEnabled = true;
            ProgressGrid.Visibility = Visibility.Collapsed;
            ButtonsPanel.Visibility = Visibility.Visible;
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _installerService?.Dispose();
            base.OnClosed(e);
        }
    }
}

