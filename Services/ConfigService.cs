using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Stalker2ModManager.Models;

namespace Stalker2ModManager.Services
{
    public class ConfigService
    {
        private readonly string _pathsConfigPath = "paths_config.json";
        private readonly string _modsOrderPath = "mods_order.json";

        // Загрузка и сохранение путей (Vortex и Target)
        public ModConfig LoadPathsConfig()
        {
            if (!File.Exists(_pathsConfigPath))
            {
                return new ModConfig();
            }

            try
            {
                var json = File.ReadAllText(_pathsConfigPath);
                return JsonConvert.DeserializeObject<ModConfig>(json) ?? new ModConfig();
            }
            catch
            {
                return new ModConfig();
            }
        }

        public void SavePathsConfig(ModConfig config)
        {
            try
            {
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_pathsConfigPath, json);
            }
            catch
            {
                // Ошибка сохранения
            }
        }

        // Загрузка и сохранение порядка модов
        public ModOrder LoadModsOrder()
        {
            if (!File.Exists(_modsOrderPath))
            {
                return new ModOrder();
            }

            try
            {
                var json = File.ReadAllText(_modsOrderPath);
                return JsonConvert.DeserializeObject<ModOrder>(json) ?? new ModOrder();
            }
            catch
            {
                return new ModOrder();
            }
        }

        public void SaveModsOrder(ModOrder order)
        {
            try
            {
                var json = JsonConvert.SerializeObject(order, Formatting.Indented);
                File.WriteAllText(_modsOrderPath, json);
            }
            catch
            {
                // Ошибка сохранения
            }
        }

        public void SaveModsOrderToFile(ModOrder order, string filePath)
        {
            try
            {
                var json = JsonConvert.SerializeObject(order, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch
            {
                throw;
            }
        }

        public ModOrder LoadModsOrderFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new ModOrder();
            }

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<ModOrder>(json) ?? new ModOrder();
            }
            catch
            {
                throw;
            }
        }

        // Вспомогательные методы для работы с ModInfo
        public ModOrder CreateModOrderFromMods(System.Collections.Generic.List<ModInfo> mods)
        {
            var order = new ModOrder();
            foreach (var mod in mods.OrderBy(m => m.Order))
            {
                order.Mods.Add(new ModOrderItem
                {
                    Name = mod.Name,
                    Order = mod.Order,
                    IsEnabled = mod.IsEnabled
                });
            }
            return order;
        }

        // Для обратной совместимости - загрузка старого формата конфига
        public bool TryLoadLegacyConfig(out ModConfig config, out ModOrder order)
        {
            config = new ModConfig();
            order = new ModOrder();
            
            const string legacyConfigPath = "mods_config.json";
            if (!File.Exists(legacyConfigPath))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(legacyConfigPath);
                var legacyConfig = JsonConvert.DeserializeObject<LegacyModConfig>(json);
                
                if (legacyConfig != null)
                {
                    config.VortexPath = legacyConfig.VortexPath ?? string.Empty;
                    config.TargetPath = legacyConfig.TargetPath ?? string.Empty;
                    
                    if (legacyConfig.Mods != null)
                    {
                        foreach (var mod in legacyConfig.Mods.OrderBy(m => m.Order))
                        {
                            order.Mods.Add(new ModOrderItem
                            {
                                Name = mod.Name ?? string.Empty,
                                Order = mod.Order,
                                IsEnabled = mod.IsEnabled
                            });
                        }
                    }
                    return true;
                }
            }
            catch
            {
                // Игнорируем ошибки
            }
            
            return false;
        }

        private class LegacyModConfig
        {
            public string VortexPath { get; set; }
            public string TargetPath { get; set; }
            public System.Collections.Generic.List<ModInfo> Mods { get; set; }
        }
    }
}

