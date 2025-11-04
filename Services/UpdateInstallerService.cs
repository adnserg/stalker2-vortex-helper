using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Stalker2ModManager.Services
{
    public class UpdateInstallerService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _tempFolder;
        private readonly string _updateArchivePath;
        private readonly string _extractFolder;
        private readonly string _updateServerUrl = "https://dev.wow-crystal.ru/FOR_SERVER/Stalker2vortexhelper/";
        private readonly string _updaterExeName = "UpdateInstaller.exe";

        public UpdateInstallerService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(10); // Больше времени для скачивания
            _logger = Logger.Instance;
            
            _tempFolder = Path.Combine(Path.GetTempPath(), "Stalker2ModManager_Update");
            _updateArchivePath = Path.Combine(_tempFolder, "update.7z");
            _extractFolder = Path.Combine(_tempFolder, "extracted");
        }

        public async Task<bool> DownloadUpdateAsync(string downloadUrl, IProgress<double> progress)
        {
            try
            {
                _logger.LogInfo($"Starting update download from: {downloadUrl}");

                // Создаем временную папку
                if (Directory.Exists(_tempFolder))
                {
                    Directory.Delete(_tempFolder, true);
                }
                Directory.CreateDirectory(_tempFolder);

                // Скачиваем архив
                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    _logger.LogInfo($"Archive size: {totalBytes} bytes");

                    using (var fileStream = new FileStream(_updateArchivePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192];
                        long totalBytesRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                var percentage = (double)totalBytesRead / totalBytes * 100;
                                progress?.Report(percentage);
                            }
                        }
                    }
                }

                _logger.LogSuccess($"Update archive downloaded successfully: {_updateArchivePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error downloading update", ex);
                return false;
            }
        }

        public bool ExtractArchive()
        {
            try
            {
                _logger.LogInfo($"Extracting archive: {_updateArchivePath}");

                if (!File.Exists(_updateArchivePath))
                {
                    _logger.LogError("Update archive not found");
                    return false;
                }

                // Создаем папку для распаковки
                if (Directory.Exists(_extractFolder))
                {
                    Directory.Delete(_extractFolder, true);
                }
                Directory.CreateDirectory(_extractFolder);

                // Распаковываем архив
                using (var archive = ArchiveFactory.Open(_updateArchivePath))
                {
                    int totalEntries = 0;
                    int extractedEntries = 0;

                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            totalEntries++;
                        }
                    }

                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            var destinationPath = Path.Combine(_extractFolder, entry.Key);
                            var destinationDir = Path.GetDirectoryName(destinationPath);
                            
                            if (!string.IsNullOrEmpty(destinationDir))
                            {
                                Directory.CreateDirectory(destinationDir);
                            }

                            using (var entryStream = entry.OpenEntryStream())
                            using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
                            {
                                entryStream.CopyTo(fileStream);
                            }

                            extractedEntries++;
                            _logger.LogDebug($"Extracted: {entry.Key} ({extractedEntries}/{totalEntries})");
                        }
                    }
                }

                _logger.LogSuccess($"Archive extracted successfully to: {_extractFolder}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error extracting archive", ex);
                return false;
            }
        }

        public async Task<bool> PrepareUpdateInstallerAsync(IProgress<double> progress = null)
        {
            try
            {
                _logger.LogInfo("Preparing update installer");

                var appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var appDirectory = Path.GetDirectoryName(appPath);
                var updaterExePath = Path.Combine(appDirectory, _updaterExeName);
                
                // Если UpdateInstaller.exe уже есть, используем его
                if (File.Exists(updaterExePath))
                {
                    _logger.LogInfo($"UpdateInstaller.exe found at: {updaterExePath}");
                    progress?.Report(100);
                    return true;
                }

                // Если нет, скачиваем с сервера
                _logger.LogInfo($"UpdateInstaller.exe not found. Downloading from server...");
                var updaterUrl = _updateServerUrl + _updaterExeName;
                progress?.Report(0);

                try
                {
                    using (var response = await _httpClient.GetAsync(updaterUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? 0;
                        _logger.LogInfo($"UpdateInstaller.exe size: {totalBytes} bytes");

                        using (var fileStream = new FileStream(updaterExePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        {
                            var buffer = new byte[8192];
                            long totalBytesRead = 0;
                            int bytesRead;

                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalBytesRead += bytesRead;

                                if (totalBytes > 0)
                                {
                                    var percentage = (double)totalBytesRead / totalBytes * 100;
                                    progress?.Report(percentage);
                                }
                            }
                        }
                    }

                    _logger.LogSuccess($"UpdateInstaller.exe downloaded successfully to: {updaterExePath}");
                    progress?.Report(100);
                    return true;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError($"Failed to download UpdateInstaller.exe from server: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error preparing update installer", ex);
                return false;
            }
        }

        public bool StartUpdateProcess()
        {
            try
            {
                var appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var appDirectory = Path.GetDirectoryName(appPath);
                var appExeName = Path.GetFileName(appPath);
                var updaterExePath = Path.Combine(appDirectory, _updaterExeName);
                
                if (!File.Exists(updaterExePath))
                {
                    _logger.LogError($"UpdateInstaller.exe not found at: {updaterExePath}");
                    return false;
                }

                _logger.LogInfo("Starting update installer process");

                // Параметры: appDirectory, extractFolder, appExeName, waitTimeout
                var arguments = $"\"{appDirectory}\" \"{_extractFolder}\" \"{appExeName}\" 30";

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = updaterExePath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                Process.Start(processStartInfo);
                _logger.LogSuccess("Update installer process started");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error starting update installer process", ex);
                return false;
            }
        }

        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_tempFolder))
                {
                    // Не удаляем папку сразу, так как батник её использует
                    // Батник сам удалит её после обновления
                    _logger.LogInfo("Cleanup will be performed by update script");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during cleanup", ex);
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

