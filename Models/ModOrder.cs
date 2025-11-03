using System.Collections.Generic;

namespace Stalker2ModManager.Models
{
    public class ModOrder
    {
        public List<ModOrderItem> Mods { get; set; } = new List<ModOrderItem>();
    }

    public class ModOrderItem
    {
        public string Name { get; set; } = string.Empty;
        public int Order { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}

