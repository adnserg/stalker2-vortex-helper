using Stalker2ModManager.Helpers;
using Stalker2ModManager.Models;
using Stalker2ModManager.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;

namespace Stalker2ModManager.Views
{
    public partial class MainWindow : Window
    {
        private readonly ModManagerService _modManagerService;
        private readonly ConfigService _configService;
        private readonly Services.Logger _logger;
        private readonly Services.LocalizationService _localization;
        private readonly ObservableCollection<ModInfo> _mods;
        private readonly System.Windows.Data.CollectionViewSource _modsViewSource;
        private ModInfo? _draggedMod;
        private Point _dragStartPoint;
        private readonly System.Windows.Threading.DispatcherTimer _scrollTimer;
        private System.Windows.Controls.ListBoxItem? _draggedListItem;
        private InsertionLineAdorner? _insertionLineAdorner;
        
        // Состояние для отслеживания изменений списка модов
        private readonly List<ModInfo> _originalModsState = [];
        private bool _hasUnsavedChanges = false;
        private bool _isClosing = false;
        private bool _isApplyingOrder = false;
        // Флаг для предотвращения рекурсивных вызовов при обновлении отображаемых имён и агрегированного состояния
        private bool _isUpdatingDisplayNames = false;
        
        // Для отмены установки модов
        private CancellationTokenSource? _installCancellationTokenSource;
        private bool _isInstalling = false;
        
        // Для сортировки таблицы (без изменения реального порядка)
        private string _currentSortProperty = "Order";
        private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;

        public MainWindow()
        {
            InitializeComponent();
            _modManagerService = new ModManagerService();
            _configService = new ConfigService();
            _logger = Services.Logger.Instance;
            _localization = Services.LocalizationService.Instance;
            _mods = [];
            _modsViewSource = new System.Windows.Data.CollectionViewSource
            {
                Source = _mods
            };
            _modsViewSource.Filter += ModsViewSource_Filter;
            ModsListBox.ItemsSource = _modsViewSource.View;
            
            // Устанавливаем начальную сортировку по Order
            ApplySorting();

            _logger.LogInfo("Application started");
            
            // Загружаем и отображаем версию
            LoadVersion();
            
            LoadConfig();
            
            // Сохраняем исходное состояние модов
            SaveOriginalModsState();
            
            // Подписываемся на изменение языка
            _localization.LanguageChanged += Localization_LanguageChanged;
            
            // Подписываемся на изменения модов для отслеживания несохраненных изменений
            _mods.CollectionChanged += Mods_CollectionChanged;
            
            // Инициализируем ComboBox языков
            InitializeLanguageComboBox();
            
            // Инициализируем локализацию
            UpdateLocalization();
            
            // Подписываемся на изменение размера окна
            SizeChanged += MainWindow_SizeChanged;
            
            // Инициализируем таймер для автоскролла
            _scrollTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(30) // Обновляем каждые 30мс для плавности
            };
            _scrollTimer.Tick += ScrollTimer_Tick;

            CheckGameAvailability();
            // Проверяем обновления при запуске (асинхронно, не блокируя UI)
            CheckForUpdatesAsync();
        }

        private void ScrollTimer_Tick(object? sender, EventArgs e)
        {
            if (ModsListBox == null)
            {
                _scrollTimer.Stop();
                return;
            }

            var scrollViewer = GetScrollViewer(ModsListBox);
            if (scrollViewer == null)
            {
                _scrollTimer.Stop();
                return;
            }

            // Получаем позицию мыши относительно ListBox
            var mousePos = System.Windows.Input.Mouse.GetPosition(ModsListBox);
            var scrollArea = 50.0; // Область автоскролла (пиксели от края)
            var baseScrollSpeed = 1.5; // Базовая скорость скролла
            var maxScrollSpeed = 4.0; // Максимальная скорость скролла

            // Проверяем позицию мыши относительно ListBox
            if (mousePos.Y < scrollArea && mousePos.Y >= 0)
            {
                // Скроллим вверх с адаптивной скоростью (чем ближе к краю, тем быстрее)
                var distanceFromEdge = mousePos.Y;
                var speedFactor = 1.0 - (distanceFromEdge / scrollArea); // От 0 до 1
                var scrollSpeed = baseScrollSpeed + (maxScrollSpeed - baseScrollSpeed) * speedFactor;
                
                var newOffset = scrollViewer.VerticalOffset - scrollSpeed;
                if (newOffset >= 0)
                {
                    scrollViewer.ScrollToVerticalOffset(newOffset);
                }
                else
                {
                    scrollViewer.ScrollToVerticalOffset(0);
                    _scrollTimer.Stop();
                }
            }
            else if (mousePos.Y > ModsListBox.ActualHeight - scrollArea && mousePos.Y <= ModsListBox.ActualHeight)
            {
                // Скроллим вниз с адаптивной скоростью
                var distanceFromEdge = ModsListBox.ActualHeight - mousePos.Y;
                var speedFactor = 1.0 - (distanceFromEdge / scrollArea); // От 0 до 1
                var scrollSpeed = baseScrollSpeed + (maxScrollSpeed - baseScrollSpeed) * speedFactor;
                
                var newOffset = scrollViewer.VerticalOffset + scrollSpeed;
                var maxOffset = scrollViewer.ScrollableHeight;
                if (newOffset <= maxOffset)
                {
                    scrollViewer.ScrollToVerticalOffset(newOffset);
                }
                else
                {
                    scrollViewer.ScrollToVerticalOffset(maxOffset);
                    _scrollTimer.Stop();
                }
            }
            else
            {
                // Мышь в середине - останавливаем скролл
                _scrollTimer.Stop();
            }
        }

        /// <summary>
        /// Обновляет отображаемые имена модов (DisplayName), чтобы пользователю было видно,
        /// когда под одним базовым названием есть несколько версий (разные папки).
        /// </summary>
        private void UpdateModsDisplayNames()
        {
            if (_isUpdatingDisplayNames)
            {
                return;
            }

            if (_mods == null || _mods.Count == 0)
            {
                return;
            }

            _isUpdatingDisplayNames = true;

            // Группируем моды по базовому имени, очищенному от числовых суффиксов Nexus (NormalizeOrderName)
            var groups = _mods
                .GroupBy(m => NormalizeOrderName(m.Name), StringComparer.InvariantCultureIgnoreCase)
                .ToList();

            try
            {
                foreach (var group in groups)
                {
                    var modsInGroup = group.ToList();

                    if (modsInGroup.Count == 1)
                    {
                        // Одиночный мод: отображаем исходное имя
                        var single = modsInGroup[0];
                        single.DisplayName = single.Name;
                        single.IsPrimaryVersion = true;
                        single.HasMultipleVersions = false;
                        single.InstalledVersionsCount = single.IsEnabled ? 1 : 0;
                        single.MultipleVersionsTooltip = string.Empty;

                        // Групповой чекбокс включён только если мод можно установить (есть файлы и есть хотя бы один включенный файл)
                        bool canInstall = single.HasAnyFiles && single.HasAnyEnabledFiles;
                        single.GroupIsEnabled = single.IsEnabled && canInstall;

                        // Подсказка и предупреждение:
                        // - если файлов нет, показываем текст "нет файлов" и всегда показываем индикатор
                        // - если файлы есть и мод включён, но есть отключённые файлы, показываем предупреждение
                        if (!single.HasAnyFiles)
                        {
                            single.DisabledFilesTooltip = _localization.GetString("NoFilesFound") ?? "No files found in this mod folder";
                            single.AggregatedHasDisabledFiles = true;
                        }
                        else
                        {
                            single.DisabledFilesTooltip = _localization.GetString("SomeFilesDisabled") ?? "Some files are disabled";
                            bool singleHasDisabledFiles = single.IsEnabled && single.HasDisabledFiles;
                            single.AggregatedHasDisabledFiles = singleHasDisabledFiles;
                        }
                    }
                    else
                    {
                        string baseName = group.Key;
                        // Считаем, сколько версий включены для установки
                        var enabledMods = modsInGroup.Where(m => m.IsEnabled).ToList();
                        int enabledCount = enabledMods.Count;
                        bool anyEnabled = enabledCount > 0;

                        // Сохраняем уже выбранную "основную" версию, если она есть в группе
                        var existingPrimary = modsInGroup.FirstOrDefault(m => m.IsPrimaryVersion);

                        ModInfo primary;
                        if (existingPrimary != null)
                        {
                            primary = existingPrimary;
                        }
                        else
                        {
                            // Если первичная версия не помечена, выбираем мод с "максимальным" именем (часто это последняя версия)
                            primary = modsInGroup
                                .OrderBy(m => m.Name, StringComparer.InvariantCultureIgnoreCase)
                                .Last();
                        }

                        // Выбираем отображаемое имя в зависимости от количества включённых версий:
                        // 0 включённых  -> просто базовое имя
                        // 1 включённая  -> базовое имя + выбранная версия
                        // >1 включённых -> базовое имя + пометка, что выбрано несколько версий
                        string displayName;
                        if (!anyEnabled)
                        {
                            displayName = baseName;
                        }
                        else if (enabledCount == 1)
                        {
                            var onlyEnabled = enabledMods[0];
                            displayName = $"{baseName} ({onlyEnabled.Name})";
                        }
                        else
                        {
                            displayName = $"{baseName} - несколько версий";
                        }

                        foreach (var mod in modsInGroup)
                        {
                            if (mod == primary)
                            {
                                mod.IsPrimaryVersion = true;
                                mod.HasMultipleVersions = modsInGroup.Count > 1;
                                mod.InstalledVersionsCount = enabledCount;
                                mod.DisplayName = displayName;
                                mod.MultipleVersionsTooltip = _localization.GetString("MultipleVersionsAvailable") ?? "Multiple versions available";
                                mod.DisabledFilesTooltip = _localization.GetString("SomeFilesDisabled") ?? "Some files are disabled";
                            }
                            else
                            {
                                mod.IsPrimaryVersion = false;
                                mod.HasMultipleVersions = modsInGroup.Count > 1;
                                mod.InstalledVersionsCount = enabledCount;
                                mod.MultipleVersionsTooltip = _localization.GetString("MultipleVersionsAvailable") ?? "Multiple versions available";
                                mod.DisabledFilesTooltip = _localization.GetString("SomeFilesDisabled") ?? "Some files are disabled";
                                // Для скрытых версий DisplayName сейчас не принципиален.
                            }
                        }

                        // Групповой чекбокс в главном списке: включён, если включена хотя бы одна версия и у неё есть включённые файлы.
                        bool anyInstallableVersion = modsInGroup.Any(m => m.HasAnyFiles && m.HasAnyEnabledFiles && m.IsEnabled);
                        primary.GroupIsEnabled = anyInstallableVersion;

                        // Предупреждение в главном списке:
                        // - если ни в одной версии нет файлов, показываем "нет файлов"
                        // - иначе, если среди включённых версий есть отключённые файлы, показываем стандартное предупреждение
                        bool groupHasFiles = modsInGroup.Any(m => m.HasAnyFiles);
                        if (!groupHasFiles)
                        {
                            primary.DisabledFilesTooltip = _localization.GetString("NoFilesFound") ?? "No files found in this mod folder";
                            primary.AggregatedHasDisabledFiles = true;
                        }
                        else
                        {
                            primary.DisabledFilesTooltip = _localization.GetString("SomeFilesDisabled") ?? "Some files are disabled";
                            bool anyDisabledInEnabledMods = enabledMods.Any(m => m.HasDisabledFiles);
                            primary.AggregatedHasDisabledFiles = anyDisabledInEnabledMods;
                        }
                    }
                }

                // Обновляем представление списка модов после изменения DisplayName / IsPrimaryVersion
                _modsViewSource?.View?.Refresh();
            }
            finally
            {
                _isUpdatingDisplayNames = false;
            }
        }

