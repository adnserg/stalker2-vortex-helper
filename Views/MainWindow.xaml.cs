using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using Stalker2ModManager.Models;
using Stalker2ModManager.Services;

namespace Stalker2ModManager.Views
{
    public partial class MainWindow : Window
    {
        private readonly ModManagerService _modManagerService;
        private readonly ConfigService _configService;
        private readonly Services.Logger _logger;
        private readonly Services.LocalizationService _localization;
        private ObservableCollection<ModInfo> _mods;
        private System.Windows.Data.CollectionViewSource _modsViewSource;
        private ModInfo _draggedMod;
        private Point _dragStartPoint;
        private System.Windows.Threading.DispatcherTimer _scrollTimer;
        private System.Windows.Controls.ListBoxItem _draggedListItem;
        private InsertionLineAdorner _insertionLineAdorner;

        public MainWindow()
        {
            InitializeComponent();
            _modManagerService = new ModManagerService();
            _configService = new ConfigService();
            _logger = Services.Logger.Instance;
            _localization = Services.LocalizationService.Instance;
            _mods = new ObservableCollection<ModInfo>();
            _modsViewSource = new System.Windows.Data.CollectionViewSource();
            _modsViewSource.Source = _mods;
            _modsViewSource.Filter += ModsViewSource_Filter;
            ModsListBox.ItemsSource = _modsViewSource.View;

            _logger.LogInfo("Application started");
            
            // Загружаем и отображаем версию
            LoadVersion();
            
            LoadConfig();
            
            // Подписываемся на изменение языка
            _localization.LanguageChanged += Localization_LanguageChanged;
            
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

            // Проверяем обновления при запуске (асинхронно, не блокируя UI)
            CheckForUpdatesAsync();
        }

        private void ScrollTimer_Tick(object sender, EventArgs e)
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

