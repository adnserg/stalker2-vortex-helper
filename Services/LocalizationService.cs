using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace Stalker2ModManager.Services
{
    public class LocalizationService
    {
        private static LocalizationService _instance;
        private Dictionary<string, Dictionary<string, string>> _translations;
        private string _currentLanguage = "en";

        public static LocalizationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LocalizationService();
                    _instance.Initialize();
                }
                return _instance;
            }
        }

        public event EventHandler LanguageChanged;

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value && (_translations?.ContainsKey(value) ?? false))
                {
                    _currentLanguage = value;
                    SaveLanguagePreference();
                    LanguageChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void Initialize()
        {
            _translations = new Dictionary<string, Dictionary<string, string>>();

            // Загружаем переводы из JSON файла
            LoadTranslationsFromFile();

            // Загружаем сохраненный язык из конфига
            LoadLanguagePreference();
        }

        private void LoadTranslationsFromFile()
        {
            try
            {
                // Сначала проверяем, есть ли путь к внешнему файлу локализации в конфиге
                var configService = new ConfigService();
                var config = configService.LoadPathsConfig();
                
                if (!string.IsNullOrWhiteSpace(config.CustomLocalizationPath) && File.Exists(config.CustomLocalizationPath))
                {
                    // Загружаем из внешнего файла
                    var json = File.ReadAllText(config.CustomLocalizationPath);
                    _translations = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(json);
                    
                    if (_translations != null && _translations.Count > 0)
                    {
                        Logger.Instance.LogInfo($"Loaded localization from external file: {config.CustomLocalizationPath}");
                        return;
                    }
                }
                
                // Если внешний файл не найден или пуст, загружаем из embedded resource
                LoadTranslationsFromEmbeddedResource();
            }
            catch (Exception ex)
            {
                // Log error and fallback to embedded resource
                Logger.Instance.LogError("Error loading localization file", ex);
                LoadTranslationsFromEmbeddedResource();
            }
        }

        private void LoadTranslationsFromEmbeddedResource()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                
                // Пробуем разные возможные имена ресурсов
                var possibleNames = new[]
                {
                    "Stalker2ModManager.Localization.localization.json",
                    "Stalker2VortexHelper.Localization.localization.json",
                    "Localization.localization.json",
                    "localization.json"
                };
                
                // Также ищем все ресурсы, содержащие "localization.json"
                var allResourceNames = assembly.GetManifestResourceNames();
                var localizationResourceName = allResourceNames.FirstOrDefault(name => 
                    name.EndsWith("localization.json", StringComparison.OrdinalIgnoreCase));
                
                if (localizationResourceName != null)
                {
                    possibleNames = new[] { localizationResourceName }.Concat(possibleNames).Distinct().ToArray();
                }
                
                foreach (var resourceName in possibleNames)
                {
                    using (var stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream != null)
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                var json = reader.ReadToEnd();
                                _translations = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(json);
                                
                                if (_translations != null && _translations.Count > 0)
                                {
                                    Logger.Instance.LogInfo($"Loaded localization from embedded resource: {resourceName}");
                                    return;
                                }
                            }
                        }
                    }
                }
                
                // Fallback to built-in translations if embedded resource not found
                Logger.Instance.LogWarning("Embedded localization resource not found, using built-in translations");
                LoadBuiltInTranslations();
            }
            catch (Exception ex)
            {
                // Log error and fallback to built-in translations
                Logger.Instance.LogError("Error loading localization from embedded resource", ex);
                LoadBuiltInTranslations();
            }
        }
        
        public void LoadFromExternalFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    Logger.Instance.LogWarning($"Localization file not found: {filePath}");
                    return;
                }
                
                var json = File.ReadAllText(filePath);
                var newTranslations = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(json);
                
                if (newTranslations != null && newTranslations.Count > 0)
                {
                    _translations = newTranslations;
                    
                    // Сохраняем путь в конфиге
                    var configService = new ConfigService();
                    var config = configService.LoadPathsConfig();
                    config.CustomLocalizationPath = filePath;
                    configService.SavePathsConfig(config);
                    
                    Logger.Instance.LogInfo($"Loaded localization from external file: {filePath}");
                    
                    // Вызываем событие изменения языка для обновления UI
                    LanguageChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Logger.Instance.LogWarning("External localization file is empty or invalid");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogError("Error loading localization from external file", ex);
                throw;
            }
        }
        
        public void ResetToEmbedded()
        {
            try
            {
                // Очищаем путь к внешнему файлу в конфиге
                var configService = new ConfigService();
                var config = configService.LoadPathsConfig();
                config.CustomLocalizationPath = string.Empty;
                configService.SavePathsConfig(config);
                
                // Загружаем из embedded resource
                LoadTranslationsFromEmbeddedResource();
                
                // Вызываем событие изменения языка для обновления UI
                LanguageChanged?.Invoke(this, EventArgs.Empty);
                
                Logger.Instance.LogInfo("Reset to embedded localization");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogError("Error resetting to embedded localization", ex);
            }
        }

        private void LoadBuiltInTranslations()
        {
            // English translations
            _translations["en"] = new Dictionary<string, string>
            {
                // Window title
                ["WindowTitle"] = "S.T.A.L.K.E.R. 2 Mod Manager",
                
                // Paths section
                ["Paths"] = "Paths",
                ["VortexPath"] = "Vortex Path:",
                ["TargetPath"] = "Target Path:",
                ["Browse"] = "Browse...",
                
                // Actions
                ["LoadMods"] = "Load Mods",
                ["SaveConfig"] = "Save Config",
                ["LoadConfig"] = "Load Config",
                ["ExportOrder"] = "Export Order",
                ["ImportOrder"] = "Import Order",
                ["Advanced"] = "Advanced",
                ["InstallMods"] = "Install Mods",
                
                // Mod list
                ["Mods"] = "Mods",
                ["NoModsLoaded"] = "No mods loaded. Click 'Load Mods' to load mods from Vortex folder.",
                ["MoveUp"] = "↑ Move Up",
                ["MoveDown"] = "↓ Move Down",
                ["SaveChanges"] = "Save",
                ["CancelChanges"] = "Cancel",
                ["OrderHeader"] = "Order",
                ["NameHeader"] = "Mod Name",
                ["TargetFolderHeader"] = "Target Folder (AAA-ModName)",
                
                // Status messages
                ["Status"] = "Ready",
                ["ConfigSaved"] = "Config saved",
                ["ModsLoaded"] = "Mods loaded: {0}",
                ["InstallationInProgress"] = "Installing...",
                ["InstallationCompleted"] = "Installation completed",
                
                // Messages
                ["SelectVortexPath"] = "Please select Vortex path first.",
                ["NoModsLoadedMessage"] = "No mods loaded. Load mods first.",
                ["ConfigSavedSuccess"] = "Config saved successfully.",
                ["OrderExported"] = "Mods order exported successfully.",
                ["OrderImported"] = "Mods order imported successfully.",
                ["ErrorSavingConfig"] = "Error saving config",
                ["ErrorLoadingConfig"] = "Error loading config",
                ["ErrorLoadingMods"] = "Error loading mods",
                ["ErrorInstallingMods"] = "Error installing mods",
                
                // Advanced options
                ["AdvancedOptions"] = "Advanced Options",
                ["SortByFile"] = "Sort mods according to JSON/TXT file",
                ["SelectFile"] = "Select JSON/TXT file:",
                ["Apply"] = "Apply",
                ["Cancel"] = "Cancel",
                
                // Save/Cancel changes
                ["CancelChangesTitle"] = "Cancel Changes",
                ["CancelChangesConfirmation"] = "Cancel all unsaved changes to the mod list?",
                ["UnsavedChangesTitle"] = "Unsaved Changes",
                ["UnsavedChangesConfirmation"] = "You have unsaved changes to the mod list. Save before closing?",
                
                // Window controls
                ["Minimize"] = "Minimize",
                ["Close"] = "Close",
                
                // Dialog titles
                ["Success"] = "Success",
                ["Error"] = "Error",
                ["Warning"] = "Warning",
                ["SelectFolder"] = "Select Folder",
                ["ExportOrderTitle"] = "Export Mods Order",
                ["ImportOrderTitle"] = "Import Mods Order",
                ["SelectJsonFile"] = "Select JSON/TXT File",
                
                // Progress
                ["Cleaning"] = "Cleaning unused mods...",
                ["Installing"] = "Installing...",
                ["Processing"] = "Processing mod: {0}",
            };

            // Russian translations
            _translations["ru"] = new Dictionary<string, string>
            {
                // Window title
                ["WindowTitle"] = "Менеджер модов S.T.A.L.K.E.R. 2",
                
                // Paths section
                ["Paths"] = "Пути",
                ["VortexPath"] = "Путь к Vortex:",
                ["TargetPath"] = "Путь к ~mods:",
                ["Browse"] = "Обзор...",
                
                // Actions
                ["LoadMods"] = "Загрузить моды",
                ["SaveConfig"] = "Сохранить конфиг",
                ["LoadConfig"] = "Загрузить конфиг",
                ["ExportOrder"] = "Экспорт порядка",
                ["ImportOrder"] = "Импорт порядка",
                ["Advanced"] = "Дополнительно",
                ["InstallMods"] = "Установить моды",
                
                // Mod list
                ["Mods"] = "Моды",
                ["NoModsLoaded"] = "Моды не загружены. Нажмите 'Загрузить моды' для загрузки модов из папки Vortex.",
                ["MoveUp"] = "↑ Вверх",
                ["MoveDown"] = "↓ Вниз",
                ["SaveChanges"] = "Сохранить",
                ["CancelChanges"] = "Отменить",
                ["OrderHeader"] = "Порядок",
                ["NameHeader"] = "Название мода",
                ["TargetFolderHeader"] = "Папка назначения (AAA-ModName)",
                
                // Status messages
                ["Status"] = "Готов",
                ["ConfigSaved"] = "Конфиг сохранен",
                ["ModsLoaded"] = "Загружено модов: {0}",
                ["InstallationInProgress"] = "Установка...",
                ["InstallationCompleted"] = "Установка завершена",
                
                // Messages
                ["SelectVortexPath"] = "Пожалуйста, сначала выберите путь к Vortex.",
                ["NoModsLoadedMessage"] = "Моды не загружены. Сначала загрузите моды.",
                ["ConfigSavedSuccess"] = "Конфиг успешно сохранен.",
                ["OrderExported"] = "Порядок модов успешно экспортирован.",
                ["OrderImported"] = "Порядок модов успешно импортирован.",
                ["ErrorSavingConfig"] = "Ошибка сохранения конфига",
                ["ErrorLoadingConfig"] = "Ошибка загрузки конфига",
                ["ErrorLoadingMods"] = "Ошибка загрузки модов",
                ["ErrorInstallingMods"] = "Ошибка установки модов",
                
                // Advanced options
                ["AdvancedOptions"] = "Дополнительные настройки",
                ["SortByFile"] = "Сортировать моды согласно файлу JSON/TXT",
                ["SelectFile"] = "Выберите файл JSON/TXT:",
                ["Apply"] = "Применить",
                ["Cancel"] = "Отмена",
                
                // Save/Cancel changes
                ["CancelChangesTitle"] = "Отменить изменения",
                ["CancelChangesConfirmation"] = "Отменить все несохраненные изменения списка модов?",
                ["UnsavedChangesTitle"] = "Несохраненные изменения",
                ["UnsavedChangesConfirmation"] = "У вас есть несохраненные изменения списка модов. Сохранить перед закрытием?",
                
                // Window controls
                ["Minimize"] = "Свернуть",
                ["Close"] = "Закрыть",
                
                // Dialog titles
                ["Success"] = "Успешно",
                ["Error"] = "Ошибка",
                ["Warning"] = "Предупреждение",
                ["SelectFolder"] = "Выбрать папку",
                ["ExportOrderTitle"] = "Экспорт порядка модов",
                ["ImportOrderTitle"] = "Импорт порядка модов",
                ["SelectJsonFile"] = "Выбрать файл JSON/TXT",
                
                // Progress
                ["Cleaning"] = "Очистка неиспользуемых модов...",
                ["Installing"] = "Установка...",
                ["Processing"] = "Обработка мода: {0}",
            };
        }

        private void LoadLanguagePreference()
        {
            try
            {
                var configService = new ConfigService();
                var config = configService.LoadPathsConfig();
                if (!string.IsNullOrEmpty(config.Language) && _translations.ContainsKey(config.Language))
                {
                    _currentLanguage = config.Language;
                }
                else
                {
                    // Используем язык системы по умолчанию
                    var culture = CultureInfo.CurrentUICulture;
                    _currentLanguage = culture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
                }
            }
            catch
            {
                // Используем язык системы по умолчанию
                var culture = CultureInfo.CurrentUICulture;
                _currentLanguage = culture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
            }
        }

        private void SaveLanguagePreference()
        {
            try
            {
                var configService = new ConfigService();
                var config = configService.LoadPathsConfig();
                config.Language = _currentLanguage;
                configService.SavePathsConfig(config);
            }
            catch
            {
                // Игнорируем ошибки сохранения языка
            }
        }

        public string GetString(string key)
        {
            if (_translations.TryGetValue(_currentLanguage, out var language) &&
                language.TryGetValue(key, out var value))
            {
                return value;
            }

            // Fallback to English
            if (_translations.TryGetValue("en", out var english) &&
                english.TryGetValue(key, out var englishValue))
            {
                return englishValue;
            }

            return key;
        }

        public string GetString(string key, params object[] args)
        {
            var format = GetString(key);
            return string.Format(format, args);
        }

        public List<string> GetAvailableLanguages()
        {
            if (_translations == null)
                return new List<string> { "en" };
            
            return _translations.Keys.ToList();
        }

        public Dictionary<string, string> GetLanguageNames()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Берём человекочитаемое название языка из ключа "localisation" для каждого языка.
            // Никаких доп. тегов или форматирования здесь не добавляем.
            if (_translations != null)
            {
                foreach (var kvp in _translations)
                {
                    var langCode = kvp.Key;
                    var dict = kvp.Value;

                    if (dict != null && dict.TryGetValue("localisation", out var displayName) && 
                        !string.IsNullOrWhiteSpace(displayName))
                    {
                        result[langCode] = displayName;
                    }
                    else
                    {
                        // Fallback — просто код языка, если в словаре нет "localisation"
                        result[langCode] = langCode;
                    }
                }
            }

            // Если по какой-то причине переводов нет — дефолтное значение
            if (result.Count == 0)
            {
                result["English"] = "English";
            }

            return result;
        }
    }
}

