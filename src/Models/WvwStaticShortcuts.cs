using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace WvWarlord.Models
{
    /// <summary>
    /// One hardcoded shortcut entry, positioned on a fixed grid: ColumnIndex
    /// 0=EBG, 1=Red home BL, 2=Green home BL, 3=Blue home BL (same order for
    /// every color), and IsWaypointSlot picks the keep slot (false) or the
    /// waypoint slot (true) within that column. A column only gets a keep
    /// slot for EBG and the player's OWN home map -- the other two enemy
    /// home maps only get a waypoint (border-crossing) slot, and that keep
    /// slot is simply never populated. Rendering must still reserve that
    /// slot's grid position rather than compacting around it, so the tray
    /// lines up identically no matter which color is active.
    /// </summary>
    public class StaticShortcut
    {
        public string ObjectiveId { get; set; }
        public string Name { get; set; }
        public string ChatLink { get; set; }
        public bool IsKeepIcon { get; set; } // true = keep-type icon, false = waypoint icon
        public Color TintColor { get; set; }
        public int ColumnIndex { get; set; } // 0=EBG, 1=RedHome, 2=GreenHome, 3=BlueHome
        public bool IsWaypointSlot { get; set; } // false = keep slot (left), true = waypoint slot (right)
    }

    /// <summary>The exact per-team hardcoded shortcut set from the original macro tray, ported verbatim.</summary>
    public static class WvwStaticShortcuts
    {
        private static readonly Color Blue = new Color(30, 144, 255);

        public static readonly Dictionary<string, List<StaticShortcut>> ByColor = new Dictionary<string, List<StaticShortcut>>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Blue"] = new List<StaticShortcut>
{
    new StaticShortcut { ObjectiveId = "38-2",     Name = "Valley Keep",   ChatLink = "[&BL8EAAA=]", IsKeepIcon = true,  TintColor = Color.Gold,       ColumnIndex = 0, IsWaypointSlot = false },
    new StaticShortcut { ObjectiveId = "38-130",   Name = "Blue Spawn",    ChatLink = "[&BPoDAAA=]", IsKeepIcon = false, TintColor = Color.Gold,       ColumnIndex = 0, IsWaypointSlot = true },
    new StaticShortcut { ObjectiveId = "1099-107", Name = "Red Border",    ChatLink = "[&BIsIAAA=]", IsKeepIcon = false, TintColor = Color.Red,        ColumnIndex = 1, IsWaypointSlot = true },   // was "Blue Border"
    new StaticShortcut { ObjectiveId = "95-112",   Name = "Green Border",  ChatLink = "[&BNoEAAA=]", IsKeepIcon = false, TintColor = Color.LimeGreen,  ColumnIndex = 2, IsWaypointSlot = true },   // was "Blue Border"
    new StaticShortcut { ObjectiveId = "96-37",    Name = "Garrison Keep", ChatLink = "[&BNUEAAA=]", IsKeepIcon = true,  TintColor = Blue,             ColumnIndex = 3, IsWaypointSlot = false },
    new StaticShortcut { ObjectiveId = "96-111",   Name = "Home Citadel",  ChatLink = "[&BNYEAAA=]", IsKeepIcon = false, TintColor = Blue,             ColumnIndex = 3, IsWaypointSlot = true },
},
            ["Red"] = new List<StaticShortcut>
{
    new StaticShortcut { ObjectiveId = "38-1",     Name = "Overlook Keep", ChatLink = "[&BL4EAAA=]", IsKeepIcon = true,  TintColor = Color.Gold,       ColumnIndex = 0, IsWaypointSlot = false },
    new StaticShortcut { ObjectiveId = "38-124",   Name = "Red Spawn",     ChatLink = "[&BPsDAAA=]", IsKeepIcon = false, TintColor = Color.Gold,       ColumnIndex = 0, IsWaypointSlot = true },
    new StaticShortcut { ObjectiveId = "1099-113", Name = "Stoic Rampart", ChatLink = "[&BP0IAAA=]", IsKeepIcon = true,  TintColor = Color.Red,        ColumnIndex = 1, IsWaypointSlot = false },
    new StaticShortcut { ObjectiveId = "1099-117", Name = "Home Citadel",  ChatLink = "[&BOQIAAA=]", IsKeepIcon = false, TintColor = Color.Red,        ColumnIndex = 1, IsWaypointSlot = true },
    new StaticShortcut { ObjectiveId = "95-112",   Name = "Green Border",  ChatLink = "[&BN4EAAA=]", IsKeepIcon = false, TintColor = Color.LimeGreen,  ColumnIndex = 2, IsWaypointSlot = true },   // was "Red Border"
    new StaticShortcut { ObjectiveId = "96-112",   Name = "Blue Border",   ChatLink = "[&BNQEAAA=]", IsKeepIcon = false, TintColor = Blue,             ColumnIndex = 3, IsWaypointSlot = true },   // was "Red Border"
},
            ["Green"] = new List<StaticShortcut>
{
    new StaticShortcut { ObjectiveId = "38-3",     Name = "Lowlands Keep", ChatLink = "[&BMAEAAA=]", IsKeepIcon = true,  TintColor = Color.Gold,       ColumnIndex = 0, IsWaypointSlot = false },
    new StaticShortcut { ObjectiveId = "38-131",   Name = "Green Spawn",   ChatLink = "[&BPwDAAA=]", IsKeepIcon = false, TintColor = Color.Gold,       ColumnIndex = 0, IsWaypointSlot = true },
    new StaticShortcut { ObjectiveId = "1099-108", Name = "Red Border",    ChatLink = "[&BMcIAAA=]", IsKeepIcon = false, TintColor = Color.Red,        ColumnIndex = 1, IsWaypointSlot = true },   // was "Green Border"
    new StaticShortcut { ObjectiveId = "95-37",    Name = "Garrison Keep", ChatLink = "[&BNsEAAA=]", IsKeepIcon = true,  TintColor = Color.LimeGreen,  ColumnIndex = 2, IsWaypointSlot = false },
    new StaticShortcut { ObjectiveId = "95-111",   Name = "Home Citadel",  ChatLink = "[&BNwEAAA=]", IsKeepIcon = false, TintColor = Color.LimeGreen,  ColumnIndex = 2, IsWaypointSlot = true },
    new StaticShortcut { ObjectiveId = "96-103",   Name = "Blue Border",   ChatLink = "[&BNgEAAA=]", IsKeepIcon = false, TintColor = Blue,             ColumnIndex = 3, IsWaypointSlot = true },   // was "Green Border"
},
        };

        public static List<StaticShortcut> ForColor(string color)
        {
            if (!string.IsNullOrEmpty(color) && ByColor.TryGetValue(color, out var list)) return list;
            return new List<StaticShortcut>();
        }
    }
}