        private System.Windows.Controls.ScrollViewer GetScrollViewer(System.Windows.Controls.ListBox listBox)
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
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                VortexPathTextBox.Text = dialog.SelectedPath;
                _logger.LogInfo($"Vortex path selected: {dialog.SelectedPath}");
            }
        }

        private void BrowseTargetPath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Select ~mods folder";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TargetPathTextBox.Text = dialog.SelectedPath;
                _logger.LogInfo($"Target path selected: {dialog.SelectedPath}");
            }
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
                var mods = _modManagerService.LoadModsFromVortexPath(VortexPathTextBox.Text);
                _mods.Clear();

                // Загружаем сохраненный порядок модов
                var savedOrder = _configService.LoadModsOrder();
                var orderByName = savedOrder.Mods.ToDictionary(m => m.Name, m => m);

                // Находим максимальный порядок из сохраненных модов
                int maxOrder = savedOrder.Mods.Any() ? savedOrder.Mods.Max(m => m.Order) : -1;

                // Устанавливаем порядок из сохраненного конфига или добавляем в конец
                foreach (var mod in mods)
                {
                    if (orderByName.TryGetValue(mod.Name, out var orderItem))
                    {
                        mod.Order = orderItem.Order;
                        mod.IsEnabled = orderItem.IsEnabled;
                    }
                    else
                    {
                        // Если мод не найден в сохраненном порядке, добавляем в конец
                        mod.Order = ++maxOrder;
                        mod.IsEnabled = true;
                    }
                    _mods.Add(mod);
                }

                // Сортируем по порядку
                var sortedMods = _mods.OrderBy(m => m.Order).ToList();
                _mods.Clear();
                foreach (var mod in sortedMods)
                {
                    _mods.Add(mod);
                }

                UpdateOrders();
                
                // Включаем кнопку Install Mods если моды загружены
                InstallModsButton.IsEnabled = _mods.Count > 0;
                
                UpdateStatus($"Loaded {mods.Count} mods");
                _logger.LogSuccess($"Loaded {mods.Count} mods from path: {VortexPathTextBox.Text}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error loading mods", ex);
                WarningWindow.Show($"{_localization.GetString("ErrorLoadingMods")}: {ex.Message}", _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Загружаем текущий конфиг, чтобы сохранить размер окна
                var pathsConfig = _configService.LoadPathsConfig();
                pathsConfig.VortexPath = VortexPathTextBox.Text;
                pathsConfig.TargetPath = TargetPathTextBox.Text;
                pathsConfig.WindowWidth = Width;
                pathsConfig.WindowHeight = Height;
                _configService.SavePathsConfig(pathsConfig);
                _logger.LogInfo("Paths config saved");

                // Сохраняем порядок модов отдельно
                var modsOrder = _configService.CreateModOrderFromMods(_mods.ToList());
                _configService.SaveModsOrder(modsOrder);
                _logger.LogInfo($"Mods order saved: {modsOrder.Mods.Count} mods");

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

                ModConfig pathsConfig = null;
                ModOrder modsOrder = null;

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
                }

                // Применяем порядок модов, если он загружен
                if (modsOrder != null && modsOrder.Mods.Any())
                {
                    // Применяем порядок к текущим модам
                    ApplyModsOrder(modsOrder);
                    
                    // Включаем/выключаем кнопку Install Mods в зависимости от наличия модов
                    InstallModsButton.IsEnabled = _mods.Count > 0;
                    
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

            var result = WarningWindow.Show(
                _localization.GetString("InstallModsWarning"),
                _localization.GetString("Warning"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // Блокируем элемент управления установки
            var installMenuItem = sender as System.Windows.Controls.MenuItem;
            var installButton = sender as System.Windows.Controls.Button;
            
            if (installMenuItem != null)
            {
                installMenuItem.IsEnabled = false;
                installMenuItem.Header = "Installing...";
            }
            
            if (installButton != null)
            {
                installButton.IsEnabled = false;
                installButton.Content = _localization.GetString("Installing");
            }

            // Показываем прогресс-бар
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            ProgressTextBlock.Text = "";

            try
            {
                var progress = new Progress<InstallProgress>(p =>
                {
                    ProgressBar.Value = p.Percentage;
                    ProgressTextBlock.Text = p.CurrentMod;
                    UpdateStatus($"Installing: {p.CurrentMod} ({p.Installed}/{p.Total})");
                });

                var enabledModsCount = _mods.Count(m => m.IsEnabled);
                _logger.LogInfo($"Starting mods installation. Target: {TargetPathTextBox.Text}, Enabled mods: {enabledModsCount}");
                
                await _modManagerService.InstallModsAsync(_mods.ToList(), TargetPathTextBox.Text, progress);
                
                UpdateStatus($"Installed {enabledModsCount} mods");
                _logger.LogSuccess($"Mods installed successfully. Installed {enabledModsCount} mods to {TargetPathTextBox.Text}");
                WarningWindow.Show(_localization.GetString("ModsInstalledSuccess"), _localization.GetString("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            var window = new DlcModLoaderWindow
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
                    e.Accepted = true;
                }
                else
                {
                    string searchLower = searchText.ToLowerInvariant();
                    e.Accepted = mod.Name.ToLowerInvariant().Contains(searchLower) ||
                                 mod.TargetFolderName.ToLowerInvariant().Contains(searchLower);
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

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (ModsListBox.SelectedItem is ModInfo selectedMod)
            {
                var index = _mods.IndexOf(selectedMod);
                if (index > 0)
                {
                    _mods.Move(index, index - 1);
                    UpdateOrders();
                    ModsListBox.SelectedIndex = index - 1;
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
                    ModsListBox.SelectedIndex = index + 1;
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

            // Загружаем порядок модов
            var modsOrder = _configService.LoadModsOrder();
            if (modsOrder.Mods.Any())
            {
                ApplyModsOrder(modsOrder);
                _logger.LogDebug($"Applied saved mods order on startup: {modsOrder.Mods.Count} mods");
            }
        }

        private void ApplyModsOrder(ModOrder order)
        {
            // Создаем словарь для быстрого поиска модов по имени
            var modsByName = _mods.ToDictionary(m => m.Name, m => m);

            // Применяем порядок и статус включения из конфига
            foreach (var orderItem in order.Mods.OrderBy(m => m.Order))
            {
                if (modsByName.TryGetValue(orderItem.Name, out var mod))
                {
                    mod.Order = orderItem.Order;
                    mod.IsEnabled = orderItem.IsEnabled;
                }
            }

            // Сортируем список по порядку
            var sortedMods = _mods.OrderBy(m => m.Order).ToList();
                _mods.Clear();
            foreach (var mod in sortedMods)
                {
                    _mods.Add(mod);
                }

            // Обновляем порядки после сортировки
            UpdateOrders();
            
            // Включаем/выключаем кнопку Install Mods в зависимости от наличия модов
            InstallModsButton.IsEnabled = _mods.Count > 0;
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

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var modsOrder = _configService.CreateModOrderFromMods(_mods.ToList());
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

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var modsOrder = _configService.LoadModsOrderFromFile(dialog.FileName);
                    
                    if (modsOrder.Mods.Any())
                    {
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

        private void LoadCustomLocalization_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var dialog = new System.Windows.Forms.OpenFileDialog())
                {
                    dialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                    dialog.Title = _localization.GetString("SelectJsonFile");
                    
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        _localization.LoadFromExternalFile(dialog.FileName);
                        UpdateLocalization(); // Обновляем UI с новой локализацией
                        UpdateStatus($"Loaded custom localization from: {System.IO.Path.GetFileName(dialog.FileName)}");
                        _logger.LogSuccess($"Loaded custom localization from: {dialog.FileName}");
                        WarningWindow.Show(
                            _localization.GetString("ConfigSavedSuccess"),
                            _localization.GetString("Success"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error loading custom localization", ex);
                WarningWindow.Show(
                    $"Error loading localization: {ex.Message}",
                    _localization.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ResetLocalization_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _localization.ResetToEmbedded();
                UpdateLocalization(); // Обновляем UI с локализацией по умолчанию
                UpdateStatus("Reset to default localization");
                _logger.LogSuccess("Reset to default localization");
                WarningWindow.Show(
                    _localization.GetString("ConfigSavedSuccess"),
                    _localization.GetString("Success"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error resetting localization", ex);
                WarningWindow.Show(
                    $"Error resetting localization: {ex.Message}",
                    _localization.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private void Advanced_Click(object sender, RoutedEventArgs e)
        {
            _logger.LogDebug("Advanced options window opened");
            var optionsWindow = new AdditionalOptionsWindow
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
                List<string> files = null;

                if (fileExtension == ".txt")
                {
                    _logger.LogInfo($"Reading TXT file: {jsonFilePath}");
                    // Читаем TXT файл построчно
                    var lines = System.IO.File.ReadAllLines(jsonFilePath);
                    files = new List<string>();

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        // Пропускаем пустые строки
                        if (string.IsNullOrEmpty(trimmed))
                            continue;

                        // Убираем кавычки и запятые, если есть (для формата как в 02.11.25 shining.txt)
                        trimmed = trimmed.Trim('"', ',', ' ');
                        
                        // Если строка содержит путь (с обратным слешем), добавляем её
                        if (trimmed.Contains("\\") || trimmed.Contains("/"))
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
                    if (string.IsNullOrEmpty(file) || (!file.Contains("\\") && !file.Contains("/")))
                        continue;

                    var separator = file.Contains("\\") ? '\\' : '/';
                    var parts = file.Split(separator);
                    if (parts.Length > 0)
                    {
                        var folderName = parts[0].Trim();
                        
                        // Проверяем формат с префиксом типа AAA-, AAB-, AAH- и т.д.
                        // Ищем первый дефис, после которого начинается название мода
                        var firstDashIndex = folderName.IndexOf('-');
                        if (firstDashIndex > 0 && firstDashIndex < folderName.Length - 1)
                        {
                            var prefix = folderName.Substring(0, firstDashIndex);
                            
                            // Проверяем, что префикс состоит только из букв (например, AAA, AAB, AAH)
                            if (prefix.Length >= 2 && prefix.All(c => System.Char.IsLetter(c)))
                            {
                                // Извлекаем имя мода без префикса (все после первого дефиса)
                                var modName = folderName.Substring(firstDashIndex + 1);
                                
                                // Пропускаем пустые имена
                                if (string.IsNullOrWhiteSpace(modName))
                                    continue;
                                
                                // Используем HashSet для уникальности (без учета регистра)
                                var modNameKey = modName.ToLowerInvariant();
                                if (!seenMods.Contains(modNameKey))
                                {
                                    seenMods.Add(modNameKey);
                                    modOrderList.Add(modName); // Сохраняем оригинальное имя с регистром
                                    _logger.LogDebug($"Found mod in order [{modOrderList.Count - 1}]: '{modName}' (from '{folderName}')");
                                }
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
                string GetBaseModName(string fullName)
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
                    if (!modsByBaseName.ContainsKey(baseName))
                    {
                        modsByBaseName[baseName] = new List<ModInfo>();
                    }
                    modsByBaseName[baseName].Add(mod);
                }

                // Обновляем порядок модов согласно порядку из файла
                int newOrder = 0;
                var matchedMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var modNameFromFile in modOrderList)
                {
                    var modNameLower = modNameFromFile.ToLowerInvariant();
                    var baseNameFromFile = GetBaseModName(modNameFromFile).ToLowerInvariant();
                    
                    ModInfo matchedMod = null;
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
                        ModInfo bestMatch = null;
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
                var fileType = System.IO.Path.GetExtension(jsonFilePath).ToLower() == ".txt" ? "TXT" : "JSON";
                
                // Включаем/выключаем кнопку Install Mods в зависимости от наличия модов
                InstallModsButton.IsEnabled = _mods.Count > 0;
                
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
            var listBox = sender as System.Windows.Controls.ListBox;
            if (listBox == null) return;

            var item = System.Windows.Input.Mouse.DirectlyOver;
            var checkBox = FindParent<System.Windows.Controls.CheckBox>(item as System.Windows.DependencyObject);
            
            // Если клик по чекбоксу, не начинаем drag
            if (checkBox != null)
            {
                return;
            }

            _dragStartPoint = e.GetPosition(null);
            var hitItem = listBox.GetItemAt(e.GetPosition(listBox));
            if (hitItem is ModInfo mod)
            {
                _draggedMod = mod;
                listBox.SelectedItem = mod;
                
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
                    var listBox = sender as System.Windows.Controls.ListBox;
                    if (listBox != null)
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
                            var container = listBox.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.Controls.ListBoxItem;
                            if (container != null)
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
                    if (adornerLayer != null)
                    {
                        adornerLayer.Remove(_insertionLineAdorner);
                    }
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

        private void UpdateInsertionIndicator(System.Windows.Controls.ListBox listBox, System.Windows.DragEventArgs e)
        {
            if (_insertionLineAdorner == null || listBox == null) return;
            
            try
            {
                var point = e.GetPosition(listBox);
                var item = listBox.GetItemAt(point);
                
                if (item is ModInfo targetMod && targetMod != _draggedMod)
                {
                    // Находим ListBoxItem для целевого элемента
                    var targetListItem = listBox.ItemContainerGenerator.ContainerFromItem(targetMod) as System.Windows.Controls.ListBoxItem;
                    if (targetListItem != null)
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
            
            var listBox = sender as System.Windows.Controls.ListBox;
            if (listBox == null) return;

            ModInfo draggedMod = null;
            
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
                int newIndex = _mods.IndexOf(targetMod);

                if (oldIndex != newIndex)
                {
                    _mods.RemoveAt(oldIndex);
                    _mods.Insert(newIndex, draggedMod);
                    UpdateOrders();
                    ModsListBox.SelectedItem = draggedMod;
                    _logger.LogDebug($"Moved mod '{draggedMod.Name}' via drag-drop (from {oldIndex} to {newIndex})");
                }
            }

            _draggedMod = null;
            _draggedListItem = null;
            _scrollTimer.Stop(); // Останавливаем автоскролл при завершении перетаскивания
            e.Handled = true;
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            // Предотвращаем выделение элемента ListBox при клике на чекбокс
            // Сбрасываем выделение, если оно есть
            if (ModsListBox.SelectedItem != null)
            {
                var selectedItem = ModsListBox.SelectedItem;
                ModsListBox.SelectedItem = null;
                // Восстанавливаем выделение после небольшой задержки, если нужно
                // Но обычно чекбокс работает через привязку данных, так что выделение не нужно
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
                    var displayName = languageNames.ContainsKey(langCode) 
                        ? $"{languageNames[langCode]} ({langCode.ToUpper()})" 
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

        private void Localization_LanguageChanged(object sender, EventArgs e)
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
                SettingsMenuItem.Header = "_" + _localization.GetString("Settings");
                LoadCustomLocalizationMenuItem.Header = "_" + _localization.GetString("LoadCustomLocalization");
                ResetLocalizationMenuItem.Header = "_" + _localization.GetString("ResetLocalization");
                InstallModsMenuItem.Header = _localization.GetString("InstallMods");
                ClearModsMenuItem.Header = "_" + _localization.GetString("ClearMods");
                
                // Mods GroupBox
                ModsGroupBox.Header = _localization.GetString("Mods");
                
                // Move buttons
                MoveUpButton.Content = _localization.GetString("MoveUp");
                MoveDownButton.Content = _localization.GetString("MoveDown");
                
                // Install Mods button
                InstallModsButton.Content = _localization.GetString("InstallMods");
                
                // Column headers
                var orderHeader = FindName("OrderHeader") as System.Windows.Controls.TextBlock;
                var nameHeader = FindName("NameHeader") as System.Windows.Controls.TextBlock;
                var targetFolderHeader = FindName("TargetFolderHeader") as System.Windows.Controls.TextBlock;
                
                if (orderHeader != null)
                    orderHeader.Text = _localization.GetString("OrderHeader");
                if (nameHeader != null)
                    nameHeader.Text = _localization.GetString("NameHeader");
                if (targetFolderHeader != null)
                    targetFolderHeader.Text = _localization.GetString("TargetFolderHeader");
                
                // Status
                if (StatusTextBlock.Text == "Ready" || StatusTextBlock.Text == "Готов")
                {
                    StatusTextBlock.Text = _localization.GetString("Status");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating localization: {ex.Message}");
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
        private T FindParent<T>(System.Windows.DependencyObject child) where T : System.Windows.DependencyObject
        {
            System.Windows.DependencyObject parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;

            T parent = parentObject as T;
            if (parent != null)
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
public static class ListBoxExtensions
{
    public static object GetItemAt(this System.Windows.Controls.ListBox listBox, System.Windows.Point point)
    {
        var element = listBox.InputHitTest(point) as System.Windows.DependencyObject;
        while (element != null)
        {
            if (element is System.Windows.Controls.ListBoxItem)
            {
                return ((System.Windows.Controls.ListBoxItem)element).Content;
            }
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return null;
    }
}

// Adorner для отображения линии вставки при перетаскивании
public class InsertionLineAdorner : Adorner
{
    private double _insertionY;
    
    public InsertionLineAdorner(UIElement adornedElement) 
        : base(adornedElement)
    {
        IsHitTestVisible = false; // Adorner не должен перехватывать события мыши
        _insertionY = 0;
    }
    
    public void UpdatePosition(double y)
    {
        _insertionY = y;
        InvalidateVisual();
    }
    
    protected override Size MeasureOverride(Size constraint)
    {
        return AdornedElement.RenderSize;
    }
    
    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }
    
    protected override void OnRender(DrawingContext drawingContext)
    {
        var listBox = AdornedElement as System.Windows.Controls.ListBox;
        if (listBox == null) return;
        
        var width = listBox.ActualWidth;
        
        // Рисуем горизонтальную линию вставки
        var lineY = Math.Max(0, Math.Min(_insertionY, listBox.ActualHeight));
        
        // Толстая синяя линия
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(255, 0, 122, 204)), 3);
        drawingContext.DrawLine(pen, new Point(0, lineY), new Point(width, lineY));
        
        // Добавляем небольшой треугольник-индикатор слева
        var triangleSize = 8.0;
        var pathGeometry = new PathGeometry();
        var figure = new PathFigure();
        figure.StartPoint = new Point(0, lineY - triangleSize);
        figure.Segments.Add(new LineSegment(new Point(triangleSize, lineY), true));
        figure.Segments.Add(new LineSegment(new Point(0, lineY + triangleSize), true));
        figure.IsClosed = true;
        pathGeometry.Figures.Add(figure);
        
        drawingContext.DrawGeometry(
            new SolidColorBrush(Color.FromArgb(255, 0, 122, 204)), 
            null, 
            pathGeometry);
    }
}

