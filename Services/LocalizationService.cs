using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

            // Загружаем переводы из встроенных ресурсов
            LoadBuiltInTranslations();

            // Загружаем сохраненный язык из конфига
            LoadLanguagePreference();
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
    }
}

