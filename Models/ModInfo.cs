using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Stalker2ModManager.Models
{
    public class ModInfo : INotifyPropertyChanged
    {
        private string _sourcePath = string.Empty;
        private string _name = string.Empty;
        private bool _isEnabled = true;
        private int _order;
        
        // Отображаемое имя мода в UI (может отличаться от реального имени папки)
        // Если не задано, в качестве DisplayName используется Name
        private string _displayName = string.Empty;
        
        // Признак "основной" версии мода для отображения в списке.
        // Если у базового имени несколько версий, в списке показывается только одна (IsPrimaryVersion = true),
        // остальные остаются в коллекции, но скрываются фильтром.
        private bool _isPrimaryVersion = true;
        
        // Признак того, что у мода есть несколько версий (несколько папок с одним базовым именем).
        // Используется только для визуального индикатора в списке.
        private bool _hasMultipleVersions;
        
        // Локализованная подсказка для индикатора нескольких версий.
        private string _multipleVersionsTooltip = string.Empty;
        
        // Количество версий мода с этим базовым именем, которые включены для установки.
        // Используется для визуального предупреждения о множественной установке.
        private int _installedVersionsCount = 1;
        
        // Словарь для хранения информации о включенных/отключенных файлах
        // Ключ - относительный путь файла от SourcePath, значение - включен ли файл
        private Dictionary<string, bool> _fileStates = new Dictionary<string, bool>();

        // Агрегированный флаг отключённых файлов по группе версий (используется для главного списка).
        private bool _aggregatedHasDisabledFiles;

        // Групповой флаг "мод включён" для отображения чекбокса в главном списке (по всем версиям).
        private bool _groupIsEnabled;

        // Локализованный текст подсказки для индикатора отключённых файлов.
        private string _disabledFilesTooltip = string.Empty;

        // Есть ли вообще файлы в папке мода (используется для индикации пустых модов и блокировки установки).
        private bool _hasAnyFiles = true;

        public string SourcePath
        {
            get => _sourcePath;
            set
            {
                _sourcePath = value;
                OnPropertyChanged(nameof(SourcePath));
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(TargetFolderName));
                // Если DisplayName не задан явно, обновляем его вместе с Name
                if (string.IsNullOrEmpty(_displayName))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                // Запрещаем включать мод, если у него нет файлов или если для него не осталось ни одного включенного файла.
                if (value && (!HasAnyFiles || !HasAnyEnabledFiles))
                {
                    return;
                }

                if (_isEnabled == value) return; // предотвращаем лишние уведомления и возможные циклы
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }

        public int Order
        {
            get => _order;
            set
            {
                _order = value;
                OnPropertyChanged(nameof(Order));
                OnPropertyChanged(nameof(TargetFolderName));
            }
        }

        /// <summary>
        /// Имя мода, отображаемое в UI.
        /// По умолчанию совпадает с Name, но может быть переопределено (например, чтобы показать базовое имя + версию).
        /// </summary>
        public string DisplayName
        {
            get => string.IsNullOrEmpty(_displayName) ? _name : _displayName;
            set
            {
                _displayName = value ?? string.Empty;
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        /// <summary>
        /// Является ли эта запись "основной" версией мода, отображаемой в главном списке.
        /// </summary>
        public bool IsPrimaryVersion
        {
            get => _isPrimaryVersion;
            set
            {
                if (_isPrimaryVersion == value) return;
                _isPrimaryVersion = value;
                OnPropertyChanged(nameof(IsPrimaryVersion));
            }
        }

        /// <summary>
        /// Есть ли у мода несколько версий (для отображения индикатора в списке).
        /// </summary>
        public bool HasMultipleVersions
        {
            get => _hasMultipleVersions;
            set
            {
                if (_hasMultipleVersions == value) return;
                _hasMultipleVersions = value;
                OnPropertyChanged(nameof(HasMultipleVersions));
            }
        }

        /// <summary>
        /// Текст подсказки для индикатора нескольких версий (локализованный).
        /// </summary>
        public string MultipleVersionsTooltip
        {
            get => _multipleVersionsTooltip;
            set
            {
                if (_multipleVersionsTooltip == value) return;
                _multipleVersionsTooltip = value ?? string.Empty;
                OnPropertyChanged(nameof(MultipleVersionsTooltip));
            }
        }

        /// <summary>
        /// Количество версий этого мода (по базовому имени), которые включены для установки.
        /// </summary>
        public int InstalledVersionsCount
        {
            get => _installedVersionsCount;
            set
            {
                if (_installedVersionsCount == value) return;
                _installedVersionsCount = value < 1 ? 1 : value;
                OnPropertyChanged(nameof(InstalledVersionsCount));
                OnPropertyChanged(nameof(HasMultipleInstalledVersions));
            }
        }

        /// <summary>
        /// Флаг, что для данного мода включено более одной версии (будут установлены несколько версий).
        /// </summary>
        public bool HasMultipleInstalledVersions => _installedVersionsCount > 1;

        /// <summary>
        /// Локализованный текст подсказки для индикатора отключённых файлов.
        /// </summary>
        public string DisabledFilesTooltip
        {
            get => _disabledFilesTooltip;
            set
            {
                if (_disabledFilesTooltip == value) return;
                _disabledFilesTooltip = value ?? string.Empty;
                OnPropertyChanged(nameof(DisabledFilesTooltip));
            }
        }

        /// <summary>
        /// Есть ли вообще файлы в папке мода.
        /// </summary>
        public bool HasAnyFiles
        {
            get => _hasAnyFiles;
            set
            {
                if (_hasAnyFiles == value) return;
                _hasAnyFiles = value;
                OnPropertyChanged(nameof(HasAnyFiles));
            }
        }

        /// <summary>
        /// Есть ли хотя бы один файл, который будет установлен (не отключён пользователем).
        /// </summary>
        public bool HasAnyEnabledFiles
        {
            get
            {
                if (!HasAnyFiles)
                {
                    return false;
                }

                if (_fileStates == null || _fileStates.Count == 0)
                {
                    // Если нет явных состояний файлов, считаем, что все существующие файлы включены.
                    return true;
                }

                return _fileStates.Values.Any(v => v);
            }
        }

        /// <summary>
        /// Групповое состояние "включённости" мода (по всем версиям) для чекбокса в главном списке.
        /// </summary>
        public bool GroupIsEnabled
        {
            get => _groupIsEnabled;
            set
            {
                if (_groupIsEnabled == value) return;
                _groupIsEnabled = value;
                OnPropertyChanged(nameof(GroupIsEnabled));
            }
        }

        public Dictionary<string, bool> FileStates
        {
            get => _fileStates;
            set
            {
                _fileStates = value ?? new Dictionary<string, bool>();
                OnPropertyChanged(nameof(FileStates));
                OnPropertyChanged(nameof(HasDisabledFiles));
                OnPropertyChanged(nameof(AggregatedHasDisabledFiles));
            }
        }

        public string TargetFolderName
        {
            get => GetTargetFolderName();
        }

        public string GetTargetFolderName()
        {
            // Генерируем префикс на основе порядка: AAA, AAB, AAC, и т.д.
            string prefix = GetPrefixFromOrder(_order);
            return $"{prefix}-{Name}";
        }

        private string GetPrefixFromOrder(int order)
        {
            // AAA (0), AAB (1), AAC (2), ..., AAZ (25), ABA (26), ABB (27), ..., ABZ (51), ACA (52), и т.д.
            // Третья буква меняется каждые 1 мод: A-Z (0-25)
            // Вторая буква меняется каждые 26 модов: A-Z (0-25)
            // Первая буква начинается с A, для order < 676 остается A
            
            int thirdLetter = order % 26; // 0-25 -> A-Z
            int secondLetter = (order / 26) % 26; // 0-25 -> A-Z
            int firstLetter = order / 676; // 0 -> A, 1 -> B, и т.д.
            
            if (firstLetter == 0)
            {
                // AAA - AZZ (порядки 0-675)
                return $"A{(char)('A' + secondLetter)}{(char)('A' + thirdLetter)}";
            }
            else
            {
                // BAA и далее (порядки 676+)
                return $"{(char)('A' + firstLetter)}{(char)('A' + secondLetter)}{(char)('A' + thirdLetter)}";
            }
        }

        /// <summary>
        /// Проверяет, включен ли файл. Если файл не в словаре, возвращает true (по умолчанию включен).
        /// </summary>
        public bool IsFileEnabled(string relativePath)
        {
            if (_fileStates.TryGetValue(relativePath, out bool isEnabled))
            {
                return isEnabled;
            }
            return true; // По умолчанию все файлы включены
        }

        /// <summary>
        /// Устанавливает состояние файла (включен/отключен).
        /// </summary>
        public void SetFileEnabled(string relativePath, bool isEnabled)
        {
            _fileStates[relativePath] = isEnabled;
            OnPropertyChanged(nameof(FileStates));
            OnPropertyChanged(nameof(HasDisabledFiles));
            OnPropertyChanged(nameof(AggregatedHasDisabledFiles));
        }

        /// <summary>
        /// Проверяет, есть ли у мода отключенные файлы.
        /// </summary>
        public bool HasDisabledFiles
        {
            get
            {
                if (_fileStates.Count == 0)
                    return false;
                
                // Проверяем, есть ли хотя бы один отключенный файл
                return _fileStates.Values.Any(enabled => !enabled);
            }
        }

        /// <summary>
        /// Агрегированный флаг отключённых файлов для отображения в главном списке.
        /// Для одиночного мода равен HasDisabledFiles, для группы версий устанавливается из вне.
        /// </summary>
        public bool AggregatedHasDisabledFiles
        {
            get => _aggregatedHasDisabledFiles || HasDisabledFiles;
            set
            {
                if (_aggregatedHasDisabledFiles == value) return;
                _aggregatedHasDisabledFiles = value;
                OnPropertyChanged(nameof(AggregatedHasDisabledFiles));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

