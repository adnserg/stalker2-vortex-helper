using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace UpdateInstaller
{
    public partial class MainWindow : Window
    {
        private string _appDirectory;
        private string _extractFolder;
        private string _appExeName;
        private int _waitTimeout = 30;
        private DispatcherTimer _updateTimer;
        private readonly Logger _logger;

        public MainWindow()
        {
            InitializeComponent();
            _logger = Logger.Instance;
            _logger.LogInfo("UpdateInstaller started");
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Получаем параметры из командной строки
            var args = Environment.GetCommandLineArgs();
            _logger.LogInfo($"UpdateInstaller loaded with {args.Length - 1} arguments");
            
            if (args.Length < 4)
            {
                _logger.LogError("Invalid arguments count");
                ShowError("Invalid arguments. Expected: <appDirectory> <extractFolder> <appExeName> [waitTimeout]");
                return;
            }

            _appDirectory = args[1];
            _extractFolder = args[2];
            _appExeName = args[3];
            if (args.Length > 4 && int.TryParse(args[4], out int timeout))
            {
                _waitTimeout = timeout;
            }

            _logger.LogInfo($"Update parameters: AppDirectory={_appDirectory}, ExtractFolder={_extractFolder}, AppExeName={_appExeName}, WaitTimeout={_waitTimeout}");

            // Начинаем процесс обновления
            Task.Run(async () => await StartUpdateProcessAsync());
        }

        private async Task StartUpdateProcessAsync()
        {
            try
            {
                // Проверяем существование папок
                _logger.LogInfo("Starting update process validation");
                
                if (!Directory.Exists(_appDirectory))
                {
                    _logger.LogError($"Application directory does not exist: {_appDirectory}");
                    UpdateUI(() => ShowError($"Application directory does not exist: {_appDirectory}"));
                    return;
                }

                if (!Directory.Exists(_extractFolder))
                {
                    _logger.LogError($"Extract folder does not exist: {_extractFolder}");
                    UpdateUI(() => ShowError($"Extract folder does not exist: {_extractFolder}"));
                    return;
                }

                var appExePath = Path.Combine(_appDirectory, _appExeName);
                if (!File.Exists(appExePath))
                {
                    _logger.LogError($"Application executable not found: {appExePath}");
                    UpdateUI(() => ShowError($"Application executable not found: {appExePath}"));
                    return;
                }
                
                _logger.LogSuccess("All validation checks passed");

                // Ждем закрытия приложения
                _logger.LogInfo($"Waiting for application '{_appExeName}' to close (timeout: {_waitTimeout}s)");
                
                UpdateUI(() =>
                {
                    StatusTextBlock.Text = "Waiting for application to close...";
                    ProgressBar.Value = 0;
                    ProgressTextBlock.Text = "0%";
                    DetailsTextBlock.Text = $"Waiting for {_appExeName} to close...";
                });

                var startTime = DateTime.Now;
                var processFound = false;
                var processName = Path.GetFileNameWithoutExtension(_appExeName);

                while ((DateTime.Now - startTime).TotalSeconds < _waitTimeout)
                {
                    var processes = Process.GetProcessesByName(processName);
                    if (processes.Length == 0)
                    {
                        if (processFound)
                        {
                            var elapsed = (int)(DateTime.Now - startTime).TotalSeconds;
                            _logger.LogSuccess($"Application closed after {elapsed} seconds");
                            UpdateUI(() =>
                            {
                                StatusTextBlock.Text = "Application closed.";
                                ProgressBar.Value = 10;
                                DetailsTextBlock.Text = "Proceeding with update...";
                            });
                            await Task.Delay(500);
                            break;
                        }
                    }
                    else
                    {
                        processFound = true;
                        var elapsed = (int)(DateTime.Now - startTime).TotalSeconds;
                        var remaining = Math.Max(0, _waitTimeout - elapsed);
                        UpdateUI(() =>
                        {
                            DetailsTextBlock.Text = $"Application is still running. Waiting... ({remaining}s remaining)";
                        });
                    }

                    await Task.Delay(500);
                }

                if (processFound)
                {
                    var processes = Process.GetProcessesByName(processName);
                    if (processes.Length > 0)
                    {
                        UpdateUI(() =>
                        {
                            StatusTextBlock.Text = "Warning: Application is still running";
                            DetailsTextBlock.Text = "Proceeding with update anyway...";
                        });
                        await Task.Delay(1000);
                    }
                }
                else
                {
                    UpdateUI(() =>
                    {
                        StatusTextBlock.Text = "Application is not running.";
                        DetailsTextBlock.Text = "Proceeding with update...";
                    });
                    await Task.Delay(500);
                }

                // Копируем файлы
                _logger.LogInfo("Starting file copy process");
                
                UpdateUI(() =>
                {
                    StatusTextBlock.Text = "Updating files...";
                    ProgressBar.Value = 20;
                    ProgressTextBlock.Text = "20%";
                    DetailsTextBlock.Text = "Copying files...";
                });

                var extractFiles = Directory.GetFiles(_extractFolder, "*", SearchOption.AllDirectories).ToList();
                var totalFiles = extractFiles.Count;
                var filesCopied = 0;
                var filesSkipped = 0;
                var errors = 0;
                
                _logger.LogInfo($"Found {totalFiles} files to process");

                foreach (var sourceFile in extractFiles)
                {
                    try
                    {
                        var relativePath = Path.GetRelativePath(_extractFolder, sourceFile);
                        var targetFile = Path.Combine(_appDirectory, relativePath);
                        var targetDir = Path.GetDirectoryName(targetFile);

                        if (!string.IsNullOrEmpty(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        // Проверяем, нужно ли обновлять файл
                        bool shouldCopy = true;
                        if (File.Exists(targetFile))
                        {
                            var sourceInfo = new FileInfo(sourceFile);
                            var targetInfo = new FileInfo(targetFile);

                            if (sourceInfo.Length == targetInfo.Length &&
                                Math.Abs((sourceInfo.LastWriteTime - targetInfo.LastWriteTime).TotalSeconds) < 1)
                            {
                                shouldCopy = false;
                                filesSkipped++;
                            }
                        }

                        if (shouldCopy)
                        {
                            File.Copy(sourceFile, targetFile, true);
                            filesCopied++;
                            _logger.LogDebug($"Copied: {relativePath}");
                        }
                        else
                        {
                            filesSkipped++;
                            _logger.LogDebug($"Skipped (already up to date): {relativePath}");
                        }

                        var processed = filesCopied + filesSkipped;
                        var progress = 20 + (int)((double)processed / totalFiles * 70); // 20-90%

                        UpdateUI(() =>
                        {
                            ProgressBar.Value = progress;
                            ProgressTextBlock.Text = $"{progress}%";
                            DetailsTextBlock.Text = $"Processing: {relativePath} ({processed}/{totalFiles})";
                        });
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError($"Error copying {sourceFile}", ex);
                        UpdateUI(() =>
                        {
                            DetailsTextBlock.Text = $"Error copying {Path.GetFileName(sourceFile)}: {ex.Message}";
                        });
                    }
                }

                _logger.LogInfo($"File copy completed: {filesCopied} copied, {filesSkipped} skipped, {errors} errors");
                
                UpdateUI(() =>
                {
                    StatusTextBlock.Text = "Files updated";
                    ProgressBar.Value = 90;
                    ProgressTextBlock.Text = "90%";
                    DetailsTextBlock.Text = $"Files copied: {filesCopied}, skipped: {filesSkipped}, errors: {errors}";
                });

                if (errors > 0)
                {
                    _logger.LogWarning($"Update completed with {errors} errors");
                    await Task.Delay(2000);
                }

                // Запускаем обновленное приложение
                _logger.LogInfo($"Starting updated application: {appExePath}");
                
                UpdateUI(() =>
                {
                    StatusTextBlock.Text = "Starting updated application...";
                    ProgressBar.Value = 95;
                    ProgressTextBlock.Text = "95%";
                    DetailsTextBlock.Text = "Launching application...";
                });

                await Task.Delay(500);

                try
                {
                    var processStartInfo = new ProcessStartInfo
                    {
                        FileName = appExePath,
                        WorkingDirectory = _appDirectory,
                        UseShellExecute = true
                    };

                    Process.Start(processStartInfo);
                    _logger.LogSuccess("Application started successfully");
                    
                    UpdateUI(() =>
                    {
                        StatusTextBlock.Text = "Update completed successfully!";
                        ProgressBar.Value = 100;
                        ProgressTextBlock.Text = "100%";
                        DetailsTextBlock.Text = "Application has been started.";
                        FooterTextBlock.Text = "You can close this window.";
                    });

                    await Task.Delay(2000);

                    // Удаляем временные файлы
                    _logger.LogInfo("Cleaning up temporary files");
                    await CleanupTempFilesAsync();

                    // Закрываем окно
                    _logger.LogInfo("Closing UpdateInstaller window");
                    UpdateUI(() => Close());
                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to start application", ex);
                    UpdateUI(() => ShowError($"Failed to start application: {ex.Message}"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Fatal error in update process", ex);
                UpdateUI(() => ShowError($"Fatal error: {ex.Message}"));
            }
        }

        private async Task CleanupTempFilesAsync()
        {
            try
            {
                var tempFolder = Path.GetDirectoryName(_extractFolder);
                if (Directory.Exists(tempFolder) && tempFolder.Contains("Stalker2ModManager_Update"))
                {
                    await Task.Delay(1000);

                    try
                    {
                        Directory.Delete(tempFolder, true);
                    }
                    catch
                    {
                        // Если не удалось удалить, создаем батник для удаления позже
                        var cleanupScript = Path.Combine(Path.GetTempPath(), "cleanup_update.bat");
                        var scriptContent = $@"@echo off
timeout /t 2 /nobreak >nul
rmdir /S /Q ""{tempFolder}"" >nul 2>&1
del ""%~f0"" >nul 2>&1";
                        File.WriteAllText(cleanupScript, scriptContent);
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = cleanupScript,
                            UseShellExecute = true,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        });
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки очистки
            }
        }

        private void UpdateUI(Action action)
        {
            Dispatcher.Invoke(action);
        }

        private void ShowError(string message)
        {
            StatusTextBlock.Text = "Error";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.IndianRed;
            DetailsTextBlock.Text = message;
            DetailsTextBlock.Foreground = System.Windows.Media.Brushes.IndianRed;
            FooterTextBlock.Text = "Please close this window and try again.";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.LogInfo("UpdateInstaller closed by user");
            Close();
        }
    }
}

