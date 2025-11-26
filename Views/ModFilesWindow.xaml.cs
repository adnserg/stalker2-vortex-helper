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
        private readonly ModInfo _mod;
        private readonly ObservableCollection<ModFileItem> _files;
        private readonly LocalizationService _localization;

        public ModFilesWindow(ModInfo mod)
        {
            InitializeComponent();
            _mod = mod;
            _localization = LocalizationService.Instance;
            _files = new ObservableCollection<ModFileItem>();
            FilesItemsControl.ItemsSource = _files;

            LoadFiles();
            UpdateLocalization();
        }

        private void UpdateLocalization()
        {
            if (TitleTextBlock != null)
                TitleTextBlock.Text = $"{_localization.GetString("ModFiles")}: {_mod.Name}";
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

            if (string.IsNullOrEmpty(_mod.SourcePath) || !Directory.Exists(_mod.SourcePath))
            {
                return;
            }

            // Рекурсивно собираем все файлы из папки мода
            CollectFiles(_mod.SourcePath, _mod.SourcePath);
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
                    var isEnabled = _mod.IsFileEnabled(relativePath);
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
                _mod.SetFileEnabled(fileItem.Path, true);
                fileItem.IsEnabled = true;
            }
        }

        private void FileCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ModFileItem fileItem)
            {
                _mod.SetFileEnabled(fileItem.Path, false);
                fileItem.IsEnabled = false;
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var fileItem in _files)
            {
                fileItem.IsEnabled = true;
                _mod.SetFileEnabled(fileItem.Path, true);
            }
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var fileItem in _files)
            {
                fileItem.IsEnabled = false;
                _mod.SetFileEnabled(fileItem.Path, false);
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