        private static System.Windows.Controls.ScrollViewer? GetScrollViewer(System.Windows.Controls.ListBox listBox)
        {
            if (listBox == null) return null;
            
            // Ищем ScrollViewer внутри ListBox
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(listBox); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(listBox, i);
                if (child is System.Windows.Controls.ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
                
                // Рекурсивно ищем во вложенных элементах
                for (int j = 0; j < System.Windows.Media.VisualTreeHelper.GetChildrenCount(child); j++)
                {
                    var grandChild = System.Windows.Media.VisualTreeHelper.GetChild(child, j);
                    if (grandChild is System.Windows.Controls.ScrollViewer sv)
                    {
                        return sv;
                    }
                }
            }
            
            // Если не нашли внутри, ищем в родителях
            for (System.Windows.DependencyObject depObj = listBox; depObj != null; depObj = System.Windows.Media.VisualTreeHelper.GetParent(depObj))
            {
                if (depObj is System.Windows.Controls.ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
            }
            return null;
        }

        private void BrowseVortexPath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Select Vortex mods folder";
            
            // Если путь уже задан и существует, открываем диалог в нём
            if (!string.IsNullOrWhiteSpace(VortexPathTextBox.Text) && Directory.Exists(VortexPathTextBox.Text))
            {
                dialog.SelectedPath = VortexPathTextBox.Text;
            }
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                VortexPathTextBox.Text = dialog.SelectedPath;
                _logger.LogInfo($"Vortex path selected: {dialog.SelectedPath}");
            }
        }

        private void BrowseTargetPath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Select game installation folder";
            
            // Если путь уже задан и существует, открываем диалог в нём
            if (!string.IsNullOrWhiteSpace(TargetPathTextBox.Text) && Directory.Exists(TargetPathTextBox.Text))
            {
                dialog.SelectedPath = TargetPathTextBox.Text;
            }
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var selectedPath = dialog.SelectedPath;
                var cfg = _configService.LoadPathsConfig();
                if (cfg.ValidateGamePathEnabled && !ValidateGamePath(selectedPath))
                {
                    WarningWindow.Show(
                        _localization.GetString("InvalidGamePath") ?? "Selected path is not a valid Stalker 2 game directory. Please select the game installation folder.",
                        _localization.GetString("Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }
                
                TargetPathTextBox.Text = selectedPath;
                _logger.LogInfo($"Target path selected: {selectedPath}");
                CheckGameAvailability();
                UpdateInstallButtonState();
            }
        }

        private void TargetPathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            CheckGameAvailability();
            UpdateInstallButtonState();
        }

        private void UpdateInstallButtonState()
        {
            if (InstallModsButton == null)
            {
                return;
            }

            // Проверяем, что путь валидный и моды загружены
            var cfg = _configService.LoadPathsConfig();
            bool isValidPath = !string.IsNullOrWhiteSpace(TargetPathTextBox.Text);
            if (cfg.ValidateGamePathEnabled)
            {
                isValidPath = isValidPath && ValidateGamePath(TargetPathTextBox.Text);
            }
            bool hasMods = _mods != null && _mods.Count > 0;

            InstallModsButton.IsEnabled = isValidPath && hasMods && !_isInstalling;
        }

        private static bool ValidateGamePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            // Проверяем наличие игрового exe файла
            var possibleExePaths = new[]
            {
                // Windows/Steam version
                Path.Combine(path, "Stalker2", "Binaries", "Win64", "Stalker2-Win64-Shipping.exe"),
                Path.Combine(path, "Stalker2", "Binaries", "Win64", "Stalker2.exe"),
                Path.Combine(path, "Stalker2", "Binaries", "Win64", "Stalker2Game.exe"),
                // Xbox/Windows Store version
                Path.Combine(path, "Stalker2", "Binaries", "WinGDK", "Stalker2-WinGDK-Shipping.exe"),
                // Fallback paths
                Path.Combine(path, "Stalker2-Win64-Shipping.exe"),
                Path.Combine(path, "Stalker2.exe")
            };

            bool exeExists = false;
            foreach (var exePath in possibleExePaths)
            {
                if (File.Exists(exePath))
                {
                    exeExists = true;
                    break;
                }
            }

            var gameContentPath = Path.Combine(path, "Stalker2", "Content");
            // Также проверяем наличие структуры папок игры
            bool gameFoldersExists = Directory.Exists(gameContentPath);

            return exeExists && gameFoldersExists;
        }

        private void LoadMods_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(VortexPathTextBox.Text))
            {
                WarningWindow.Show(_localization.GetString("SelectVortexPath"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                LoadModsFromPath(VortexPathTextBox.Text);
                UpdateStatus(_localization.GetString("ModsLoaded", _mods.Count));
                _logger.LogSuccess($"Loaded {_mods.Count} mods from path: {VortexPathTextBox.Text}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error loading mods", ex);
                WarningWindow.Show($"{_localization.GetString("ErrorLoadingMods")}: {ex.Message}", _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Загружает моды из указанного пути и применяет сохраненный порядок и состояния файлов
        /// </summary>
        private void LoadModsFromPath(string vortexPath)
        {
            if (string.IsNullOrWhiteSpace(vortexPath))
            {
                return;
            }

            var mods = _modManagerService.LoadModsFromVortexPath(vortexPath);
            _mods.Clear();

            // Загружаем сохраненный порядок модов
            var savedOrder = _configService.LoadModsOrder();
            var orderByName = savedOrder.Mods.ToDictionary(m => m.Name, m => m);

            // Находим максимальный порядок из сохраненных модов
            int maxOrder = savedOrder.Mods.Count != 0 ? savedOrder.Mods.Max(m => m.Order) : -1;

            // Устанавливаем порядок из сохраненного конфига или добавляем в конец
            foreach (var mod in mods)
            {
                if (orderByName.TryGetValue(mod.Name, out var orderItem))
                {
                    mod.Order = orderItem.Order;
                    mod.IsEnabled = orderItem.IsEnabled;
                    // Восстанавливаем состояния файлов (всегда устанавливаем, даже если пустой)
                    if (orderItem.FileStates != null && orderItem.FileStates.Count > 0)
                    {
                        mod.FileStates = new Dictionary<string, bool>(orderItem.FileStates);
                        _logger.LogDebug($"Loaded FileStates for mod '{mod.Name}': {orderItem.FileStates.Count} files, HasDisabledFiles={mod.HasDisabledFiles}");
                    }
                    else
                    {
                        // Инициализируем пустым словарем, чтобы HasDisabledFiles правильно вычислялся
                        mod.FileStates = new Dictionary<string, bool>();
                    }
                }
                else
                {
                    // Если мод не найден в сохраненном порядке, добавляем в конец
                    mod.Order = ++maxOrder;
                    mod.IsEnabled = true;
                    mod.FileStates = new Dictionary<string, bool>(); // Инициализируем пустым словарем
                }
                _mods.Add(mod);
            }

            // Обновляем отображаемые имена модов (с учетом базового имени и возможных дублей по версиям)
            UpdateModsDisplayNames();

            // Сортируем по порядку
            var sortedMods = _mods.OrderBy(m => m.Order).ToList();
            _mods.Clear();
            foreach (var mod in sortedMods)
            {
                _mods.Add(mod);
                // Принудительно обновляем индикатор отключенных файлов после загрузки
                if (mod.FileStates != null && mod.FileStates.Count > 0)
                {
                    // Вызываем обновление через изменение свойства, чтобы триггернуть PropertyChanged
                    var temp = mod.FileStates;
                    mod.FileStates = new Dictionary<string, bool>(temp);
                }
            }

            // Обновляем представление списка, чтобы индикаторы обновились
            _modsViewSource?.View?.Refresh();
            
            UpdateOrders();
            
            // Подписываемся на изменения всех модов
            foreach (var mod in _mods)
            {
                SubscribeToModChanges(mod);
            }
            
            // Сохраняем исходное состояние после загрузки модов
            SaveOriginalModsState();
            _hasUnsavedChanges = false;
            UpdateSaveCancelButtons();
            
            // Обновляем состояние кнопки Install Mods
            UpdateInstallButtonState();
            
            // Применяем сортировку после загрузки модов
            ApplySorting();
            UpdateSortHeaderButtons();
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Сохраняем ТОЛЬКО конфиг (пути и размер окна), НЕ список модов
                var pathsConfig = _configService.LoadPathsConfig();
                pathsConfig.VortexPath = VortexPathTextBox.Text;
                pathsConfig.TargetPath = TargetPathTextBox.Text;
                pathsConfig.WindowWidth = Width;
                pathsConfig.WindowHeight = Height;
                _configService.SavePathsConfig(pathsConfig);
                _logger.LogInfo("Paths config saved");

                UpdateStatus("Config saved");
                WarningWindow.Show(_localization.GetString("ConfigSavedSuccess"), _localization.GetString("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                _logger.LogSuccess("Config saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error saving config", ex);
                WarningWindow.Show($"{_localization.GetString("ErrorSavingConfig")}: {ex.Message}", _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SaveModsList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Сохраняем ТОЛЬКО список модов
                var modsOrder = _configService.CreateModOrderFromMods([.. _mods]);
                _configService.SaveModsOrder(modsOrder);
                _logger.LogInfo($"Mods order saved: {modsOrder.Mods.Count} mods");

                // Сохраняем текущее состояние как исходное
                SaveOriginalModsState();
                _hasUnsavedChanges = false;
                UpdateSaveCancelButtons();

                UpdateStatus("Mods order saved");
                WarningWindow.Show("Порядок модов сохранен", _localization.GetString("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                _logger.LogSuccess("Mods order saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error saving mods order", ex);
                WarningWindow.Show($"Ошибка при сохранении порядка модов: {ex.Message}", _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelChanges_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasUnsavedChanges)
            {
                return;
            }
            
            var result = WarningWindow.Show(
                _localization.GetString("CancelChangesConfirmation"),
                _localization.GetString("CancelChangesTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                CancelChanges();
            }
        }
        
        private void CancelChanges()
        {
            try
            {
                // Восстанавливаем исходное состояние модов
                _mods.Clear();
                
                // Отписываемся от событий, чтобы не помечать изменения при восстановлении
                _mods.CollectionChanged -= Mods_CollectionChanged;
                
                foreach (var originalMod in _originalModsState)
                {
                    var mod = new ModInfo
                    {
                        SourcePath = originalMod.SourcePath,
                        Name = originalMod.Name,
                        Order = originalMod.Order,
                        IsEnabled = originalMod.IsEnabled
                    };
                    
                    // Подписываемся на изменения каждого мода
                    SubscribeToModChanges(mod);
                    
                    _mods.Add(mod);
                }
                
                // Подписываемся обратно
                _mods.CollectionChanged += Mods_CollectionChanged;
                
                UpdateOrders();
                _hasUnsavedChanges = false;
                UpdateSaveCancelButtons();
                
                // Обновляем представление и применяем сортировку
                _modsViewSource?.View?.Refresh();
                ApplySorting();
                UpdateSortHeaderButtons();
                
                UpdateStatus("Changes cancelled");
                _logger.LogInfo("Changes cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error cancelling changes", ex);
                WarningWindow.Show($"Ошибка при отмене изменений: {ex.Message}", _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SaveOriginalModsState()
        {
            _originalModsState.Clear();
            foreach (var mod in _mods)
            {
                _originalModsState.Add(new ModInfo
                {
                    SourcePath = mod.SourcePath,
                    Name = mod.Name,
                    Order = mod.Order,
                    IsEnabled = mod.IsEnabled
                });
            }
            _hasUnsavedChanges = false;
            UpdateSaveCancelButtons();
        }
        
        private void Mods_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isClosing) return;
            
            // Подписываемся на изменения новых модов
            if (e.NewItems != null)
            {
                foreach (ModInfo mod in e.NewItems)
                {
                    SubscribeToModChanges(mod);
                }
            }
            
            // Отмечаем изменения при изменении коллекции
            MarkAsChanged();
        }
        
        private void SubscribeToModChanges(ModInfo mod)
        {
            mod.PropertyChanged += (s, e) =>
            {
                if (_isClosing || _isApplyingOrder) return;
                
                // Отслеживаем изменения Order и IsEnabled
                if (e.PropertyName == nameof(ModInfo.Order) || e.PropertyName == nameof(ModInfo.IsEnabled))
                {
                    MarkAsChanged();

                    // При изменении включения/отключения версий пересчитываем
                    // отображаемые имена и индикаторы множественных установок
                    if (e.PropertyName == nameof(ModInfo.IsEnabled))
                    {
                        UpdateModsDisplayNames();
                    }
                }

                // При изменении набора отключённых файлов пересчитываем агрегированный индикатор
                if (e.PropertyName == nameof(ModInfo.HasDisabledFiles))
                {
                    UpdateModsDisplayNames();
                }
            };
        }
        
        private void MarkAsChanged()
        {
            if (_isClosing) return;
            
            // Проверяем, действительно ли есть изменения
            if (HasChanges())
            {
                _hasUnsavedChanges = true;
                UpdateSaveCancelButtons();
            }
            else
            {
                _hasUnsavedChanges = false;
                UpdateSaveCancelButtons();
            }
        }
        
        private bool HasChanges()
        {
            // Проверяем количество модов
            if (_mods.Count != _originalModsState.Count)
            {
                return true;
            }
            
            // Создаем словарь исходных модов по имени
            var originalModsByName = _originalModsState.ToDictionary(m => m.Name, m => m);
            
            // Проверяем каждый текущий мод
            foreach (var mod in _mods)
            {
                if (!originalModsByName.TryGetValue(mod.Name, out var originalMod))
                {
                    return true; // Мод добавлен или удален
                }
                
                // Проверяем изменения Order и IsEnabled
                if (mod.Order != originalMod.Order || mod.IsEnabled != originalMod.IsEnabled)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        private void UpdateSaveCancelButtons()
        {
            // Обновляем состояние кнопок Сохранить/Отменить
            if (SaveChangesButton != null)
            {
                SaveChangesButton.IsEnabled = _hasUnsavedChanges;
            }
            if (CancelChangesButton != null)
            {
                CancelChangesButton.IsEnabled = _hasUnsavedChanges;
            }
        }

        // Нормализует имя мода из order-файлов, удаляя суффиксы вида "-123-1-2-..." или "-v3-0-1-..."
        private static string NormalizeOrderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            // Правило:
            // - если встречаем первый '-' и далее идет цифра, то сохраняем всё ДО этого '-' (не включая его).
            //   Остальной хвост отбрасываем.
            // Примеры:
            //   "SimpleModLoader-304-0-4-9-1748465595" -> "SimpleModLoader"
            //   "UE4SS-560-v3-0-1-..." -> "UE4SS"
            //   "Achievements Enabler - Steam-1493-1-5-..." -> "Achievements Enabler - Steam"
            //   "Loader v2.0.1 - Steam-664-2-0-1-1737163600" -> "Loader v2.0.1 - Steam"

            for (int i = 0; i < name.Length - 1; i++)
            {
                if (name[i] != '-') continue;
                
                // Пропускаем пробелы после '-'
                int j = i + 1;
                while (j < name.Length && char.IsWhiteSpace(name[j])) j++;
                if (j >= name.Length) break;
                
                // Если после '-' идет цифра, возвращаем всё до этого '-'
                if (char.IsDigit(name[j]))
                {
                    return name.Substring(0, i).TrimEnd();
                }
            }

            return name.Trim();
        }

        private void LoadConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Проверяем, есть ли файлы конфигов
                bool hasPathsConfig = System.IO.File.Exists("config.json");
                bool hasModsOrder = System.IO.File.Exists("mods_order.json");
                bool hasLegacyConfig = System.IO.File.Exists("mods_config.json");

                // Если нет ни одного файла конфига, ничего не делаем
                if (!hasPathsConfig && !hasModsOrder && !hasLegacyConfig)
                {
                    return;
                }

                ModConfig? pathsConfig = null;
                ModOrder? modsOrder = null;

                // Если есть старый формат конфига, загружаем его
                if (hasLegacyConfig && _configService.TryLoadLegacyConfig(out var legacyPaths, out var legacyOrder))
                {
                    pathsConfig = legacyPaths;
                    modsOrder = legacyOrder;
                }
                else
                {
                    // Загружаем новые конфиги
                    if (hasPathsConfig)
                    {
                        pathsConfig = _configService.LoadPathsConfig();
                    }

                    if (hasModsOrder)
                    {
                        modsOrder = _configService.LoadModsOrder();
                    }
                }

                // Применяем пути, если они загружены
                if (pathsConfig != null)
                {
                    VortexPathTextBox.Text = pathsConfig.VortexPath;
                    TargetPathTextBox.Text = pathsConfig.TargetPath;
                    CheckGameAvailability();
                    UpdateInstallButtonState();
                }

                // Применяем порядок модов, если он загружен
                if (modsOrder != null && modsOrder.Mods.Count != 0)
                {
                    // Применяем порядок к текущим модам
                    ApplyModsOrder(modsOrder);
                    
                    // Обновляем состояние кнопки Install Mods
                    UpdateInstallButtonState();
                    
                    UpdateStatus($"Loaded mods order with {modsOrder.Mods.Count} mods");
                    _logger.LogSuccess($"Loaded mods order with {modsOrder.Mods.Count} mods");
                }
                else if (pathsConfig != null)
                {
                    UpdateStatus("Loaded paths config");
                    _logger.LogInfo("Loaded paths config");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error loading config", ex);
                WarningWindow.Show($"{_localization.GetString("ErrorLoadingConfig")}: {ex.Message}", _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void InstallMods_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, что моды загружены
            if (_mods == null || _mods.Count == 0)
            {
                WarningWindow.Show(_localization.GetString("NoModsLoadedMessage"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (string.IsNullOrWhiteSpace(TargetPathTextBox.Text))
            {
                WarningWindow.Show(_localization.GetString("SelectTargetPath"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Проверяем валидность пути игры перед установкой
            var cfg = _configService.LoadPathsConfig();
            if (cfg.ValidateGamePathEnabled && !ValidateGamePath(TargetPathTextBox.Text))
            {
                WarningWindow.Show(
                    _localization.GetString("InvalidGamePath") ?? "Selected path is not a valid Stalker 2 game directory. Please select the game installation folder.",
                    _localization.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }

            var result = WarningWindow.Show(
                _localization.GetString("InstallModsWarning"),
                _localization.GetString("Warning"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // Проверяем, идет ли уже установка
            var installMenuItem = sender as System.Windows.Controls.MenuItem;
            var installButton = sender as System.Windows.Controls.Button;
            
            // Если установка идет, отменяем её
            if (_isInstalling && _installCancellationTokenSource != null)
            {
                _installCancellationTokenSource.Cancel();
                _logger.LogInfo("Installation cancelled by user");
                
                if (installMenuItem != null)
                {
                    installMenuItem.IsEnabled = true;
                    installMenuItem.Header = "_" + _localization.GetString("InstallMods");
                }
                
                if (installButton != null)
                {
                    installButton.IsEnabled = true;
                    installButton.Content = _localization.GetString("InstallMods");
                }
                
                _isInstalling = false;
                ProgressBar.Visibility = Visibility.Collapsed;
                ProgressTextBlock.Text = "";
                UpdateStatus(_localization.GetString("InstallationCancelled") ?? "Installation cancelled");
                UpdateInstallButtonState();
                return;
            }
            
            // Создаем CancellationTokenSource для отмены
            _installCancellationTokenSource = new CancellationTokenSource();
            _isInstalling = true;
            
            // Изменяем кнопку на Cancel
            if (installMenuItem != null)
            {
                installMenuItem.IsEnabled = true;
                installMenuItem.Header = "_" + _localization.GetString("Cancel");
            }
            
            if (installButton != null)
            {
                installButton.IsEnabled = true;
                installButton.Content = _localization.GetString("Cancel");
            }

            // Показываем прогресс-бар
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            ProgressTextBlock.Text = "";

            try
            {
                UpdateStatus("");
                var progress = new Progress<InstallProgress>(p =>
                {
                    ProgressBar.Value = p.Percentage;
                    ProgressTextBlock.Text = $"Installing: {p.CurrentMod} ({p.Installed}/{p.Total})";
                    //UpdateStatus($"Installing: {p.CurrentMod} ({p.Installed}/{p.Total})");
                });

                // Устанавливаем все включенные версии модов.
                // Пользователь может вручную включить одну или несколько версий одного и того же мода.
                var modsToInstall = _mods
                    .Where(m => m.IsEnabled)
                    .OrderBy(m => m.Order)
                    .ToList();

                var enabledModsCount = modsToInstall.Count;
                _logger.LogInfo($"Starting mods installation. Target: {TargetPathTextBox.Text}, Enabled mods: {enabledModsCount}");
                
                await _modManagerService.InstallModsAsync(modsToInstall, TargetPathTextBox.Text, progress, _installCancellationTokenSource.Token);

                if (_installCancellationTokenSource.Token.IsCancellationRequested)
                {
                    UpdateStatus(_localization.GetString("InstallationCancelled") ?? "Installation cancelled");
                    _logger.LogInfo("Installation was cancelled");
                    return;
                }

                UpdateStatus($"Installed {enabledModsCount} mods");
                _logger.LogSuccess($"Mods installed successfully. Installed {enabledModsCount} mods to {TargetPathTextBox.Text}");
                WarningWindow.Show(_localization.GetString("ModsInstalledSuccess"), _localization.GetString("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                UpdateStatus(_localization.GetString("InstallationCancelled") ?? "Installation cancelled");
                _logger.LogInfo("Installation was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error installing mods", ex);
                WarningWindow.Show($"{_localization.GetString("ErrorInstallingMods")}: {ex.Message}", _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Восстанавливаем элемент управления
                if (installMenuItem != null)
                {
                    installMenuItem.IsEnabled = true;
                    installMenuItem.Header = "_" + _localization.GetString("InstallMods");
                }
                
                if (installButton != null)
                {
                    installButton.IsEnabled = true;
                    installButton.Content = _localization.GetString("InstallMods");
                }

                // Скрываем прогресс-бар
                ProgressBar.Visibility = Visibility.Collapsed;
                ProgressBar.Value = 0;
                ProgressTextBlock.Text = "";
                
                _isInstalling = false;
                _installCancellationTokenSource?.Dispose();
                _installCancellationTokenSource = null;
                
                // Обновляем состояние кнопки после завершения установки
                UpdateInstallButtonState();
            }
        }

        private async void ClearMods_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TargetPathTextBox.Text))
            {
                WarningWindow.Show(_localization.GetString("SelectTargetPath"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = WarningWindow.Show(
                _localization.GetString("ClearModsWarning"),
                _localization.GetString("Warning"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // Блокируем пункт меню
            var clearMenuItem = sender as System.Windows.Controls.MenuItem;
            if (clearMenuItem != null)
            {
                clearMenuItem.IsEnabled = false;
                clearMenuItem.Header = "Clearing...";
            }

            // Показываем прогресс-бар
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            ProgressTextBlock.Text = "Clearing ~mods folder...";
            UpdateStatus("Clearing ~mods folder...");

            try
            {
                _logger.LogInfo($"Starting to clear mods folder: {TargetPathTextBox.Text}");
                
                await _modManagerService.ClearModsFolderAsync(TargetPathTextBox.Text);
                
                UpdateStatus("~mods folder cleared");
                _logger.LogSuccess($"Mods folder cleared successfully: {TargetPathTextBox.Text}");
                WarningWindow.Show(_localization.GetString("ModsFolderClearedSuccess"), _localization.GetString("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error clearing mods folder", ex);
                WarningWindow.Show($"{_localization.GetString("ErrorClearingMods")}: {ex.Message}", _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Восстанавливаем пункт меню
                if (clearMenuItem != null)
                {
                    clearMenuItem.IsEnabled = true;
                    clearMenuItem.Header = "_Clear ~mods Folder";
                }

                // Скрываем прогресс-бар
                ProgressBar.Visibility = Visibility.Collapsed;
                ProgressBar.Value = 0;
                ProgressTextBlock.Text = "";
            }
        }

        private void DlcModLoader_Click(object sender, RoutedEventArgs e)
        {
            var window = new DlcModLoaderWindow(TargetPathTextBox.Text)
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var window = new AboutWindow
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void ModsViewSource_Filter(object sender, System.Windows.Data.FilterEventArgs e)
        {
            if (e.Item is ModInfo mod)
            {
                string searchText = string.Empty;
                if (SearchModsTextBox != null && !string.IsNullOrEmpty(SearchModsTextBox.Text))
                {
                    searchText = SearchModsTextBox.Text.Trim();
                }
                
                if (string.IsNullOrEmpty(searchText))
                {
                    // Без поисковой строки отображаем только "основные" версии модов
                    e.Accepted = mod.IsPrimaryVersion;
                }
                else
                {
                    string searchLower = searchText.ToLowerInvariant();
                    // При поиске всё равно ограничиваемся только основными версиями,
                    // чтобы пользователь не видел несколько строк одного и того же мода.
                    e.Accepted = mod.IsPrimaryVersion &&
                                 (mod.Name.Contains(searchLower, StringComparison.InvariantCultureIgnoreCase) ||
                                  mod.TargetFolderName.Contains(searchLower, StringComparison.InvariantCultureIgnoreCase));
                }
            }
            else
            {
                e.Accepted = false;
            }
        }

        private void SearchModsTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _modsViewSource?.View?.Refresh();
        }

        private void EnableHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSortProperty == "IsEnabled")
            {
                // Переключаем направление сортировки
                _currentSortDirection = _currentSortDirection == ListSortDirection.Ascending 
                    ? ListSortDirection.Descending 
                    : ListSortDirection.Ascending;
            }
            else
            {
                _currentSortProperty = "IsEnabled";
                _currentSortDirection = ListSortDirection.Ascending;
            }
            
            ApplySorting();
            UpdateSortHeaderButtons();
        }

        private void OrderHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSortProperty == "Order")
            {
                // Переключаем направление сортировки
                _currentSortDirection = _currentSortDirection == ListSortDirection.Ascending 
                    ? ListSortDirection.Descending 
                    : ListSortDirection.Ascending;
            }
            else
            {
                _currentSortProperty = "Order";
                _currentSortDirection = ListSortDirection.Ascending;
            }
            
            ApplySorting();
            UpdateSortHeaderButtons();
        }

        private void NameHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSortProperty == "Name")
            {
                // Переключаем направление сортировки
                _currentSortDirection = _currentSortDirection == ListSortDirection.Ascending 
                    ? ListSortDirection.Descending 
                    : ListSortDirection.Ascending;
            }
            else
            {
                _currentSortProperty = "Name";
                _currentSortDirection = ListSortDirection.Ascending;
            }
            
            ApplySorting();
            UpdateSortHeaderButtons();
        }

        private void ApplySorting()
        {
            if (_modsViewSource?.View == null)
            {
                return;
            }

            _modsViewSource.View.SortDescriptions.Clear();
            
            var sortDescription = new SortDescription(_currentSortProperty, _currentSortDirection);
            _modsViewSource.View.SortDescriptions.Add(sortDescription);
            
            _modsViewSource.View.Refresh();
        }

        private void UpdateSortHeaderButtons()
        {
            if (EnableHeaderButton == null || OrderHeaderButton == null || NameHeaderButton == null)
            {
                return;
            }

            // Сбрасываем все заголовки
            EnableHeaderButton.Content = "✓";
            OrderHeaderButton.Content = "Order";
            NameHeaderButton.Content = "Mod Name";
            
            // Добавляем индикатор направления сортировки к активному заголовку
            string sortIndicator = _currentSortDirection == ListSortDirection.Ascending ? " ↑" : " ↓";
            
            if (_currentSortProperty == "IsEnabled")
            {
                EnableHeaderButton.Content = "✓" + sortIndicator;
            }
            else if (_currentSortProperty == "Order")
            {
                OrderHeaderButton.Content = "Order" + sortIndicator;
            }
            else if (_currentSortProperty == "Name")
            {
                NameHeaderButton.Content = "Mod Name" + sortIndicator;
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (ModsListBox.SelectedItem is ModInfo selectedMod)
            {
                var index = _mods.IndexOf(selectedMod);
                if (index > 0)
                {
                    _mods.Move(index, index - 1);
                    UpdateOrders();
                    // Применяем сортировку после изменения порядка
                    ApplySorting();
                    // Находим новый индекс после сортировки
                    ModsListBox.SelectedItem = selectedMod;
                    _logger.LogDebug($"Moved mod '{selectedMod.Name}' up (from {index} to {index - 1})");
                }
            }
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (ModsListBox.SelectedItem is ModInfo selectedMod)
            {
                var index = _mods.IndexOf(selectedMod);
                if (index < _mods.Count - 1)
                {
                    _mods.Move(index, index + 1);
                    UpdateOrders();
                    // Применяем сортировку после изменения порядка
                    ApplySorting();
                    // Находим новый индекс после сортировки
                    ModsListBox.SelectedItem = selectedMod;
                    _logger.LogDebug($"Moved mod '{selectedMod.Name}' down (from {index} to {index + 1})");
                }
            }
        }

        private void UpdateOrders()
        {
            for (int i = 0; i < _mods.Count; i++)
            {
                _mods[i].Order = i;
                // Свойство TargetFolderName автоматически обновится через OnPropertyChanged в сеттере Order
            }
        }

        private void LoadConfig()
        {
            // Загружаем пути
            var pathsConfig = _configService.LoadPathsConfig();
            VortexPathTextBox.Text = pathsConfig.VortexPath;
            
            if (string.IsNullOrWhiteSpace(pathsConfig.TargetPath))
            {
                TargetPathTextBox.Text = _modManagerService.GetDefaultTargetPath();
            }
            else
            {
                TargetPathTextBox.Text = pathsConfig.TargetPath;
            }

            // Загружаем размер окна
            if (pathsConfig.WindowWidth >= MinWidth && pathsConfig.WindowHeight >= MinHeight)
            {
                Width = pathsConfig.WindowWidth;
                Height = pathsConfig.WindowHeight;
            }

            // Если указана папка с модами, автоматически загружаем моды
            if (!string.IsNullOrWhiteSpace(pathsConfig.VortexPath))
            {
                try
                {
                    LoadModsFromPath(pathsConfig.VortexPath);
                    _logger.LogInfo($"Auto-loaded {_mods.Count} mods on startup from saved path: {pathsConfig.VortexPath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error auto-loading mods on startup", ex);
                    // Не показываем ошибку пользователю при старте, только логируем
                }
            }

            // Загружаем порядок модов (если моды уже были загружены выше, порядок уже применен)
            // Если моды не были загружены, но есть сохраненный порядок, применяем его к существующим модам
            if (_mods.Count == 0)
            {
                var modsOrder = _configService.LoadModsOrder();
                if (modsOrder.Mods.Count != 0)
                {
                    ApplyModsOrder(modsOrder);
                    _logger.LogDebug($"Applied saved mods order on startup: {modsOrder.Mods.Count} mods");
                }
            }
        }

        private void ApplyModsOrder(ModOrder order)
        {
            // Временно отключаем отслеживание изменений при применении порядка
            _isApplyingOrder = true;
            
            try
            {
                // Смотрим настройку: учитывать ли версии модов при сопоставлении
                var cfg = _configService.LoadPathsConfig();
                bool considerVersion = cfg.ConsiderModVersion;

                // Создаем словарь для быстрого поиска модов по имени
                var modsByName = _mods.ToDictionary(m => m.Name, m => m, StringComparer.InvariantCultureIgnoreCase);
                
                // Для режима без учета версий готовим группировку по базовому имени (NormalizeOrderName),
                // чтобы корректно обрабатывать несколько версий одного и того же мода
                Dictionary<string, List<ModInfo>>? modsByBaseName = null;
                if (!considerVersion)
                {
                    modsByBaseName = new Dictionary<string, List<ModInfo>>(StringComparer.InvariantCultureIgnoreCase);
                    foreach (var mod in _mods)
                    {
                        var baseName = NormalizeOrderName(mod.Name);
                        if (!modsByBaseName.TryGetValue(baseName, out var list))
                        {
                            list = new List<ModInfo>();
                            modsByBaseName[baseName] = list;
                        }
                        list.Add(mod);
                    }
                }

                // Применяем порядок и статус включения из конфига
                foreach (var orderItem in order.Mods.OrderBy(m => m.Order))
                {
                    // Матч: либо по полному имени (если включено), либо по нормализованному
                    ModInfo? mod = null;
                    if (considerVersion)
                    {
                        modsByName.TryGetValue(orderItem.Name, out mod);
                    }
                    else
                    {
                        if (!modsByName.TryGetValue(orderItem.Name, out mod))
                        {
                            var normalized = NormalizeOrderName(orderItem.Name);
                            if (modsByBaseName != null && modsByBaseName.TryGetValue(normalized, out var list) && list.Count > 0)
                            {
                                // По умолчанию используем "последнюю" версию (по алфавиту имени папки),
                                // чтобы при отключенном учёте версий автоматически брать самую новую.
                                mod = list
                                    .OrderBy(m => m.Name, StringComparer.InvariantCultureIgnoreCase)
                                    .Last();
                            }
                        }
                    }

                    if (mod != null)
                    {
                        mod.Order = orderItem.Order;
                        mod.IsEnabled = orderItem.IsEnabled;
                        // Восстанавливаем состояния файлов
                        if (orderItem.FileStates != null && orderItem.FileStates.Count > 0)
                        {
                            mod.FileStates = new Dictionary<string, bool>(orderItem.FileStates);
                            _logger.LogDebug($"Applied FileStates for mod '{mod.Name}': {orderItem.FileStates.Count} files, HasDisabledFiles={mod.HasDisabledFiles}");
                        }
                        else
                        {
                            mod.FileStates = new Dictionary<string, bool>(); // Инициализируем пустым словарем
                        }
                    }
                }

                // Сортируем список по порядку
                var sortedMods = _mods.OrderBy(m => m.Order).ToList();
                _mods.Clear();
                foreach (var mod in sortedMods)
                {
                    _mods.Add(mod);
                    // Принудительно обновляем индикатор отключенных файлов после применения порядка
                    if (mod.FileStates != null && mod.FileStates.Count > 0)
                    {
                        // Вызываем обновление через изменение свойства, чтобы триггернуть PropertyChanged
                        var temp = mod.FileStates;
                        mod.FileStates = new Dictionary<string, bool>(temp);
                    }
                }

                // Обновляем порядки после сортировки
                UpdateOrders();
                
                // Обновляем отображаемые имена модов (после применения порядка и возможных изменений)
                UpdateModsDisplayNames();
                
                // Подписываемся на изменения всех модов
                foreach (var mod in _mods)
                {
                    SubscribeToModChanges(mod);
                }
                
                // Сохраняем исходное состояние после применения порядка
                SaveOriginalModsState();
            }
            finally
            {
                // Включаем отслеживание изменений обратно
                _isApplyingOrder = false;
            }
            
            // Обновляем состояние кнопки Install Mods
            UpdateInstallButtonState();
            
            // Применяем сортировку после применения порядка модов
            ApplySorting();
            UpdateSortHeaderButtons();
        }

        private void ExportOrder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_mods.Any())
                {
                    WarningWindow.Show(_localization.GetString("NoModsLoadedMessage"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                using var dialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = "mods_order.json",
                    DefaultExt = "json",
                    Title = "Export Mods Order"
                };

                // Устанавливаем начальную директорию - последний использованный путь, mods_order.json или рабочая директория
                var pathsConfig = _configService.LoadPathsConfig();
                string? initialDir = null;
                
                if (!string.IsNullOrWhiteSpace(pathsConfig.LastExportOrderPath) && File.Exists(pathsConfig.LastExportOrderPath))
                {
                    initialDir = Path.GetDirectoryName(pathsConfig.LastExportOrderPath);
                    dialog.FileName = Path.GetFileName(pathsConfig.LastExportOrderPath);
                }
                else
                {
                    var currentDir = Environment.CurrentDirectory;
                    var modsOrderPath = Path.Combine(currentDir, "mods_order.json");
                    if (File.Exists(modsOrderPath))
                    {
                        initialDir = currentDir;
                        dialog.FileName = "mods_order.json";
                    }
                    else if (Directory.Exists(currentDir))
                    {
                        initialDir = currentDir;
                    }
                }
                
                if (!string.IsNullOrEmpty(initialDir))
                {
                    dialog.InitialDirectory = initialDir;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // Сохраняем путь к последнему экспортированному файлу
                    pathsConfig.LastExportOrderPath = dialog.FileName;
                    _configService.SavePathsConfig(pathsConfig);
                    
                    var modsOrder = _configService.CreateModOrderFromMods([.. _mods]);
                    _configService.SaveModsOrderToFile(modsOrder, dialog.FileName);
                    UpdateStatus($"Mods order exported to {System.IO.Path.GetFileName(dialog.FileName)}");
                    _logger.LogSuccess($"Mods order exported to {dialog.FileName}");
                    WarningWindow.Show(_localization.GetString("OrderExported"), _localization.GetString("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error exporting order", ex);
                WarningWindow.Show($"{_localization.GetString("ErrorExportingOrder")}: {ex.Message}", _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportOrder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_mods.Any())
                {
                    WarningWindow.Show(_localization.GetString("NoModsLoadedMessage"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                using var dialog = new OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = "mods_order.json",
                    Title = "Import Mods Order"
                };

                // Устанавливаем начальную директорию - последний использованный путь, mods_order.json или рабочая директория
                var pathsConfig = _configService.LoadPathsConfig();
                string? initialDir = null;
                
                if (!string.IsNullOrWhiteSpace(pathsConfig.LastImportOrderPath) && File.Exists(pathsConfig.LastImportOrderPath))
                {
                    initialDir = Path.GetDirectoryName(pathsConfig.LastImportOrderPath);
                    dialog.FileName = Path.GetFileName(pathsConfig.LastImportOrderPath);
                }
                else
                {
                    var currentDir = Environment.CurrentDirectory;
                    var modsOrderPath = Path.Combine(currentDir, "mods_order.json");
                    if (File.Exists(modsOrderPath))
                    {
                        initialDir = currentDir;
                        dialog.FileName = "mods_order.json";
                    }
                    else if (Directory.Exists(currentDir))
                    {
                        initialDir = currentDir;
                    }
                }
                
                if (!string.IsNullOrEmpty(initialDir))
                {
                    dialog.InitialDirectory = initialDir;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // Сохраняем путь к последнему импортированному файлу
                    pathsConfig.LastImportOrderPath = dialog.FileName;
                    _configService.SavePathsConfig(pathsConfig);
                    
                    var modsOrder = _configService.LoadModsOrderFromFile(dialog.FileName);
                    
                    if (modsOrder.Mods.Count != 0)
                    {
                        // Отключаем все моды, которых нет в импортированном order
                        var cfg = _configService.LoadPathsConfig();
                        bool considerVersion = cfg.ConsiderModVersion;
                        var orderNames = new HashSet<string>(
                            modsOrder.Mods.Select(m => considerVersion ? m.Name.Trim() : NormalizeOrderName(m.Name)),
                            StringComparer.InvariantCultureIgnoreCase);
                        foreach (var mod in _mods)
                        {
                            var key = considerVersion ? mod.Name.Trim() : NormalizeOrderName(mod.Name);
                            if (!orderNames.Contains(key))
                            {
                                mod.IsEnabled = false;
                            }
                        }

                        ApplyModsOrder(modsOrder);
                        UpdateStatus($"Mods order imported from {System.IO.Path.GetFileName(dialog.FileName)}");
                        _logger.LogSuccess($"Mods order imported from {dialog.FileName}. Applied order for {modsOrder.Mods.Count} mods");
                        WarningWindow.Show(_localization.GetString("OrderImportedSuccess", modsOrder.Mods.Count), _localization.GetString("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        _logger.LogWarning("Imported file does not contain any mods order");
                        WarningWindow.Show(_localization.GetString("NoModsInImportedFile"), _localization.GetString("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error importing order", ex);
                WarningWindow.Show($"{_localization.GetString("ErrorImportingOrder")}: {ex.Message}", _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private void Advanced_Click(object sender, RoutedEventArgs e)
        {
            _logger.LogDebug("Advanced options window opened");
            var optionsWindow = new AdditionalOptionsWindow(TargetPathTextBox.Text)
            {
                Owner = this
            };

            if (optionsWindow.ShowDialog() == true)
            {
                if (optionsWindow.SortBySnapshot && !string.IsNullOrWhiteSpace(optionsWindow.JsonFilePath))
                {
                    var fileExt = System.IO.Path.GetExtension(optionsWindow.JsonFilePath).ToLower();
                    var fileType = fileExt == ".txt" ? "TXT" : "JSON";
                    _logger.LogInfo($"Sorting mods by {fileType} file: {optionsWindow.JsonFilePath}");
                    SortModsByJsonFile(optionsWindow.JsonFilePath);
                }
                
                // Если изменилась локализация, обновляем UI
                if (optionsWindow.LocalizationChanged)
                {
                    UpdateLocalization();
                }
                
                // Обновляем состояние кнопки установки, так как настройка ValidateGamePath могла измениться
                UpdateInstallButtonState();
                CheckGameAvailability();
            }
        }

        private void CheckGameAvailability()
        {
            try
            {
                if (LaunchGameSection == null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(TargetPathTextBox.Text))
                {
                    LaunchGameSection.Visibility = Visibility.Collapsed;
                    return;
                }

                var targetPath = TargetPathTextBox.Text.Trim();
                var cfg = _configService.LoadPathsConfig();
                bool gameFound = !cfg.ValidateGamePathEnabled || ValidateGamePath(targetPath);

                if (gameFound)
                {
                    LaunchGameSection.Visibility = Visibility.Visible;
                    _logger.LogInfo("Stalker 2 game found and ready to launch");
                }
                else
                {
                    LaunchGameSection.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error checking game availability", ex);
                if (LaunchGameSection != null)
                {
                    LaunchGameSection.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void LaunchStalker2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TargetPathTextBox.Text))
                {
                    WarningWindow.Show(_localization.GetString("SelectTargetPath"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var targetPath = TargetPathTextBox.Text.Trim();
                if (!Directory.Exists(targetPath))
                {
                    WarningWindow.Show(_localization.GetString("TargetPathNotFound"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _logger.LogInfo($"Attempting to launch Stalker 2 from: {targetPath}");

                // Find executable
                var possibleExePaths = new[]
                {
                    // Windows/Steam version
                    Path.Combine(targetPath, "Stalker2", "Binaries", "Win64", "Stalker2-Win64-Shipping.exe"),
                    Path.Combine(targetPath, "Stalker2", "Binaries", "Win64", "Stalker2.exe"),
                    Path.Combine(targetPath, "Stalker2", "Binaries", "Win64", "Stalker2Game.exe"),
                    // Xbox/Windows Store version
                    Path.Combine(targetPath, "Stalker2", "Binaries", "WinGDK", "Stalker2-WinGDK-Shipping.exe"),
                    // Fallback paths
                    Path.Combine(targetPath, "Stalker2-Win64-Shipping.exe"),
                    Path.Combine(targetPath, "Stalker2.exe")
                };

                string? exePath = null;
                foreach (var path in possibleExePaths)
                {
                    if (File.Exists(path))
                    {
                        exePath = path;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(exePath))
                {
                    WarningWindow.Show(
                        _localization.GetString("Stalker2ExeNotFound") ?? "Stalker 2 executable not found. Please check the game installation path.",
                        _localization.GetString("Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    _logger.LogError("Stalker 2 executable not found in any expected location");
                    return;
                }

                // Launch directly from folder
                var gameDir = Path.GetDirectoryName(exePath);
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = gameDir ?? targetPath,
                    UseShellExecute = true
                };

                Process.Start(processStartInfo);
                _logger.LogSuccess($"Launched Stalker 2: {exePath}");
                StatusTextBlock.Text = _localization.GetString("LaunchingStalker2") ?? "Launching Stalker 2...";
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to launch Stalker 2", ex);
                WarningWindow.Show(
                    _localization.GetString("FailedToLaunchStalker2") ?? $"Failed to launch Stalker 2: {ex.Message}",
                    _localization.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void SortModsByJsonFile(string jsonFilePath)
        {
            try
            {
                if (!System.IO.File.Exists(jsonFilePath))
                {
                    WarningWindow.Show(_localization.GetString("FileNotFound", jsonFilePath), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!_mods.Any())
                {
                    WarningWindow.Show(_localization.GetString("NoModsLoadedMessage"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Определяем тип файла
                var fileExtension = System.IO.Path.GetExtension(jsonFilePath).ToLower();
                List<string>? files = null;

                if (fileExtension == ".txt")
                {
                    _logger.LogInfo($"Reading TXT file: {jsonFilePath}");
                    // Читаем TXT файл построчно
                    var lines = System.IO.File.ReadAllLines(jsonFilePath);
                    files = [];

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        // Пропускаем пустые строки
                        if (string.IsNullOrEmpty(trimmed))
                            continue;

                        // Убираем кавычки и запятые, если есть (для формата как в 02.11.25 shining.txt)
                        trimmed = trimmed.Trim('"', ',', ' ');
                        
                        // Если строка содержит путь (с обратным слешем), добавляем её
                        if (trimmed.Contains('\\') || trimmed.Contains('/'))
                        {
                            files.Add(trimmed);
                        }
                    }

                    _logger.LogInfo($"Read {files.Count} file paths from TXT file");

                    if (files.Count == 0)
                    {
                        _logger.LogWarning("TXT file does not contain any valid file paths");
                        WarningWindow.Show(_localization.GetString("TxtFileNoValidPaths"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                else
                {
                    _logger.LogInfo($"Reading JSON file: {jsonFilePath}");
                    // Читаем JSON файл
                    var snapshotJson = System.IO.File.ReadAllText(jsonFilePath);

                    // Пробуем разные форматы JSON
                    try
                    {
                        // Формат 1: Простой массив строк
                        files = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(snapshotJson);
                    }
                    catch
                    {
                        try
                        {
                            // Формат 2: Объект с полем Files
                            var snapshot = Newtonsoft.Json.JsonConvert.DeserializeObject<VortexSnapshot>(snapshotJson);
                            files = snapshot?.Files;
                        }
                        catch
                        {
                            try
                            {
                                // Формат 3: Объект с корневым Files
                                var snapshotRoot = Newtonsoft.Json.JsonConvert.DeserializeObject<VortexSnapshotRoot>(snapshotJson);
                                files = snapshotRoot?.Files;
                            }
                            catch
                            {
                                WarningWindow.Show(_localization.GetString("JsonUnsupportedFormat"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }
                        }
                    }

                    if (files == null || files.Count == 0)
                    {
                        WarningWindow.Show(_localization.GetString("JsonFileEmptyOrInvalid"), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Извлекаем порядок модов из файла
                // Список для сохранения порядка первого вхождения каждой папки
                var modOrderList = new List<string>();
                var seenMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    if (string.IsNullOrEmpty(file) || (!file.Contains('\\') && !file.Contains('/')))
                        continue;

                    var separator = file.Contains('\\') ? '\\' : '/';
                    var parts = file.Split(separator);
                    if (parts.Length > 0)
                    {
                        var folderName = parts[0].Trim();
                        
                        // Проверяем формат с префиксом типа AAA-, AAB-, AAH- и т.д.
                        // Ищем первый дефис, после которого начинается название мода
                        var firstDashIndex = folderName.IndexOf('-');
                        if (firstDashIndex > 0 && firstDashIndex < folderName.Length - 1)
                        {
                            var prefix = folderName[..firstDashIndex];
                            
                            // Проверяем, что префикс состоит только из букв (например, AAA, AAB, AAH)
                            if (prefix.Length >= 2 && prefix.All(c => System.Char.IsLetter(c)))
                            {
                                // Извлекаем имя мода без префикса (все после первого дефиса)
                                var modName = folderName[(firstDashIndex + 1)..];

                                // Пропускаем пустые имена
                                if (string.IsNullOrWhiteSpace(modName))
                                    continue;

                                // Используем HashSet для уникальности (без учета регистра)
                                var modNameKey = modName.ToLowerInvariant();

                                if (seenMods.Contains(modNameKey)) continue;

                                seenMods.Add(modNameKey);
                                modOrderList.Add(modName); // Сохраняем оригинальное имя с регистром
                                _logger.LogDebug($"Found mod in order [{modOrderList.Count - 1}]: '{modName}' (from '{folderName}')");
                            }
                        }
                    }
                }

                _logger.LogInfo($"Extracted {modOrderList.Count} unique mods from file");
                
                // Логируем первые несколько модов для проверки порядка
                var firstMods = modOrderList.Take(Math.Min(10, modOrderList.Count));
                _logger.LogDebug($"First mods in order: {string.Join(" -> ", firstMods)}");

                // Создаем словарь для быстрого поиска текущих модов (ключ - имя в нижнем регистре)
                var modsByLowerName = _mods.ToDictionary(
                    m => m.Name.ToLowerInvariant(), 
                    m => m,
                    StringComparer.OrdinalIgnoreCase);

                // Функция для извлечения базового названия мода (без версий и ID)
                static string GetBaseModName(string fullName)
                {
                    // Убираем версии типа -1621-1-9-1760894384 или -1621-2-0-1761270207
                    // Ищем паттерн: буквы-цифры-цифры-цифры-цифры
                    var baseName = fullName;
                    
                    // Удаляем суффиксы типа "(v2.0)" или "-rev75"
                    baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"\s*\(v?\d+[.\d]*\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"-rev\d+$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    // Удаляем ID версии в конце типа "-1621-1-9-1760894384"
                    // Паттерн: -число-число-число-число (4 группы чисел с дефисами)
                    baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"-\d+-\d+-\d+-\d+$", "");
                    // Также паттерн: -число-число-число (3 группы чисел с дефисами)
                    baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"-\d+-\d+-\d+$", "");
                    
                    return baseName.Trim();
                }

                // Создаем словарь по базовым названиям для более точного сопоставления
                var modsByBaseName = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
                foreach (var mod in modsByLowerName.Values)
                {
                    var baseName = GetBaseModName(mod.Name).ToLowerInvariant();
                    if (!modsByBaseName.TryGetValue(baseName, out List<ModInfo>? value))
                    {
                        value = [];
                        modsByBaseName[baseName] = value;
                    }

                    value.Add(mod);
                }

                // Обновляем порядок модов согласно порядку из файла
                int newOrder = 0;
                var matchedMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var modNameFromFile in modOrderList)
                {
                    var modNameLower = modNameFromFile.ToLowerInvariant();
                    var baseNameFromFile = GetBaseModName(modNameFromFile).ToLowerInvariant();
                    
                    ModInfo? matchedMod = null;
                    string matchType = "";
                    
                    // 1. Пробуем точное совпадение (без учета регистра)
                    if (modsByLowerName.TryGetValue(modNameLower, out var exactMod))
                    {
                        if (!matchedMods.Contains(exactMod.Name))
                        {
                            matchedMod = exactMod;
                            matchType = "exact";
                        }
                    }

                    // 2. Если точного совпадения нет, пробуем по базовому названию
                    if (matchedMod == null && modsByBaseName.TryGetValue(baseNameFromFile, out var modsWithSameBase))
                    {
                        // Берем первый не сопоставленный мод с таким базовым названием
                        matchedMod = modsWithSameBase.FirstOrDefault(m => !matchedMods.Contains(m.Name));
                        if (matchedMod != null)
                        {
                            matchType = "base name";
                        }
                    }

                    // 3. Если всё еще не нашли, ищем частичное совпадение
                    if (matchedMod == null)
                    {
                        ModInfo? bestMatch = null;
                        int bestMatchScore = 0;

                        foreach (var mod in modsByLowerName.Values)
                        {
                            if (matchedMods.Contains(mod.Name))
                                continue;

                            var modNameLowerForMatch = mod.Name.ToLowerInvariant();
                            var baseNameForMatch = GetBaseModName(mod.Name).ToLowerInvariant();
                            int score = 0;

                            // Проверяем совпадение базовых названий
                            if (baseNameForMatch == baseNameFromFile && !string.IsNullOrEmpty(baseNameFromFile))
                            {
                                score = 900; // Совпадение базовых названий - очень высокий приоритет
                            }
                            // Проверяем разные типы совпадений с разными весами
                            else if (modNameLowerForMatch == modNameLower)
                            {
                                score = 1000; // Точное совпадение (уже должно быть обработано выше, но на всякий случай)
                            }
                            else if (modNameLowerForMatch.StartsWith(modNameLower) || modNameLower.StartsWith(modNameLowerForMatch))
                            {
                                score = 500; // Начинается с
                            }
                            else if (modNameLowerForMatch.EndsWith(modNameLower) || modNameLower.EndsWith(modNameLowerForMatch))
                            {
                                score = 400; // Заканчивается на
                            }
                            else if (baseNameForMatch.Contains(baseNameFromFile) || baseNameFromFile.Contains(baseNameForMatch))
                            {
                                score = 450; // Базовое название содержит
                            }
                            else if (modNameLowerForMatch.Contains(modNameLower) || modNameLower.Contains(modNameLowerForMatch))
                            {
                                // Вычисляем длину совпадающей части
                                var shorter = modNameLower.Length < modNameLowerForMatch.Length ? modNameLower : modNameLowerForMatch;
                                var longer = modNameLower.Length >= modNameLowerForMatch.Length ? modNameLower : modNameLowerForMatch;
                                
                                // Ищем наибольшую общую подстроку
                                int maxCommonLength = 0;
                                for (int i = 0; i <= longer.Length - shorter.Length; i++)
                                {
                                    int commonLength = 0;
                                    for (int j = 0; j < shorter.Length; j++)
                                    {
                                        if (longer[i + j] == shorter[j])
                                            commonLength++;
                                        else
                                            break;
                                    }
                                    if (commonLength > maxCommonLength)
                                        maxCommonLength = commonLength;
                                }
                                score = 300 + maxCommonLength; // Содержит, с бонусом за длину совпадения
                            }

                            if (score > bestMatchScore)
                            {
                                bestMatchScore = score;
                                bestMatch = mod;
                            }
                        }

                        if (bestMatch != null)
                        {
                            matchedMod = bestMatch;
                            matchType = $"partial (score: {bestMatchScore})";
                        }
                    }

                    if (matchedMod != null)
                    {
                        matchedMod.Order = newOrder++;
                        matchedMods.Add(matchedMod.Name);
                        _logger.LogDebug($"Matched ({matchType}): '{modNameFromFile}' -> '{matchedMod.Name}' (Order: {matchedMod.Order})");
                    }
                    else
                    {
                        _logger.LogWarning($"Could not match mod from file: '{modNameFromFile}' (base: '{baseNameFromFile}')");
                    }
                }

                // Добавляем оставшиеся (не сопоставленные) моды в конец
                foreach (var mod in modsByLowerName.Values)
                {
                    if (!matchedMods.Contains(mod.Name))
                    {
                        mod.Order = newOrder++;
                        _logger.LogDebug($"Added unmatched mod '{mod.Name}' at end (Order: {mod.Order})");
                    }
                }

                // Сортируем список по порядку
                var sortedMods = _mods.OrderBy(m => m.Order).ToList();
                _mods.Clear();
                foreach (var mod in sortedMods)
                {
                    _mods.Add(mod);
                }

                UpdateOrders();
                var fileName = System.IO.Path.GetFileName(jsonFilePath);
                var fileType = System.IO.Path.GetExtension(jsonFilePath).Equals(".txt", StringComparison.CurrentCultureIgnoreCase) ? "TXT" : "JSON";
                
                // Обновляем состояние кнопки Install Mods
                UpdateInstallButtonState();
                
                // Применяем сортировку после сортировки модов по JSON/TXT
                ApplySorting();
                UpdateSortHeaderButtons();
                
                UpdateStatus($"Sorted {_mods.Count} mods according to {fileName} ({fileType})");
                _logger.LogSuccess($"Sorted {_mods.Count} mods according to {fileName} ({fileType}). Found {modOrderList.Count} mods in file");

                WarningWindow.Show(
                    _localization.GetString("ModsSortedSuccess", fileName, modOrderList.Count),
                    _localization.GetString("Success"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error sorting mods by JSON file", ex);
                WarningWindow.Show($"{_localization.GetString("ErrorSortingMods")}: {ex.Message}", _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Keyboard shortcuts
        private void ModsListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // CTRL+A - Select all
            if (e.Key == System.Windows.Input.Key.A && 
                (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                if (ModsListBox != null && ModsListBox.Items.Count > 0)
                {
                    ModsListBox.SelectAll();
                    e.Handled = true;
                }
            }
        }

        // Drag and Drop handlers
        private void ModsListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.ListBox listBox) return;

            var item = System.Windows.Input.Mouse.DirectlyOver;
            var checkBox = FindParent<System.Windows.Controls.CheckBox>(item as DependencyObject);
            
            // Если клик по чекбоксу, не начинаем drag и позволяем стандартному поведению выделения работать
            if (checkBox != null)
            {
                return;
            }

            // Проверяем, нажата ли клавиша Ctrl или Shift
            bool isCtrlPressed = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
            bool isShiftPressed = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift;
            
            _dragStartPoint = e.GetPosition(null);
            var hitItem = listBox.GetItemAt(e.GetPosition(listBox));
            if (hitItem is ModInfo mod)
            {
                _draggedMod = mod;
                
                // Если нажат Ctrl или Shift, не трогаем выделение - позволяем стандартному поведению ListBox работать
                if (!isCtrlPressed && !isShiftPressed)
                {
                    // Если элемент уже выделен один, не меняем выделение (для drag & drop)
                    if (listBox.SelectedItems.Count == 1 && listBox.SelectedItem == mod)
                    {
                        // Элемент уже выделен, продолжаем для drag & drop
                    }
                    else
                    {
                        listBox.SelectedItem = mod;
                    }
                }
                // При Ctrl+клике или Shift+клике стандартное поведение ListBox автоматически обработает выделение
                
                // Находим соответствующий ListBoxItem для визуальной обратной связи
                _draggedListItem = listBox.ItemContainerGenerator.ContainerFromItem(mod) as System.Windows.Controls.ListBoxItem;
            }
        }

        private void ModsListBox_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_draggedMod == null) return;

            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                var currentPoint = e.GetPosition(null);
                var distance = (currentPoint - _dragStartPoint).Length;

                // Начинаем drag если мышь переместилась достаточно далеко
                if (distance > 5)
                {
                    if (sender is System.Windows.Controls.ListBox listBox)
                    {
                        // Применяем визуальный эффект к перетаскиваемому элементу
                        ApplyDragVisualEffect();

                        // Создаем индикатор вставки
                        CreateInsertionIndicator();

                        // Подписываемся на GiveFeedback для визуальной обратной связи
                        listBox.GiveFeedback += ModsListBox_GiveFeedback;

                        var data = new System.Windows.DataObject(typeof(ModInfo), _draggedMod);
                        System.Windows.DragDrop.DoDragDrop(listBox, data, System.Windows.DragDropEffects.Move);

                        listBox.GiveFeedback -= ModsListBox_GiveFeedback;

                        // Удаляем индикатор вставки
                        RemoveInsertionIndicator();

                        // Восстанавливаем визуальный стиль после завершения drag
                        RestoreDraggedItemVisual();

                        _draggedMod = null;
                        _draggedListItem = null;
                        _scrollTimer.Stop(); // Останавливаем автоскролл после завершения drag
                    }
                }
            }
            else
            {
                // Если кнопка мыши отпущена до начала drag, сбрасываем состояние
                if (_draggedMod != null)
                {
                    RemoveInsertionIndicator();
                    RestoreDraggedItemVisual();
                    _draggedMod = null;
                    _draggedListItem = null;
                }
            }
        }

        private void ModsListBox_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(ModInfo)))
            {
                e.Effects = System.Windows.DragDropEffects.Move;
                
                var listBox = sender as System.Windows.Controls.ListBox;
                
                // Обновляем индикатор вставки
                UpdateInsertionIndicator(listBox, e);
                
                // Автоскролл при перетаскивании
                if (listBox != null)
                {
                    var scrollViewer = GetScrollViewer(listBox);
                    if (scrollViewer != null && scrollViewer.ScrollableHeight > 0)
                    {
                        var mousePos = e.GetPosition(listBox);
                        var scrollArea = 50.0; // Область автоскролла (пиксели от края)
                        var baseScrollSpeed = 1.5; // Базовая скорость скролла
                        var maxScrollSpeed = 4.0; // Максимальная скорость скролла
                        
                        // Проверяем, близко ли мышь к краям
                        if (mousePos.Y < scrollArea && mousePos.Y >= 0)
                        {
                            // Скроллим вверх с адаптивной скоростью
                            var distanceFromEdge = mousePos.Y;
                            var speedFactor = 1.0 - (distanceFromEdge / scrollArea);
                            var scrollSpeed = baseScrollSpeed + (maxScrollSpeed - baseScrollSpeed) * speedFactor;
                            
                            var newOffset = scrollViewer.VerticalOffset - scrollSpeed;
                            if (newOffset >= 0)
                            {
                                scrollViewer.ScrollToVerticalOffset(newOffset);
                            }
                            
                            // Запускаем таймер для плавного скролла
                            if (!_scrollTimer.IsEnabled)
                            {
                                _scrollTimer.Start();
                            }
                        }
                        else if (mousePos.Y > listBox.ActualHeight - scrollArea && mousePos.Y <= listBox.ActualHeight)
                        {
                            // Скроллим вниз с адаптивной скоростью
                            var distanceFromEdge = listBox.ActualHeight - mousePos.Y;
                            var speedFactor = 1.0 - (distanceFromEdge / scrollArea);
                            var scrollSpeed = baseScrollSpeed + (maxScrollSpeed - baseScrollSpeed) * speedFactor;
                            
                            var newOffset = scrollViewer.VerticalOffset + scrollSpeed;
                            var maxOffset = scrollViewer.ScrollableHeight;
                            if (newOffset <= maxOffset)
                            {
                                scrollViewer.ScrollToVerticalOffset(newOffset);
                            }
                            
                            // Запускаем таймер для плавного скролла
                            if (!_scrollTimer.IsEnabled)
                            {
                                _scrollTimer.Start();
                            }
                        }
                        else
                        {
                            // Останавливаем таймер, если мышь в середине
                            _scrollTimer.Stop();
                        }
                    }
                }
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
                _scrollTimer.Stop();
            }
            e.Handled = true;
        }

        private void ModsListBox_GiveFeedback(object sender, System.Windows.GiveFeedbackEventArgs e)
        {
            // Используем стандартный курсор Move для визуальной обратной связи
            e.UseDefaultCursors = false;
            System.Windows.Input.Mouse.SetCursor(System.Windows.Input.Cursors.SizeAll);
            e.Handled = true;
        }

        private void ApplyDragVisualEffect()
        {
            // Находим ListBoxItem, если он еще не был найден
            if (_draggedListItem == null && _draggedMod != null)
            {
                var listBox = ModsListBox;
                _draggedListItem = listBox.ItemContainerGenerator.ContainerFromItem(_draggedMod) as System.Windows.Controls.ListBoxItem;
                
                // Если элемент еще не создан в визуальном дереве, пытаемся найти его через поиск
                if (_draggedListItem == null)
                {
                    foreach (var item in listBox.Items)
                    {
                        if (item == _draggedMod)
                        {
                            if (listBox.ItemContainerGenerator.ContainerFromItem(item) is System.Windows.Controls.ListBoxItem container)
                            {
                                _draggedListItem = container;
                                break;
                            }
                        }
                    }
                }
            }
            
            if (_draggedListItem != null)
            {
                _draggedListItem.Opacity = 0.5;
                var scaleTransform = new System.Windows.Media.ScaleTransform(0.95, 0.95);
                _draggedListItem.RenderTransform = scaleTransform;
                _draggedListItem.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Colors.Blue,
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.8
                };
            }
        }

        private void CreateInsertionIndicator()
        {
            try
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(ModsListBox);
                if (adornerLayer == null)
                {
                    _logger.LogWarning("AdornerLayer not found for ListBox, insertion indicator may not display");
                    return;
                }
                
                _insertionLineAdorner = new InsertionLineAdorner(ModsListBox);
                adornerLayer.Add(_insertionLineAdorner);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating insertion indicator: {ex.Message}");
            }
        }
        
        private void RemoveInsertionIndicator()
        {
            if (_insertionLineAdorner != null)
            {
                try
                {
                    var adornerLayer = AdornerLayer.GetAdornerLayer(ModsListBox);
                    adornerLayer?.Remove(_insertionLineAdorner);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error removing insertion indicator: {ex.Message}");
                }
                finally
                {
                    _insertionLineAdorner = null;
                }
            }
        }

        private void UpdateInsertionIndicator(System.Windows.Controls.ListBox? listBox, System.Windows.DragEventArgs e)
        {
            if (_insertionLineAdorner == null || listBox == null) return;
            
            try
            {
                var point = e.GetPosition(listBox);
                var item = listBox.GetItemAt(point);
                
                if (item is ModInfo targetMod && targetMod != _draggedMod)
                {
                    // Находим ListBoxItem для целевого элемента
                    if (listBox.ItemContainerGenerator.ContainerFromItem(targetMod) is System.Windows.Controls.ListBoxItem targetListItem)
                    {
                        // Определяем, вставляем ли мы выше или ниже элемента
                        var itemPoint = e.GetPosition(targetListItem);
                        var isAbove = itemPoint.Y < targetListItem.ActualHeight / 2;

                        var insertionY = isAbove
                            ? targetListItem.TranslatePoint(new Point(0, 0), listBox).Y
                            : targetListItem.TranslatePoint(new Point(0, targetListItem.ActualHeight), listBox).Y;

                        _insertionLineAdorner.UpdatePosition(insertionY);
                    }
                }
                else if (item == null)
                {
                    // Если не нашли элемент, проверяем, показывать ли внизу или вверху
                    if (point.Y < listBox.ActualHeight && point.Y >= 0)
                    {
                        // Если мышь в списке, но не на элементе, проверяем ближайший элемент
                        var nearestY = point.Y;
                        _insertionLineAdorner.UpdatePosition(nearestY);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating insertion indicator: {ex.Message}");
            }
        }

        private void RestoreDraggedItemVisual()
        {
            if (_draggedListItem != null)
            {
                _draggedListItem.Opacity = 1.0;
                _draggedListItem.RenderTransform = null;
                _draggedListItem.Effect = null;
            }
        }

        private void ModsListBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
            // Удаляем индикатор вставки
            RemoveInsertionIndicator();
            
            // Восстанавливаем визуальный стиль перетаскиваемого элемента
            RestoreDraggedItemVisual();

            if (sender is not System.Windows.Controls.ListBox listBox) return;

            ModInfo? draggedMod = null;
            
            // Пытаемся получить данные из DragEventArgs
            if (e.Data.GetDataPresent(typeof(ModInfo)))
            {
                draggedMod = e.Data.GetData(typeof(ModInfo)) as ModInfo;
            }
            else if (_draggedMod != null)
            {
                draggedMod = _draggedMod;
            }

            if (draggedMod == null) return;

            var point = e.GetPosition(listBox);
            var targetItem = listBox.GetItemAt(point);
            
            if (targetItem is ModInfo targetMod && targetMod != draggedMod)
            {
                int oldIndex = _mods.IndexOf(draggedMod);
                int targetIndex = _mods.IndexOf(targetMod);

                // Определяем, вставляем ли мы выше или ниже элемента
                int newIndex = targetIndex;

                if (listBox.ItemContainerGenerator.ContainerFromItem(targetMod) is System.Windows.Controls.ListBoxItem targetListItem)
                {
                    var itemPoint = e.GetPosition(targetListItem);
                    var isAbove = itemPoint.Y < targetListItem.ActualHeight / 2;
                    
                    if (isAbove)
                    {
                        // Вставляем выше элемента
                        newIndex = targetIndex;
                    }
                    else
                    {
                        // Вставляем ниже элемента
                        newIndex = targetIndex + 1;
                    }
                }

                // Корректируем индекс, если старый индекс меньше нового (после удаления индекс сдвинется)
                if (oldIndex < newIndex)
                {
                    newIndex--;
                }

                if (oldIndex != newIndex)
                {
                    _mods.RemoveAt(oldIndex);
                    _mods.Insert(newIndex, draggedMod);
                    UpdateOrders();
                    // Применяем сортировку после перемещения, чтобы обновить визуальное представление
                    ApplySorting();
                    UpdateSortHeaderButtons();
                    ModsListBox.SelectedItem = draggedMod;
                    _logger.LogDebug($"Moved mod '{draggedMod.Name}' via drag-drop (from {oldIndex} to {newIndex})");
                }
            }
            else if (targetItem == null)
            {
                // Если не нашли элемент, пытаемся определить позицию на основе позиции курсора
                // Проверяем, находится ли курсор в пределах списка
                if (point.Y >= 0 && point.Y <= listBox.ActualHeight)
                {
                    // Находим ближайший элемент
                    for (int i = 0; i < listBox.Items.Count; i++)
                    {
                        if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is System.Windows.Controls.ListBoxItem item)
                        {
                            var itemTop = item.TranslatePoint(new Point(0, 0), listBox).Y;
                            var itemBottom = itemTop + item.ActualHeight;

                            if (point.Y >= itemTop && point.Y <= itemBottom)
                            {
                                // Нашли элемент, используем ту же логику
                                var itemPoint = e.GetPosition(item);
                                var isAbove = itemPoint.Y < item.ActualHeight / 2;

                                int oldIndex = _mods.IndexOf(draggedMod);
                                int newIndex = i;

                                if (!isAbove && oldIndex < newIndex)
                                {
                                    newIndex++;
                                }

                                if (oldIndex != newIndex)
                                {
                                    _mods.RemoveAt(oldIndex);
                                    if (oldIndex < newIndex)
                                    {
                                        newIndex--;
                                    }
                                    _mods.Insert(newIndex, draggedMod);
                                    UpdateOrders();
                                    // Применяем сортировку после перемещения, чтобы обновить визуальное представление
                                    ApplySorting();
                                    UpdateSortHeaderButtons();
                                    ModsListBox.SelectedItem = draggedMod;
                                    _logger.LogDebug($"Moved mod '{draggedMod.Name}' via drag-drop (from {oldIndex} to {newIndex})");
                                }
                                break;
                            }
                            else if (point.Y < itemTop && i == 0)
                            {
                                // Вставляем в начало
                                int oldIndex = _mods.IndexOf(draggedMod);
                                if (oldIndex != 0)
                                {
                                    _mods.RemoveAt(oldIndex);
                                    _mods.Insert(0, draggedMod);
                                    UpdateOrders();
                                    // Применяем сортировку после перемещения, чтобы обновить визуальное представление
                                    ApplySorting();
                                    UpdateSortHeaderButtons();
                                    ModsListBox.SelectedItem = draggedMod;
                                    _logger.LogDebug($"Moved mod '{draggedMod.Name}' via drag-drop (from {oldIndex} to 0)");
                                }
                                break;
                            }
                            else if (point.Y > itemBottom && i == listBox.Items.Count - 1)
                            {
                                // Вставляем в конец
                                int oldIndex = _mods.IndexOf(draggedMod);
                                int newIndex = _mods.Count - 1;
                                if (oldIndex != newIndex)
                                {
                                    _mods.RemoveAt(oldIndex);
                                    if (oldIndex < newIndex)
                                    {
                                        newIndex--;
                                    }
                                    _mods.Insert(newIndex, draggedMod);
                                    UpdateOrders();
                                    // Применяем сортировку после перемещения, чтобы обновить визуальное представление
                                    ApplySorting();
                                    UpdateSortHeaderButtons();
                                    ModsListBox.SelectedItem = draggedMod;
                                    _logger.LogDebug($"Moved mod '{draggedMod.Name}' via drag-drop (from {oldIndex} to {newIndex})");
                                }
                                break;
                            }
                        }
                    }
                }
            }

            _draggedMod = null;
            _draggedListItem = null;
            _scrollTimer.Stop(); // Останавливаем автоскролл при завершении перетаскивания
            e.Handled = true;
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, нажата ли клавиша Ctrl
            bool isCtrlPressed = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
            
            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.DataContext is ModInfo clickedMod)
            {
                // Новое состояние чекбокса (IsChecked уже обновлён событием Click)
                bool? isChecked = checkBox.IsChecked;
                bool newState = isChecked == true;

                // Если нажат Ctrl, добавляем/удаляем элемент из выделения
                if (isCtrlPressed)
                {
                    // Если элемент уже выделен, удаляем его из выделения
                    if (ModsListBox.SelectedItems.Contains(clickedMod))
                    {
                        ModsListBox.SelectedItems.Remove(clickedMod);
                    }
                    else
                    {
                        // Если элемент не выделен, добавляем его в выделение
                        ModsListBox.SelectedItems.Add(clickedMod);
                    }
                    // Не сбрасываем выделение при Ctrl+клике
                    return;
                }
                
                // Проверяем, выделено ли несколько элементов (больше одного)
                if (ModsListBox.SelectedItems.Count > 1)
                {
                    // Если выделено несколько элементов, изменяем состояние всех выделенных чекбоксов
                    // Применяем это состояние ко всем выделенным модам
                    foreach (ModInfo mod in ModsListBox.SelectedItems)
                    {
                        if (mod is ModInfo selectedMod)
                        {
                            // Запрещаем включать моды без файлов или без включённых файлов
                            if (newState && (!selectedMod.HasAnyFiles || !selectedMod.HasAnyEnabledFiles))
                            {
                                // Возвращаем визуальное состояние чекбокса для этого мода
                                selectedMod.GroupIsEnabled = false;
                                continue;
                            }

                            selectedMod.IsEnabled = newState;
                        }
                    }
                    
                    // Явно вызываем MarkAsChanged после изменения IsEnabled
                    MarkAsChanged();
                    
                    e.Handled = true;
                    return;
                }

                // Один мод (или выделение из одного элемента)
                // Если пытаемся включить мод, у которого нет файлов, или у которого все файлы выключены — запрещаем.
                if (newState && (!clickedMod.HasAnyFiles || !clickedMod.HasAnyEnabledFiles))
                {
                    // Возвращаем визуальное состояние чекбокса
                    clickedMod.GroupIsEnabled = false;
                    checkBox.IsChecked = false;
                    e.Handled = true;
                    return;
                }

                // Разрешённое включение/выключение одиночного мода
                clickedMod.IsEnabled = newState;
                MarkAsChanged();
                e.Handled = true;
                return;
            }
            
            // Если Ctrl не нажат и выделен один или ноль элементов, предотвращаем выделение элемента ListBox при клике на чекбокс
            // Сбрасываем выделение, если оно есть
            if (!isCtrlPressed && ModsListBox.SelectedItem != null)
            {
                ModsListBox.SelectedItem = null;
            }
            
            // Явно вызываем MarkAsChanged после изменения IsEnabled через CheckBox
            // (для одного мода это должно работать автоматически через подписку, но на всякий случай вызываем явно)
            MarkAsChanged();
        }

        private void ModsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ModsListBox.SelectedItem is ModInfo selectedMod)
            {
                OpenModFilesWindow(selectedMod);
            }
        }

        private void OpenModFiles_Click(object sender, RoutedEventArgs e)
        {
            if (ModsListBox.SelectedItem is ModInfo selectedMod)
            {
                OpenModFilesWindow(selectedMod);
            }
        }

        private void ModsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Обновляем состояние кнопки открытия файлов мода
            if (OpenModFilesButton != null)
            {
                OpenModFilesButton.IsEnabled = ModsListBox.SelectedItem is ModInfo;
            }
        }

        private void OpenModFilesWindow(ModInfo mod)
        {
            try
            {
                // Находим все версии этого мода с тем же базовым именем (NormalizeOrderName)
                var baseName = NormalizeOrderName(mod.Name);
                var versions = _mods
                    .Where(m => string.Equals(NormalizeOrderName(m.Name), baseName, StringComparison.InvariantCultureIgnoreCase))
                    .ToList();

                var window = new ModFilesWindow(mod, versions)
                {
                    Owner = this
                };
                window.ShowDialog();
                
                // Индикатор обновляется автоматически через SetFileEnabled,
                // который вызывает OnPropertyChanged(nameof(HasDisabledFiles))
                // Дополнительное обновление не требуется
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error opening mod files window: {ex.Message}", ex);
                WarningWindow.Show(
                    $"Error opening mod files: {ex.Message}",
                    _localization.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Window management handlers
        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                DragMove();
            }
        }

        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            _logger.LogInfo("Application closing");
            Close();
        }

        private void InitializeLanguageComboBox()
        {
            try
            {
                var availableLanguages = _localization.GetAvailableLanguages();
                var languageNames = _localization.GetLanguageNames();
                
                LanguageComboBox.Items.Clear();
                
                foreach (var langCode in availableLanguages)
                {
                    var displayName = languageNames.TryGetValue(langCode, out string? value)
                        ? $"{value} ({langCode.ToUpper()})" 
                        : langCode.ToUpper();
                    
                    var item = new System.Windows.Controls.ComboBoxItem
                    {
                        Content = displayName,
                        Tag = langCode
                    };
                    
                    LanguageComboBox.Items.Add(item);
                    
                    // Устанавливаем выбранный язык
                    if (langCode == _localization.CurrentLanguage)
                    {
                        LanguageComboBox.SelectedItem = item;
                    }
                }
                
                // Если ничего не выбрано, выбираем первый элемент
                if (LanguageComboBox.SelectedItem == null && LanguageComboBox.Items.Count > 0)
                {
                    LanguageComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error initializing language combo box", ex);
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                if (LanguageComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
                {
                    var langCode = selectedItem.Tag?.ToString();
                    if (!string.IsNullOrEmpty(langCode) && langCode != _localization.CurrentLanguage)
                    {
                        _localization.CurrentLanguage = langCode;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error changing language", ex);
            }
        }

        private void Localization_LanguageChanged(object? sender, EventArgs e)
        {
            // Обновляем выбранный язык в ComboBox
            foreach (System.Windows.Controls.ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == _localization.CurrentLanguage)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }
            
            UpdateLocalization();
        }

        private void LoadVersion()
        {
            try
            {
                var updateService = new Services.UpdateService();
                var version = updateService.GetCurrentVersion();
                if (VersionTextBlock != null)
                {
                    VersionTextBlock.Text = $"v{version}";
                    VersionTextBlock.Visibility = Visibility.Visible;
                    _logger.LogDebug($"Application version loaded: {version}");
                }
                else
                {
                    _logger.LogWarning("VersionTextBlock is null");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to load version", ex);
                if (VersionTextBlock != null)
                {
                    VersionTextBlock.Text = "v1.0.0";
                    VersionTextBlock.Visibility = Visibility.Visible;
                }
            }
        }

        private void UpdateLocalization()
        {
            try
            {
                // Обновляем заголовок окна
                Title = _localization.GetString("WindowTitle");
                TitleTextBlock.Text = _localization.GetString("WindowTitle");
                
                // Paths GroupBox
                PathsGroupBox.Header = _localization.GetString("Paths");
                
                // Labels
                VortexPathLabel.Content = _localization.GetString("VortexPath");
                TargetPathLabel.Content = _localization.GetString("TargetPath");
                
                // Buttons
                BrowseVortexButton.Content = _localization.GetString("Browse");
                BrowseTargetButton.Content = _localization.GetString("Browse");
                
                // Main menu headers
                FileMenuItem.Header = "_" + _localization.GetString("File");
                OrderMenuItem.Header = "_" + _localization.GetString("Order");
                ToolsMenuItem.Header = "_" + _localization.GetString("Tools");
                InstallMenuItem.Header = "_" + _localization.GetString("Install");
                
                // Action menu items
                LoadModsMenuItem.Header = "_" + _localization.GetString("LoadMods");
                SaveConfigMenuItem.Header = "_" + _localization.GetString("SaveConfig");
                LoadConfigMenuItem.Header = "_" + _localization.GetString("LoadConfig");
                ExportOrderMenuItem.Header = "_" + _localization.GetString("ExportOrder");
                ImportOrderMenuItem.Header = "_" + _localization.GetString("ImportOrder");
                AdvancedMenuItem.Header = "_" + _localization.GetString("Advanced");
                AboutMenuItem.Header = "_" + _localization.GetString("About");
                InstallModsMenuItem.Header = _localization.GetString("InstallMods");
                ClearModsMenuItem.Header = "_" + _localization.GetString("ClearMods");
                DlcModLoaderMenuItem.Header = "_" + _localization.GetString("DlcModLoader");
                
                // Mods GroupBox
                ModsGroupBox.Header = _localization.GetString("Mods");
                
                // Move buttons
                if (OpenModFilesButton != null)
                    OpenModFilesButton.Content = _localization.GetString("OpenModFiles");
                MoveUpButton.Content = _localization.GetString("MoveUp");
                MoveDownButton.Content = _localization.GetString("MoveDown");
                
                // Save/Cancel buttons
                if (SaveChangesButton != null)
                {
                    SaveChangesButton.Content = _localization.GetString("SaveChanges");
                }
                if (CancelChangesButton != null)
                {
                    CancelChangesButton.Content = _localization.GetString("CancelChanges");
                }
                
                // Install Mods button
                InstallModsButton.Content = _localization.GetString("InstallMods");
                
                // Launch Stalker 2 button
                if (LaunchStalker2Button != null)
                {
                    LaunchStalker2Button.Content = _localization.GetString("LaunchStalker2");
                }

                // Column headers

                if (FindName("OrderHeader") is System.Windows.Controls.TextBlock orderHeader)
                    orderHeader.Text = _localization.GetString("OrderHeader");
                if (FindName("NameHeader") is System.Windows.Controls.TextBlock nameHeader)
                    nameHeader.Text = _localization.GetString("NameHeader");
                if (FindName("TargetFolderHeader") is System.Windows.Controls.TextBlock targetFolderHeader)
                    targetFolderHeader.Text = _localization.GetString("TargetFolderHeader");
                
                // Status
                //if (StatusTextBlock.Text == "Ready" || StatusTextBlock.Text == "Готов")
                //{
                //    StatusTextBlock.Text = _localization.GetString("Status");
                //}
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating localization: {ex.Message}");
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Проверяем наличие несохраненных изменений списка модов
            if (_hasUnsavedChanges)
            {
                var result = WarningWindow.Show(
                    _localization.GetString("UnsavedChangesConfirmation"),
                    _localization.GetString("UnsavedChangesTitle"),
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    SaveModsList_Click(this, new RoutedEventArgs());
                    _isClosing = true;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                else
                {
                    _isClosing = true;
                }
            }
            else
            {
                _isClosing = true;
            }
            
            // Автоматически сохраняем конфиг и порядок модов при закрытии
            if (!e.Cancel)
            {
                SaveConfigOnClose();
                SaveModsOrderOnClose();
            }
            
            base.OnClosing(e);
        }
        
        private void SaveConfigOnClose()
        {
            try
            {
                var pathsConfig = _configService.LoadPathsConfig();
                
                // Обновляем пути, если они изменились
                if (!string.IsNullOrEmpty(VortexPathTextBox.Text))
                {
                    pathsConfig.VortexPath = VortexPathTextBox.Text;
                }
                if (!string.IsNullOrEmpty(TargetPathTextBox.Text))
                {
                    pathsConfig.TargetPath = TargetPathTextBox.Text;
                }
                
                // Сохраняем размер окна
                pathsConfig.WindowWidth = Width;
                pathsConfig.WindowHeight = Height;
                
                // Сохраняем язык
                pathsConfig.Language = _localization.CurrentLanguage;
                
                _configService.SavePathsConfig(pathsConfig);
                _logger.LogDebug("Paths config saved on close");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save paths config on close: {ex.Message}", ex);
            }
        }
        
        private void SaveModsOrderOnClose()
        {
            try
            {
                // Сохраняем порядок модов (включая состояния файлов)
                var modsOrder = _configService.CreateModOrderFromMods([.. _mods]);
                _configService.SaveModsOrder(modsOrder);
                _logger.LogDebug($"Mods order saved on close: {modsOrder.Mods.Count} mods");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save mods order on close: {ex.Message}", ex);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveWindowSize();
            _logger.LogInfo("Application closed");
            base.OnClosed(e);
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SaveWindowSize();
        }

        private void SaveWindowSize()
        {
            try
            {
                var pathsConfig = _configService.LoadPathsConfig();
                pathsConfig.WindowWidth = Width;
                pathsConfig.WindowHeight = Height;
                _configService.SavePathsConfig(pathsConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save window size: {ex.Message}");
            }
        }

        // Resize handlers
        private void ResizeTop_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                ResizeWindow(WindowResizeDirection.Top);
            }
        }

        private void ResizeBottom_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                ResizeWindow(WindowResizeDirection.Bottom);
            }
        }

        private void ResizeLeft_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                ResizeWindow(WindowResizeDirection.Left);
            }
        }

        private void ResizeRight_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                ResizeWindow(WindowResizeDirection.Right);
            }
        }

        private void ResizeTopLeft_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                ResizeWindow(WindowResizeDirection.TopLeft);
            }
        }

        private void ResizeTopRight_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                ResizeWindow(WindowResizeDirection.TopRight);
            }
        }

        private void ResizeBottomLeft_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                ResizeWindow(WindowResizeDirection.BottomLeft);
            }
        }

        private void ResizeBottomRight_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                ResizeWindow(WindowResizeDirection.BottomRight);
            }
        }

        private void ResizeWindow(WindowResizeDirection direction)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SendMessage(hwnd, 0x112, (IntPtr)(61440 + (int)direction), IntPtr.Zero);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // Visual feedback for resize grips
        private void ResizeGrip_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border)
            {
                border.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(100, 0, 122, 204)); // Более видимый при наведении
            }
        }

        private void ResizeGrip_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border)
            {
                border.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(51, 0, 122, 204)); // Менее видимый в обычном состоянии
            }
        }

        // Helper method to find parent control
        private T? FindParent<T>(System.Windows.DependencyObject? child) where T : System.Windows.DependencyObject
        {
            if (child is null)
                return null;

            System.Windows.DependencyObject parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;

            if (parentObject is T parent)
                return parent;
            else
                return FindParent<T>(parentObject);
        }

        private async void CheckForUpdatesAsync()
        {
            try
            {
                using var updateService = new Services.UpdateService();
                var updateInfo = await updateService.CheckForUpdatesAsync();

                if (updateInfo != null)
                {
                    var currentVersion = updateService.GetCurrentVersion();
                    var downloadUrl = updateService.GetDownloadUrl();

                    // Показываем окно обновления в UI потоке
                    Dispatcher.Invoke(() =>
                    {
                        var updateWindow = new UpdateWindow(updateInfo, currentVersion, downloadUrl)
                        {
                            Owner = this
                        };
                        updateWindow.ShowDialog();
                    });

                    _logger.LogInfo($"Update available: {updateInfo.Version}");
                }
                else
                {
                    _logger.LogInfo("No updates available");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error checking for updates on startup", ex);
                // Не показываем ошибку пользователю, чтобы не мешать работе
            }
        }
    }

    // Enum for window resize directions
    enum WindowResizeDirection
    {
        Left = 1,
        Right = 2,
        Top = 3,
        TopLeft = 4,
        TopRight = 5,
        Bottom = 6,
        BottomLeft = 7,
        BottomRight = 8
    }
}

// Extension method for ListBox to get item at point
//public static class ListBoxExtensions
//{
//    public static object? GetItemAt(this System.Windows.Controls.ListBox listBox, System.Windows.Point point)
//    {
//        var element = listBox.InputHitTest(point) as System.Windows.DependencyObject;
//        while (element != null)
//        {
//            if (element is System.Windows.Controls.ListBoxItem item)
//            {
//                return item.Content;
//            }
//            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
//        }
//        return null;
//    }
//}

// Adorner для отображения линии вставки при перетаскивании

