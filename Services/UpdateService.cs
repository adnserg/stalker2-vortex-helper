using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Stalker2ModManager.Models;

namespace Stalker2ModManager.Services
{
    public class UpdateService : IDisposable
    {
        private readonly string _updateServerUrl = "https://dev.wow-crystal.ru/FOR_SERVER/Stalker2vortexhelper/";
        private readonly string _archiveFileName = "s2vh.7z";
        private readonly string _updateCheckInfoPath = "update_check.json";
        private readonly HttpClient _httpClient;
        private readonly Logger _logger;

        public UpdateService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _logger = Logger.Instance;
        }

        public string GetCurrentVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var versionInfo = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (versionInfo != null && !string.IsNullOrEmpty(versionInfo.InformationalVersion))
                {
                    return versionInfo.InformationalVersion;
                }

                var version = assembly.GetName().Version;
                return version?.ToString() ?? "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }

        public async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                var archiveUrl = _updateServerUrl + _archiveFileName;
                _logger.LogInfo($"Checking for updates at: {archiveUrl}");

                // Используем HEAD запрос чтобы не скачивать весь архив
                var request = new HttpRequestMessage(HttpMethod.Head, archiveUrl);
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Failed to check for updates. Status code: {response.StatusCode}");
                    return null;
                }

                // Получаем дату последней модификации из заголовка
                var lastModifiedHeader = response.Content.Headers.LastModified;
                if (!lastModifiedHeader.HasValue)
                {
                    _logger.LogWarning("Last-Modified header is not available");
                    return null;
                }

                var serverLastModified = lastModifiedHeader.Value.UtcDateTime;
                var serverFileSize = response.Content.Headers.ContentLength ?? 0;

                _logger.LogInfo($"Server archive last modified: {serverLastModified:yyyy-MM-dd HH:mm:ss}, Size: {serverFileSize} bytes");

                // Загружаем информацию о последней проверке
                var lastCheckInfo = LoadUpdateCheckInfo();
                
                // Если это первая проверка или файл на сервере новее/изменен
                bool isUpdateAvailable = false;
                if (lastCheckInfo == null)
                {
                    // Первая проверка - сохраняем информацию, но не показываем обновление
                    _logger.LogInfo("First update check - saving archive info");
                    SaveUpdateCheckInfo(new UpdateCheckInfo
                    {
                        LastModifiedDate = serverLastModified,
                        FileSize = serverFileSize
                    });
                    return null;
                }
                else
                {
                    // Проверяем, изменился ли файл
                    if (serverLastModified > lastCheckInfo.LastModifiedDate || 
                        serverFileSize != lastCheckInfo.FileSize)
                    {
                        isUpdateAvailable = true;
                        _logger.LogInfo($"Update available: Server date {serverLastModified:yyyy-MM-dd HH:mm:ss} vs local {lastCheckInfo.LastModifiedDate:yyyy-MM-dd HH:mm:ss}");
                    }
                    else
                    {
                        _logger.LogInfo("No updates available - archive is the same");
                    }
                }

                if (isUpdateAvailable)
                {
                    // Обновляем сохраненную информацию
                    SaveUpdateCheckInfo(new UpdateCheckInfo
                    {
                        LastModifiedDate = serverLastModified,
                        FileSize = serverFileSize
                    });

                    var currentVersion = GetCurrentVersion();
                    return new UpdateInfo
                    {
                        Version = $"Updated {serverLastModified:yyyy-MM-dd HH:mm:ss}",
                        DownloadUrl = GetDownloadUrl()
                    };
                }

                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("Network error while checking for updates", ex);
                return null;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError("Timeout while checking for updates", ex);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error checking for updates", ex);
                return null;
            }
        }

        private UpdateCheckInfo LoadUpdateCheckInfo()
        {
            if (!File.Exists(_updateCheckInfoPath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(_updateCheckInfoPath);
                return JsonConvert.DeserializeObject<UpdateCheckInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        private void SaveUpdateCheckInfo(UpdateCheckInfo info)
        {
            try
            {
                var json = JsonConvert.SerializeObject(info, Formatting.Indented);
                File.WriteAllText(_updateCheckInfoPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to save update check info", ex);
            }
        }

        public string GetDownloadUrl()
        {
            return _updateServerUrl + _archiveFileName;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

