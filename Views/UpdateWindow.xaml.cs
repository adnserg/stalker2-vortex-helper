using System;
using System.Threading.Tasks;
using System.Windows;
using Stalker2ModManager.Models;
using Stalker2ModManager.Services;

namespace Stalker2ModManager.Views
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
                var localization = LocalizationService.Instance;
                WarningWindow.Show(localization.GetString("DownloadUrlNotAvailable"), localization.GetString("Error"), 
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
                    var localization = LocalizationService.Instance;
                    WarningWindow.Show(localization.GetString("FailedToDownloadUpdate"), localization.GetString("Error"), 
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
                    var localization = LocalizationService.Instance;
                    WarningWindow.Show(localization.GetString("FailedToExtractUpdate"), localization.GetString("Error"), 
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
                    var localization = LocalizationService.Instance;
                    WarningWindow.Show(localization.GetString("FailedToPrepareUpdateInstaller"), localization.GetString("Error"), 
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
                    var localization = LocalizationService.Instance;
                    WarningWindow.Show(localization.GetString("FailedToStartUpdateProcess"), localization.GetString("Error"), 
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
                var localization = LocalizationService.Instance;
                WarningWindow.Show(localization.GetString("ErrorDuringUpdate", ex.Message), localization.GetString("Error"), 
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

