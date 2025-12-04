using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Stalker2ModManager.Models;
using Stalker2ModManager.Services;

namespace Stalker2ModManager.Views
{
    public partial class ModFilesWindow : Window
    {
        private readonly ObservableCollection<ModInfo> _versions;
        private ModInfo _selectedMod;
        private readonly ObservableCollection<ModFileItem> _files;
        private readonly LocalizationService _localization;

        public ModFilesWindow(ModInfo selectedMod, System.Collections.Generic.IEnumerable<ModInfo>? allVersions = null)
        {
            InitializeComponent();

            _localization = LocalizationService.Instance;
            _files = new ObservableCollection<ModFileItem>();
            FilesItemsControl.ItemsSource = _files;

            // Если передан список версий, используем его; иначе считаем, что версия одна
            if (allVersions != null)
            {
                _versions = new ObservableCollection<ModInfo>(allVersions);
            }
            else
            {
                _versions = new ObservableCollection<ModInfo> { selectedMod };
            }

            _selectedMod = selectedMod;

            VersionsListBox.ItemsSource = _versions;
            VersionsListBox.SelectedItem = _selectedMod;

            LoadFiles();
            UpdateLocalization();
        }

        private void UpdateLocalization()
        {
            if (TitleTextBlock != null)
                TitleTextBlock.Text = $"{_localization.GetString("ModFiles")}: {_selectedMod.DisplayName}";
            if (OkButton != null)
                OkButton.Content = _localization.GetString("OK");
            if (SelectAllButton != null)
                SelectAllButton.Content = _localization.GetString("SelectAll");
            if (DeselectAllButton != null)
                DeselectAllButton.Content = _localization.GetString("DeselectAll");
        }

        private void LoadFiles()
        {
            _files.Clear();

            if (string.IsNullOrEmpty(_selectedMod.SourcePath) || !Directory.Exists(_selectedMod.SourcePath))
            {
                _selectedMod.HasAnyFiles = false;
                return;
            }

            // Рекурсивно собираем все файлы из папки мода
            CollectFiles(_selectedMod.SourcePath, _selectedMod.SourcePath);

            // Обновляем флаг наличия файлов для версии мода
            _selectedMod.HasAnyFiles = _files.Count > 0;
        }

        private void CollectFiles(string currentPath, string basePath)
        {
            try
            {
                // Добавляем файлы в текущей папке
                var files = Directory.GetFiles(currentPath);
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(basePath, file);
                    var isEnabled = _selectedMod.IsFileEnabled(relativePath);
                    _files.Add(new ModFileItem
                    {
                        Path = relativePath,
                        IsEnabled = isEnabled
                    });
                }

                // Рекурсивно обрабатываем подпапки
                var dirs = Directory.GetDirectories(currentPath);
                foreach (var dir in dirs)
                {
                    CollectFiles(dir, basePath);
                }
            }
            catch (Exception ex)
            {
                Services.Logger.Instance.LogError($"Error loading files from {currentPath}: {ex.Message}");
            }
        }

        private void FileCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ModFileItem fileItem)
            {
                _selectedMod.SetFileEnabled(fileItem.Path, true);
                fileItem.IsEnabled = true;

                // Если включен хотя бы один файл, соответствующая версия мода должна быть включена
                _selectedMod.IsEnabled = true;
            }
        }

        private void FileCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ModFileItem fileItem)
            {
                _selectedMod.SetFileEnabled(fileItem.Path, false);
                fileItem.IsEnabled = false;

                // 4) Если ни один файл не включен, версия мода автоматически выключается
                bool anyEnabled = _files.Any(f => f.IsEnabled);
                _selectedMod.IsEnabled = anyEnabled;
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var fileItem in _files)
            {
                fileItem.IsEnabled = true;
                _selectedMod.SetFileEnabled(fileItem.Path, true);
            }
            // 5) При включении версии через "выбрать все" оставляем мод включенным
            _selectedMod.IsEnabled = true;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var fileItem in _files)
            {
                fileItem.IsEnabled = false;
                _selectedMod.SetFileEnabled(fileItem.Path, false);
            }
            // 4) Если все файлы отключены через "снять выбор", выключаем версию мода
            _selectedMod.IsEnabled = false;
        }

        // 3) При клике по версии в списке показываем её файлы внизу
        private void VersionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionsListBox.SelectedItem is ModInfo newMod && newMod != _selectedMod)
            {
                _selectedMod = newMod;
                // Обновляем заголовок и список файлов для новой версии
                UpdateLocalization();
                LoadFiles();
            }
        }

        /// <summary>
        /// Включает или выключает все файлы для указанной версии мода.
        /// Используется при клике по чекбоксу версии.
        /// </summary>
        private void SetAllFilesEnabledForMod(ModInfo mod, bool isEnabled)
        {
            if (string.IsNullOrEmpty(mod.SourcePath) || !Directory.Exists(mod.SourcePath))
            {
                return;
            }

            try
            {
                var basePath = mod.SourcePath;
                var files = Directory.GetFiles(basePath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(basePath, file);
                    mod.SetFileEnabled(relativePath, isEnabled);
                }
            }
            catch (Exception ex)
            {
                Services.Logger.Instance.LogError($"Error setting all files {(isEnabled ? "enabled" : "disabled")} for mod '{mod.Name}': {ex.Message}");
            }
        }

        // 2) Чекбокс версии: при включении/выключении версии включаем/выключаем все её файлы
        private void VersionCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ModInfo mod)
            {
                bool isChecked = checkBox.IsChecked == true;

                SetAllFilesEnabledForMod(mod, isChecked);

                // Если текущая выбранная версия — та же, обновляем список файлов
                if (mod == _selectedMod)
                {
                    LoadFiles();
                }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                DragMove();
            }
        }
    }

    public class ModFileItem : System.ComponentModel.INotifyPropertyChanged
    {
        private string _path = string.Empty;
        private bool _isEnabled = true;

        public string Path
        {
            get => _path;
            set
            {
                _path = value;
                OnPropertyChanged(nameof(Path));
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}

