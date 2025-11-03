using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Stalker2ModManager.Models
{
    public class ModInfo : INotifyPropertyChanged
    {
        private string _sourcePath = string.Empty;
        private string _name = string.Empty;
        private bool _isEnabled = true;
        private int _order;

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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

