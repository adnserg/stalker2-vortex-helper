using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using Stalker2ModManager.Models;
using Stalker2ModManager.Services;

namespace Stalker2ModManager
{
    public partial class MainWindow : Window
    {
        private readonly ModManagerService _modManagerService;
        private readonly ConfigService _configService;
        private ObservableCollection<ModInfo> _mods;
        private ModInfo _draggedMod;
        private Point _dragStartPoint;

        public MainWindow()
        {
            InitializeComponent();
            _modManagerService = new ModManagerService();
            _configService = new ConfigService();
            _mods = new ObservableCollection<ModInfo>();
            ModsListBox.ItemsSource = _mods;

            LoadConfig();
        }

        private void BrowseVortexPath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Select Vortex mods folder";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                VortexPathTextBox.Text = dialog.SelectedPath;
            }
        }

        private void BrowseTargetPath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Select ~mods folder";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TargetPathTextBox.Text = dialog.SelectedPath;
            }
        }

        private void LoadMods_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(VortexPathTextBox.Text))
            {
                System.Windows.MessageBox.Show("Please select Vortex path first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                UpdateStatus($"Loaded {mods.Count} mods");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading mods: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Сохраняем пути отдельно
                var pathsConfig = new ModConfig
                {
                    VortexPath = VortexPathTextBox.Text,
                    TargetPath = TargetPathTextBox.Text
                };
                _configService.SavePathsConfig(pathsConfig);

                // Сохраняем порядок модов отдельно
                var modsOrder = _configService.CreateModOrderFromMods(_mods.ToList());
                _configService.SaveModsOrder(modsOrder);

                UpdateStatus("Config saved");
                System.Windows.MessageBox.Show("Config saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error saving config: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Проверяем, есть ли файлы конфигов
                bool hasPathsConfig = System.IO.File.Exists("paths_config.json");
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
                    UpdateStatus($"Loaded mods order with {modsOrder.Mods.Count} mods");
                }
                else if (pathsConfig != null)
                {
                    UpdateStatus("Loaded paths config");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading config: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InstallMods_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TargetPathTextBox.Text))
            {
                System.Windows.MessageBox.Show("Please select target path first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = System.Windows.MessageBox.Show(
                "This will DELETE all files in ~mods folder and install only enabled mods. Continue?",
                "Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _modManagerService.InstallMods(_mods.ToList(), TargetPathTextBox.Text);
                UpdateStatus($"Installed {_mods.Count(m => m.IsEnabled)} mods");
                System.Windows.MessageBox.Show("Mods installed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error installing mods: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    ModsListBox.SelectedIndex = index - 1;
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
                }
            }
        }

        private void UpdateOrders()
        {
            for (int i = 0; i < _mods.Count; i++)
            {
                _mods[i].Order = i;
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

            // Загружаем порядок модов
            var modsOrder = _configService.LoadModsOrder();
            if (modsOrder.Mods.Any())
            {
                ApplyModsOrder(modsOrder);
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
        }

        private void ExportOrder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_mods.Any())
                {
                    System.Windows.MessageBox.Show("No mods loaded. Load mods first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    System.Windows.MessageBox.Show("Mods order exported successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error exporting order: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportOrder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_mods.Any())
                {
                    System.Windows.MessageBox.Show("No mods loaded. Load mods first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        System.Windows.MessageBox.Show($"Mods order imported successfully.\nApplied order for {modsOrder.Mods.Count} mods.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("The imported file does not contain any mods order.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error importing order: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
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
                        var data = new System.Windows.DataObject(typeof(ModInfo), _draggedMod);
                        System.Windows.DragDrop.DoDragDrop(listBox, data, System.Windows.DragDropEffects.Move);
                        _draggedMod = null;
                    }
                }
            }
        }

        private void ModsListBox_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(ModInfo)))
            {
                e.Effects = System.Windows.DragDropEffects.Move;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void ModsListBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
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
                }
            }

            _draggedMod = null;
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
            Close();
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

