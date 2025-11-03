using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stalker2ModManager.Models;

namespace Stalker2ModManager.Services
{
    public class ModManagerService
    {
        private readonly Logger _logger = Logger.Instance;

        public List<ModInfo> LoadModsFromVortexPath(string vortexPath)
        {
            var mods = new List<ModInfo>();

            if (!Directory.Exists(vortexPath))
            {
                _logger.LogWarning($"Vortex path does not exist: {vortexPath}");
                return mods;
            }

            _logger.LogInfo($"Loading mods from Vortex path: {vortexPath}");

            var directories = Directory.GetDirectories(vortexPath);
            int order = 0;

            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);
                
                // Пропускаем служебные папки
                if (dirInfo.Name.StartsWith("__") || dirInfo.Name == "Better Vaulting")
                {
                    continue;
                }

                var modInfo = new ModInfo
                {
                    SourcePath = dir,
                    Name = dirInfo.Name,
                    Order = order++,
                    IsEnabled = true
                };

                mods.Add(modInfo);
                _logger.LogDebug($"Found mod: {dirInfo.Name}");
            }

            _logger.LogInfo($"Total mods found: {mods.Count}");
            return mods.OrderBy(m => m.Name).ToList();
        }

        public void InstallMods(List<ModInfo> mods, string targetPath)
        {
            InstallModsAsync(mods, targetPath, null).Wait();
        }

        public async Task InstallModsAsync(List<ModInfo> mods, string targetPath, IProgress<InstallProgress> progress)
        {
            await Task.Run(async () =>
            {
                // Создаем целевую папку если её нет
                if (!Directory.Exists(targetPath))
                {
                    Directory.CreateDirectory(targetPath);
                    _logger.LogInfo($"Created target directory: {targetPath}");
                }

                _logger.LogInfo($"Starting installation. Target: {targetPath}, Total mods: {mods.Count}, Enabled: {mods.Count(m => m.IsEnabled)}");

                // Список служебных файлов Vortex, которые нужно сохранить
                var vortexFilesToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "snapshot.json",
                    "rename_folders.py",
                    "update_snapshot.py",
                    "update_deployment.py"
                };

                // Получаем список включенных модов
                var enabledMods = mods.Where(m => m.IsEnabled).OrderBy(m => m.Order).ToList();
                int total = enabledMods.Count;
                int installed = 0;

                // Сначала собираем список всех файлов, которые должны быть (из включенных модов)
                var requiredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var requiredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var mod in enabledMods)
                {
                    var targetFolderName = mod.GetTargetFolderName();
                    var targetModPath = Path.Combine(targetPath, targetFolderName);
                    requiredDirectories.Add(targetModPath);
                    
                    // Собираем все файлы, которые должны быть скопированы из этого мода
                    CollectFiles(mod.SourcePath, targetModPath, requiredFiles);
                }

                // Удаляем папки, которых нет в списке включенных модов
                progress?.Report(new InstallProgress
                {
                    CurrentMod = "Cleaning unused mods...",
                    Installed = 0,
                    Total = total,
                    Percentage = 0
                });
                
                await CleanUnusedModsAsync(targetPath, requiredDirectories, vortexFilesToKeep);

                _logger.LogInfo($"Installing {total} enabled mods (only changed files will be copied)...");

                // Копируем включенные моды в правильном порядке, копируя только измененные файлы
                foreach (var mod in enabledMods)
                {
                    var targetFolderName = mod.GetTargetFolderName();
                    var targetModPath = Path.Combine(targetPath, targetFolderName);

                    if (!Directory.Exists(targetModPath))
                    {
                        Directory.CreateDirectory(targetModPath);
                    }

                    // Обновляем прогресс
                    installed++;
                    var percentage = total > 0 ? (int)((double)installed / total * 100) : 0;
                    progress?.Report(new InstallProgress
                    {
                        CurrentMod = mod.Name,
                        Installed = installed,
                        Total = total,
                        Percentage = percentage
                    });

                    _logger.LogInfo($"Processing mod [{installed}/{total}]: {mod.Name} -> {targetFolderName}");

                    // Копируем только измененные файлы
                    int copiedCount = await CopyDirectoryAsync(mod.SourcePath, targetModPath, true);
                    
                    if (copiedCount > 0)
                    {
                        _logger.LogDebug($"Mod '{mod.Name}': copied {copiedCount} changed/new files");
                    }
                    else
                    {
                        _logger.LogDebug($"Mod '{mod.Name}': all files are up to date, skipped");
                    }
                }

                _logger.LogSuccess($"Installation completed. Installed {installed} mods");
            });
        }

        private void CopyDirectory(string sourceDir, string targetDir)
        {
            CopyDirectoryAsync(sourceDir, targetDir).Wait();
        }

        private void CollectFiles(string sourceDir, string targetDir, HashSet<string> fileSet)
        {
            try
            {
                if (!Directory.Exists(sourceDir)) return;

                var files = Directory.GetFiles(sourceDir);
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(sourceDir, file);
                    var targetFilePath = Path.Combine(targetDir, relativePath);
                    fileSet.Add(targetFilePath);
                }

                var dirs = Directory.GetDirectories(sourceDir);
                foreach (var dir in dirs)
                {
                    var dirName = Path.GetFileName(dir);
                    var targetSubDir = Path.Combine(targetDir, dirName);
                    CollectFiles(dir, targetSubDir, fileSet);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error collecting files from {sourceDir}: {ex.Message}");
            }
        }

        private async Task<int> CopyDirectoryAsync(string sourceDir, string targetDir, bool skipExisting = false)
        {
            int copiedCount = 0;
            
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(targetDir);

                // Копируем файлы
                var files = Directory.GetFiles(sourceDir);
                foreach (var file in files)
                {
                    await Task.Run(() =>
                    {
                        var fileName = Path.GetFileName(file);
                        var targetFilePath = Path.Combine(targetDir, fileName);
                        
                        // Если нужно пропускать существующие файлы, проверяем их
                        if (skipExisting && File.Exists(targetFilePath))
                        {
                            if (AreFilesIdentical(file, targetFilePath))
                            {
                                return; // Файлы идентичны, пропускаем
                            }
                        }
                        
                        File.Copy(file, targetFilePath, true);
                        copiedCount++;
                    });
                }

                // Рекурсивно копируем подпапки
                var dirs = Directory.GetDirectories(sourceDir);
                foreach (var dir in dirs)
                {
                    var dirName = Path.GetFileName(dir);
                    var targetSubDir = Path.Combine(targetDir, dirName);
                    copiedCount += await CopyDirectoryAsync(dir, targetSubDir, skipExisting);
                }
            });
            
            return copiedCount;
        }

        private bool AreFilesIdentical(string sourceFile, string targetFile)
        {
            try
            {
                var sourceInfo = new FileInfo(sourceFile);
                var targetInfo = new FileInfo(targetFile);
                
                // Сравниваем размер и дату изменения
                if (sourceInfo.Length != targetInfo.Length)
                {
                    return false;
                }
                
                // Если размер совпадает и дата изменения целевого файла новее или равна исходному,
                // считаем файлы идентичными (для ускорения не проверяем содержимое)
                // Можно улучшить, добавив проверку хеша, но это будет медленнее
                return targetInfo.LastWriteTime >= sourceInfo.LastWriteTime;
            }
            catch
            {
                // При ошибке считаем, что файлы разные и нужно скопировать
                return false;
            }
        }

        private async Task CleanUnusedModsAsync(string targetPath, HashSet<string> requiredDirectories, HashSet<string> vortexFilesToKeep)
        {
            await Task.Run(async () =>
            {
                // Удаляем папки, которых нет в списке включенных модов
                var existingDirs = Directory.GetDirectories(targetPath);
                foreach (var dir in existingDirs)
                {
                    if (!requiredDirectories.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            RemoveReadOnlyAttributes(dirInfo);
                            await Task.Run(() => Directory.Delete(dir, true));
                            _logger.LogDebug($"Deleted unused mod directory: {Path.GetFileName(dir)}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to delete unused directory: {dir}", ex);
                        }
                    }
                }

                // Удаляем файлы в корне, которых нет в исходных модах (кроме служебных Vortex)
                var existingFiles = Directory.GetFiles(targetPath);
                foreach (var file in existingFiles)
                {
                    var fileName = Path.GetFileName(file);
                    
                    // Пропускаем служебные файлы Vortex
                    bool isVortexFile = fileName.StartsWith("vortex.", StringComparison.OrdinalIgnoreCase) ||
                                       fileName.StartsWith("snapshot_", StringComparison.OrdinalIgnoreCase) ||
                                       vortexFilesToKeep.Contains(fileName);
                    
                    if (isVortexFile)
                    {
                        continue;
                    }

                    // Удаляем файл, если его нет в списке требуемых
                    // (для корневых файлов мы не собираем список, так что удаляем все ненужные)
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Exists)
                        {
                            fileInfo.Attributes = FileAttributes.Normal;
                            await Task.Run(() => File.Delete(file));
                            _logger.LogDebug($"Deleted unused file: {fileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to delete unused file: {file}", ex);
                    }
                }
            });
        }

        private void RemoveReadOnlyAttributes(DirectoryInfo directoryInfo)
        {
            try
            {
                if (!directoryInfo.Exists) return;

                directoryInfo.Attributes &= ~FileAttributes.ReadOnly;

                // Убираем атрибуты только для чтения у всех файлов в папке
                foreach (var file in directoryInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    file.Attributes &= ~FileAttributes.ReadOnly;
                }

                // Рекурсивно для всех подпапок
                foreach (var subDir in directoryInfo.GetDirectories("*", SearchOption.AllDirectories))
                {
                    subDir.Attributes &= ~FileAttributes.ReadOnly;
                }
            }
            catch
            {
                // Игнорируем ошибки при изменении атрибутов
            }
        }

        public string GetDefaultTargetPath()
        {
            // Ищем установку S.T.A.L.K.E.R. 2 в стандартных местах Steam
            var steamPaths = new[]
            {
                @"E:\SteamLibrary\steamapps\common\S.T.A.L.K.E.R. 2 Heart of Chornobyl\Stalker2\Content\Paks\~mods",
                @"C:\Program Files (x86)\Steam\steamapps\common\S.T.A.L.K.E.R. 2 Heart of Chornobyl\Stalker2\Content\Paks\~mods",
                @"D:\SteamLibrary\steamapps\common\S.T.A.L.K.E.R. 2 Heart of Chornobyl\Stalker2\Content\Paks\~mods"
            };

            foreach (var path in steamPaths)
            {
                var gameDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(path))));
                if (Directory.Exists(gameDir))
                {
                    return path;
                }
            }

            return string.Empty;
        }
    }
}

