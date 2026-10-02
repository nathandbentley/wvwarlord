using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Blish_HUD.Modules.Managers;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;
using WvWarlord.Models;

namespace WvWarlord.Api
{
    /// <summary>
    /// Loads and indexes the static WvW objective list and map rects. Runs the
    /// mandated Route1(Gw2ApiManager) -> Route2(direct endpoint) fallback for
    /// every network call.
    /// </summary>
    public static class WvwCatalogService
    {
        public static Dictionary<int, List<WvwObjectiveInfo>> ByMap { get; private set; } = new Dictionary<int, List<WvwObjectiveInfo>>();
        public static Dictionary<string, WvwObjectiveInfo> ById { get; private set; } = new Dictionary<string, WvwObjectiveInfo>();
        public static bool IsLoaded { get; private set; } = false;
        public static DateTime SessionStart { get; private set; } = DateTime.UtcNow;

        private static readonly Dictionary<int, (Microsoft.Xna.Framework.Rectangle mapRect, Microsoft.Xna.Framework.Rectangle continentRect)> _mapRects
            = new Dictionary<int, (Microsoft.Xna.Framework.Rectangle, Microsoft.Xna.Framework.Rectangle)>();

        public static async Task LoadAsync(Gw2ApiManager gw2ApiManager)
        {
            try
            {
                var byMap = new Dictionary<int, List<WvwObjectiveInfo>>();
                var byId = new Dictionary<string, WvwObjectiveInfo>();

                var infos = await ApiFallbackExecutor.ExecuteAsync(
                    "WvwObjectives.All",
                    route1: async () =>
                    {
                        var all = await gw2ApiManager.Gw2ApiClient.V2.Wvw.Objectives.AllAsync();
                        return all.Select(MapObjective).ToList();
                    },
                    route2RawJsonFactory: () => ApiFallbackExecutor.GetRawAsync("wvw/objectives?ids=all"),
                    route2Parser: json => ParseObjectivesJson(json));

                foreach (var info in infos)
                {
                    if (!byMap.ContainsKey(info.MapId)) byMap[info.MapId] = new List<WvwObjectiveInfo>();
                    byMap[info.MapId].Add(info);
                    byId[info.Id] = info;
                }

                ByMap = byMap;
                ById = byId;
                SessionStart = DateTime.UtcNow;
                IsLoaded = true;
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"WvwCatalogService.LoadAsync failed entirely: {ex.Message}");
            }
        }

        private static WvwObjectiveInfo MapObjective(WvwObjective o)
        {
            return new WvwObjectiveInfo
            {
                Id = o.Id,
                Name = o.Name,
                ChatLink = o.ChatLink,
                Type = o.Type,
                MapType = o.MapType ?? WvwMapType.Unknown,
                MapId = o.MapId,
                Coord = o.Coord != null ? new Vector2((float)o.Coord.X, (float)o.Coord.Y) : Vector2.Zero
            };
        }

