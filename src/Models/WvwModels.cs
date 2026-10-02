using System;
using System.Collections.Generic;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;

namespace WvWarlord.Models
{
    /// <summary>Live, per-tick state for a single objective. Keyed by the API's string id (e.g. "38-6").</summary>
    public class LiveObjectiveState
    {
        public string Id { get; set; }
        public string Owner { get; set; } = "Neutral";
        public int Tier { get; set; } = 0;
        public int YaksDelivered { get; set; } = 0;
        public bool IsContested { get; set; } = false;
        public DateTime LastFlipped { get; set; } = DateTime.MinValue;
        public string ClaimedByGuildId { get; set; }
        public List<int> GuildUpgrades { get; set; } = new List<int>();
        public bool HasWaypoint { get; set; } = false;
    }

    /// <summary>Static metadata for one objective, sourced from /v2/wvw/objectives.</summary>
    public class WvwObjectiveInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ChatLink { get; set; }
        public WvwObjectiveType Type { get; set; }
        public WvwMapType MapType { get; set; }
        public int MapId { get; set; }
        public Vector2 Coord { get; set; }
        
        /// Unified localized map-center coordinate in raw Game Units.
        public Vector2 LocalCoord { get; set; }
        public bool IsStaticSpawn => Type == WvwObjectiveType.Spawn;

        public string SpawnColor
        {
            get
            {
                switch (MapType)
                {
                    case WvwMapType.GreenHome: return "Green";
                    case WvwMapType.BlueHome: return "Blue";
                    case WvwMapType.RedHome: return "Red";
                }

                if (Type == WvwObjectiveType.Spawn && !string.IsNullOrEmpty(Name))
                {
                    if (Name.IndexOf("Green", StringComparison.OrdinalIgnoreCase) >= 0) return "Green";
                    if (Name.IndexOf("Blue", StringComparison.OrdinalIgnoreCase) >= 0) return "Blue";
                    if (Name.IndexOf("Red", StringComparison.OrdinalIgnoreCase) >= 0) return "Red";
                }

                return "Neutral";
            }
        }

        public int PointsValue
        {
            get
            {
                switch (Type)
                {
                    case WvwObjectiveType.Castle: return 35;
                    case WvwObjectiveType.Keep: return 25;
                    case WvwObjectiveType.Tower: return 10;
                    case WvwObjectiveType.Camp: return 5;
                    default: return 0;
                }
            }
        }
    }

    public class MyGuildData
    {
        public string Id { get; set; }
        public string Tag { get; set; }
        public string Name { get; set; }
        public int EmblemBackgroundId { get; set; }
        public List<int> EmblemBackgroundColors { get; set; } = new List<int>();
        public int EmblemForegroundId { get; set; }
        public List<int> EmblemForegroundColors { get; set; } = new List<int>();
        public List<string> EmblemFlags { get; set; } = new List<string>();
    }

    public class GuildUpgradeData
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string IconUrl { get; set; }
        public string Type { get; set; }
    }

    public class WvwUpgradeData
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<WvwUpgradeTierData> Tiers { get; set; } = new List<WvwUpgradeTierData>();
    }

    public class WvwUpgradeTierData
    {
        public string Name { get; set; }
        public int YaksRequired { get; set; }
        public List<WvwSubUpgradeItem> SubUpgrades { get; set; } = new List<WvwSubUpgradeItem>();
    }

    public class WvwSubUpgradeItem
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string IconUrl { get; set; }
    }

    /// <summary>Static tier-threshold + map-label lookups shared across UI and data layers.</summary>
    public static class WvwStaticData
    {
        private static readonly Dictionary<WvwObjectiveType, int[]> _tierThresholds = new Dictionary<WvwObjectiveType, int[]>
        {
            { WvwObjectiveType.Camp,   new[] { 15, 35, 60 } },
            { WvwObjectiveType.Tower,  new[] { 15, 35, 70 } },
            { WvwObjectiveType.Keep,   new[] { 20, 50, 100 } },
            { WvwObjectiveType.Castle, new[] { 40, 100, 190 } },
        };

        public static readonly Dictionary<int, string> MapLabels = new Dictionary<int, string>
        {
            { 38, "EBG" },
            { 1099, "Red Map" },
            { 95, "Green Map" },
            { 96, "Blue Map" }
        };

        public static int GetTier(WvwObjectiveType type, int yaksDelivered)
        {
            if (!_tierThresholds.TryGetValue(type, out var t)) return 0;
            int tier = 0;
            for (int i = 0; i < t.Length; i++) if (yaksDelivered >= t[i]) tier = i + 1;
            return tier;
        }

        public static int GetNextTierThreshold(WvwObjectiveType type, int yaksDelivered)
        {
            if (!_tierThresholds.TryGetValue(type, out var t)) return yaksDelivered;
            foreach (var threshold in t) if (yaksDelivered < threshold) return threshold;
            return t[t.Length - 1];
        }

        public static int GetUpgradePathId(WvwObjectiveType type)
        {
            switch (type)
            {
                case WvwObjectiveType.Camp: return 1;
                case WvwObjectiveType.Tower: return 2;
                case WvwObjectiveType.Keep: return 3;
                case WvwObjectiveType.Castle: return 4;
                default: return 1;
            }
        }

        public static string GetHomeKeepName(int mapId, string color)
        {
            if (mapId == 38)
            {
                switch (color)
                {
                    case "Red": return "Overlook Keep";
                    case "Blue": return "Valley Keep";
                    case "Green": return "Lowlands Keep";
                }
            }
            else if (mapId == 95 || mapId == 96)
            {
                return "Garrison Keep";
            }
            else if (mapId == 1099)
            {
                return "Fire Citadel"; // best guess -- verify against client
            }
            return null;
        }

        public static Microsoft.Xna.Framework.Color GetFactionColor(string mapOrColorName)
        {
            if (mapOrColorName.IndexOf("red", StringComparison.OrdinalIgnoreCase) >= 0) return Microsoft.Xna.Framework.Color.Red;
            if (mapOrColorName.IndexOf("blue", StringComparison.OrdinalIgnoreCase) >= 0) return new Microsoft.Xna.Framework.Color(30, 144, 255);
            if (mapOrColorName.IndexOf("green", StringComparison.OrdinalIgnoreCase) >= 0) return Microsoft.Xna.Framework.Color.LimeGreen;
            if (mapOrColorName.IndexOf("ebg", StringComparison.OrdinalIgnoreCase) >= 0) return Microsoft.Xna.Framework.Color.Gold;
            return Microsoft.Xna.Framework.Color.DarkGray;
        }
    }
}