        private static List<WvwObjectiveInfo> ParseObjectivesJson(string json)
        {
            var result = new List<WvwObjectiveInfo>();
            using (var doc = JsonDocument.Parse(json))
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var info = new WvwObjectiveInfo
                    {
                        Id = el.GetProperty("id").GetString(),
                        Name = el.TryGetProperty("name", out var n) ? n.GetString() : "",
                        ChatLink = el.TryGetProperty("chat_link", out var cl) ? cl.GetString() : "",
                        MapId = el.TryGetProperty("map_id", out var mid) ? mid.GetInt32() : 0,
                    };

                    if (el.TryGetProperty("type", out var typeEl) && Enum.TryParse<WvwObjectiveType>(typeEl.GetString(), true, out var parsedType))
                        info.Type = parsedType;

                    if (el.TryGetProperty("map_type", out var mapTypeEl) && Enum.TryParse<WvwMapType>(mapTypeEl.GetString(), true, out var parsedMapType))
                        info.MapType = parsedMapType;

                    if (el.TryGetProperty("coord", out var coordEl) && coordEl.ValueKind == JsonValueKind.Array && coordEl.GetArrayLength() >= 2)
                    {
                        var arr = coordEl.EnumerateArray().ToArray();
                        info.Coord = new Vector2((float)arr[0].GetDouble(), (float)arr[1].GetDouble());
                    }

                    result.Add(info);
                }
            }
            return result;
        }

        public static async Task<(Microsoft.Xna.Framework.Rectangle mapRect, Microsoft.Xna.Framework.Rectangle continentRect)?> GetMapRectAsync(Gw2ApiManager gw2ApiManager, int mapId)
        {
            if (_mapRects.TryGetValue(mapId, out var cached)) return cached;

            try
            {
                var result = await ApiFallbackExecutor.ExecuteAsync(
                    $"Maps.Get({mapId})",
                    route1: async () =>
                    {
                        var map = await gw2ApiManager.Gw2ApiClient.V2.Maps.GetAsync(mapId);
                        var mapRect = new Microsoft.Xna.Framework.Rectangle(
                            (int)map.MapRect.TopLeft.X, (int)map.MapRect.TopLeft.Y,
                            (int)(map.MapRect.BottomRight.X - map.MapRect.TopLeft.X),
                            (int)(map.MapRect.BottomRight.Y - map.MapRect.TopLeft.Y));
                        var continentRect = new Microsoft.Xna.Framework.Rectangle(
                            (int)map.ContinentRect.TopLeft.X, (int)map.ContinentRect.TopLeft.Y,
                            (int)(map.ContinentRect.BottomRight.X - map.ContinentRect.TopLeft.X),
                            (int)(map.ContinentRect.BottomRight.Y - map.ContinentRect.TopLeft.Y));
                        return (mapRect, continentRect);
                    },
                    route2RawJsonFactory: () => ApiFallbackExecutor.GetRawAsync($"maps/{mapId}"),
                    route2Parser: json =>
                    {
                        using (var doc = JsonDocument.Parse(json))
                        {
                            var root = doc.RootElement;
                            var mr = root.GetProperty("map_rect");
                            var cr = root.GetProperty("continent_rect");
                            var mapRect = RectFromJson(mr);
                            var continentRect = RectFromJson(cr);
                            return (mapRect, continentRect);
                        }
                    });

                _mapRects[mapId] = result;
                return result;
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"GetMapRectAsync({mapId}) failed: {ex.Message}");
                return null;
            }
        }

        private static Microsoft.Xna.Framework.Rectangle RectFromJson(JsonElement rectArray)
        {
            // [[x1,y1],[x2,y2]]
            var points = rectArray.EnumerateArray().ToArray();
            var tl = points[0].EnumerateArray().ToArray();
            var br = points[1].EnumerateArray().ToArray();
            int x1 = (int)tl[0].GetDouble(), y1 = (int)tl[1].GetDouble();
            int x2 = (int)br[0].GetDouble(), y2 = (int)br[1].GetDouble();
            return new Microsoft.Xna.Framework.Rectangle(x1, y1, x2 - x1, y2 - y1);
        }

        public static Vector2 WorldToLocalMapCoord(Vector3 worldPos)
        {
            return new Vector2(worldPos.X, worldPos.Y) * (39.3701f / 24f);
        }

        public static bool TryWorldToMapCoord(Microsoft.Xna.Framework.Rectangle mapRect, Microsoft.Xna.Framework.Rectangle continentRect, Vector3 worldPos, out Vector2 mapCoord)
        {
            try
            {
                var worldMapUnits = new Vector2(worldPos.X, -worldPos.Y) * (39.3701f / 24f);
                float scaleX = (float)continentRect.Width / mapRect.Width;
                float scaleY = (float)continentRect.Height / mapRect.Height;
                mapCoord = new Vector2(
                    continentRect.X + (worldMapUnits.X - mapRect.X) * scaleX,
                    continentRect.Y + (worldMapUnits.Y - mapRect.Y) * scaleY);
                return true;
            }
            catch
            {
                mapCoord = Vector2.Zero;
                return false;
            }
        }
    }

    /// <summary>Loads guild-upgrade (tactics) and structural WvW-upgrade definitions once at startup.</summary>
    public static class WvwUpgradeCatalogService
    {
        public static Dictionary<int, GuildUpgradeData> GuildUpgrades { get; private set; } = new Dictionary<int, GuildUpgradeData>();
        public static Dictionary<int, WvwUpgradeData> WvwUpgrades { get; private set; } = new Dictionary<int, WvwUpgradeData>();
        public static bool IsLoaded { get; private set; } = false;

        /// <summary>
        /// True once the structural "Build Waypoint" upgrade for this
        /// objective type's upgrade path has been unlocked at the given tier.
        /// This — together with static spawn locations (see
        /// WvwObjectiveInfo.IsStaticSpawn) — is the only source of truth for
        /// "does this objective have a usable waypoint right now" that the
        /// Quick Travel / icon-swap logic is allowed to use.
        ///
        /// Guild-upgrade Emergency Waypoint (EWP) is a battle tactic from
        /// /v2/guild/upgrades, not a structural upgrade: the API only tells us
        /// the tactic exists/is claimed, never whether it's currently active in
        /// a fight. It is intentionally excluded here and only ever surfaces in
        /// CardPro's Row 2 tactics grid.
        /// </summary>
        public static bool IsBuildWaypointUnlockedAtTier(int upgradePathId, int currentTier)
        {
            if (!WvwUpgrades.TryGetValue(upgradePathId, out var path)) return false;

            for (int tierIndex = 0; tierIndex < currentTier && tierIndex < path.Tiers.Count; tierIndex++)
            {
                foreach (var sub in path.Tiers[tierIndex].SubUpgrades)
                {
                    if (!string.IsNullOrEmpty(sub.Name) && sub.Name.Equals("Build Waypoint", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        public static async Task LoadAsync(Gw2ApiManager apiManager)
        {
            try
            {
                var guildDict = await ApiFallbackExecutor.ExecuteAsync(
                    "GuildUpgrades.All",
                    route1: async () =>
                    {
                        var all = await apiManager.Gw2ApiClient.V2.Guild.Upgrades.AllAsync();
                        var dict = new Dictionary<int, GuildUpgradeData>();
                        foreach (var gu in all.Where(g => g.Type == GuildUpgradeType.Claimable))
                        {
                            dict[gu.Id] = new GuildUpgradeData { Id = gu.Id, Name = gu.Name, Description = gu.Description, IconUrl = gu.Icon, Type = gu.Type.ToString() };
                        }
                        return dict;
                    },
                    route2RawJsonFactory: () => ApiFallbackExecutor.GetRawAsync("guild/upgrades?ids=all"),
                    route2Parser: ParseGuildUpgradesJson);
                GuildUpgrades = guildDict;

                var wvwDict = await ApiFallbackExecutor.ExecuteAsync(
                    "WvwUpgrades.All",
                    route1: async () =>
                    {
                        var all = await apiManager.Gw2ApiClient.V2.Wvw.Upgrades.AllAsync();
                        var dict = new Dictionary<int, WvwUpgradeData>();
                        foreach (var wu in all)
                        {
                            var upgradeData = new WvwUpgradeData { Id = wu.Id, Name = $"Upgrade Path {wu.Id}" };
                            foreach (var tier in wu.Tiers)
                            {
                                upgradeData.Tiers.Add(new WvwUpgradeTierData
                                {
                                    Name = tier.Name,
                                    YaksRequired = tier.YaksRequired,
                                    SubUpgrades = tier.Upgrades.Select(u => new WvwSubUpgradeItem { Name = u.Name, Description = u.Description, IconUrl = u.Icon }).ToList()
                                });
                            }
                            dict[wu.Id] = upgradeData;
                        }
                        return dict;
                    },
                    route2RawJsonFactory: () => ApiFallbackExecutor.GetRawAsync("wvw/upgrades?ids=all"),
                    route2Parser: ParseWvwUpgradesJson);
                WvwUpgrades = wvwDict;

                IsLoaded = true;
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"WvwUpgradeCatalogService.LoadAsync failed: {ex.Message}");
            }
        }

        private static Dictionary<int, GuildUpgradeData> ParseGuildUpgradesJson(string json)
        {
            var dict = new Dictionary<int, GuildUpgradeData>();
            using (var doc = JsonDocument.Parse(json))
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string type = el.TryGetProperty("type", out var t) ? t.GetString() : "";
                    if (!string.Equals(type, "Claimable", StringComparison.OrdinalIgnoreCase)) continue;

                    int id = el.GetProperty("id").GetInt32();
                    dict[id] = new GuildUpgradeData
                    {
                        Id = id,
                        Name = el.TryGetProperty("name", out var n) ? n.GetString() : "",
                        Description = el.TryGetProperty("description", out var d) ? d.GetString() : "",
                        IconUrl = el.TryGetProperty("icon", out var ic) ? ic.GetString() : "",
                        Type = type
                    };
                }
            }
            return dict;
        }

        private static Dictionary<int, WvwUpgradeData> ParseWvwUpgradesJson(string json)
        {
            var dict = new Dictionary<int, WvwUpgradeData>();
            using (var doc = JsonDocument.Parse(json))
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    int id = el.GetProperty("id").GetInt32();
                    var data = new WvwUpgradeData { Id = id, Name = $"Upgrade Path {id}" };

                    if (el.TryGetProperty("tiers", out var tiersEl) && tiersEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tierEl in tiersEl.EnumerateArray())
                        {
                            var tierData = new WvwUpgradeTierData
                            {
                                Name = tierEl.TryGetProperty("name", out var tn) ? tn.GetString() : "",
                                YaksRequired = tierEl.TryGetProperty("yaks_required", out var yr) ? yr.GetInt32() : 0
                            };

                            if (tierEl.TryGetProperty("upgrades", out var upgradesEl) && upgradesEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var upEl in upgradesEl.EnumerateArray())
                                {
                                    tierData.SubUpgrades.Add(new WvwSubUpgradeItem
                                    {
                                        Name = upEl.TryGetProperty("name", out var un) ? un.GetString() : "",
                                        Description = upEl.TryGetProperty("description", out var ud) ? ud.GetString() : "",
                                        IconUrl = upEl.TryGetProperty("icon", out var ui) ? ui.GetString() : ""
                                    });
                                }
                            }
                            data.Tiers.Add(tierData);
                        }
                    }
                    dict[id] = data;
                }
            }
            return dict;
        }
    }
}