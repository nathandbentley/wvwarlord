using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Modules.Managers;
using WvWarlord.Models;

namespace WvWarlord.Api
{
    /// <summary>
    /// Owns all mutable "match/account state" data plus the fetch pipeline that
    /// keeps it current: startup preload, the 15-second live match loop, and the
    /// ad-hoc guild-tag resolution queue. Contains zero UI references -- views
    /// subscribe to <see cref="DataUpdated"/> and read the public state.
    /// </summary>
    public class WvwLiveDataService
    {
        private readonly Gw2ApiManager _gw2ApiManager;
        private string _apiKey;
        private bool _isProcessingGuildBatch = false;

        private string _lastMapsHash = string.Empty;

        public Dictionary<string, MyGuildData> MyAccountGuilds { get; } = new Dictionary<string, MyGuildData>(StringComparer.OrdinalIgnoreCase);
        // Concurrent: written from background tasks (15s loop, guild-tag batch)
        // while UI code on the main thread reads and enumerates them.
        public ConcurrentDictionary<string, LiveObjectiveState> LiveStates { get; } = new ConcurrentDictionary<string, LiveObjectiveState>(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, string> GuildTagCache { get; } = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string MyAccountName { get; private set; } = "Unknown";
        public string MyWvwGuildId { get; private set; } = "None";
        public int MyTeamId { get; private set; } = 0;
        public string MyTeamColor { get; private set; } = "Neutral";
        public string ActiveMatchId { get; private set; } = "Not Found";
        public DateTime MatchEndTime { get; private set; } = DateTime.MinValue;
        public DateTime LastUpdateTime { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// Timestamp of the last time the raw map objective payload actually changed (hash mismatch).
        /// </summary>
        public DateTime LastMatchDataChangeTime { get; private set; } = DateTime.MinValue;

        public bool HasPreloaded { get; private set; } = false;

        public event EventHandler DataUpdated;
        public event EventHandler PreloadCompleted;
        public event EventHandler<string> PreloadFailed;

        public WvwLiveDataService(Gw2ApiManager gw2ApiManager)
        {
            _gw2ApiManager = gw2ApiManager;
        }

        /// <summary>
        /// Raises an event on Blish HUD's main thread. The 15s loop and the
        /// guild-tag batch run on thread-pool threads, and every subscriber
        /// creates/destroys/updates controls, which isn't safe off the main
        /// thread (it can throw "collection was modified" mid-draw).
        /// </summary>
        private void RaiseOnMainThread(EventHandler handler)
        {
            if (handler == null) return;
            GameService.Overlay.QueueMainThreadUpdate(_ =>
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception ex)
                {
                    ApiCallTracker.RecordCatch();
                    ApiCallTracker.Log($"Event handler failed: {ex.Message}");
                }
            });
        }

        private void RaiseFailed(string message)
        {
            var handler = PreloadFailed;
            if (handler == null) return;
            GameService.Overlay.QueueMainThreadUpdate(_ => handler(this, message));
        }

        public void SetApiKey(string apiKey) => _apiKey = apiKey?.Trim();

        public async Task PreloadAsync()
        {
            ApiCallTracker.Log("Preload started.");
            try
            {
                if (string.IsNullOrEmpty(_apiKey))
                {
                    HasPreloaded = false;
                    RaiseFailed("Please enter your API key in this module's settings to find your WvW Team ID.");
                    return;
                }

                bool teamResolved = false;
                try
                {
                    string json = await ApiFallbackExecutor.GetRawAsync("account/wvw", _apiKey);
                    ApiCallTracker.RecordDirectCall();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("team", out var teamProp))
                        {
                            MyTeamId = teamProp.GetInt32();
                            teamResolved = true;
                        }
                        if (root.TryGetProperty("guild", out var guildProp) && guildProp.ValueKind != JsonValueKind.Null)
                        {
                            MyWvwGuildId = guildProp.GetString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    ApiCallTracker.RecordCatch();
                    ApiCallTracker.Log($"account/wvw resolution failed: {ex.Message}");
                }

                if (!teamResolved)
                {
                    HasPreloaded = false;
                    RaiseFailed("Could not resolve your WvW team from the API key provided.");
                    return;
                }

                await LoadAccountAndGuildsAsync();

                var match = await ResolveActiveMatchAsync();
                if (match.matchId != null)
                {
                    ActiveMatchId = match.matchId;
                    MatchEndTime = match.endTime;

                    LastMatchDataChangeTime = DateTime.UtcNow;

                    ProcessLiveMatchDataRaw(match.rawMapsElement);
                }

                HasPreloaded = true;
                LastUpdateTime = DateTime.UtcNow;
                ApiCallTracker.Log($"Preload complete. Team {MyTeamId} ({MyTeamColor}), match {ActiveMatchId}.");
                RaiseOnMainThread(PreloadCompleted);
                RaiseOnMainThread(DataUpdated);
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"PreloadAsync critical failure: {ex.Message}");
                RaiseFailed($"Critical preload failure: {ex.Message}");
            }
        }

        private class AccountSummary
        {
            public string Name;
            public List<string> GuildIds = new List<string>();
        }

        private async Task LoadAccountAndGuildsAsync()
        {
            try
            {
                var summary = await ApiFallbackExecutor.ExecuteAsync<AccountSummary>(
                    "Account.Get",
                    route1: async () =>
                    {
                        var acct = await _gw2ApiManager.Gw2ApiClient.V2.Account.GetAsync();
                        return new AccountSummary
                        {
                            Name = acct.Name,
                            GuildIds = acct.Guilds?.Select(g => g.ToString()).ToList() ?? new List<string>()
                        };
                    },
                    route2RawJsonFactory: () => ApiFallbackExecutor.GetRawAsync("account", _apiKey),
                    route2Parser: json =>
                    {
                        using (var doc = JsonDocument.Parse(json))
                        {
                            var root = doc.RootElement;
                            return new AccountSummary
                            {
                                Name = root.TryGetProperty("name", out var n) ? n.GetString() : "",
                                GuildIds = root.TryGetProperty("guilds", out var g) && g.ValueKind == JsonValueKind.Array
                                    ? g.EnumerateArray().Select(x => x.GetString()).ToList()
                                    : new List<string>()
                            };
                        }
                    });

                if (!string.IsNullOrEmpty(summary.Name)) MyAccountName = summary.Name;

                MyAccountGuilds.Clear();
                foreach (var guildId in summary.GuildIds)
                {
                    await LoadGuildDetailsAsync(guildId);
                }
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"LoadAccountAndGuildsAsync failed: {ex.Message}");
            }
        }

        private class GuildDetailsResult
        {
            public string Tag;
            public string Name;
            public int EmblemBackgroundId;
            public List<int> EmblemBackgroundColors = new List<int>();
            public int EmblemForegroundId;
            public List<int> EmblemForegroundColors = new List<int>();
            public List<string> EmblemFlags = new List<string>();
        }

        private async Task LoadGuildDetailsAsync(string guildId)
        {
            try
            {
                if (!Guid.TryParse(guildId, out Guid guildGuid)) return;

                var result = await ApiFallbackExecutor.ExecuteAsync<GuildDetailsResult>(
                    $"Guild.Get({guildId})",
                    route1: async () =>
                    {
                        var details = await _gw2ApiManager.Gw2ApiClient.V2.Guild[guildGuid].GetAsync();
                        return new GuildDetailsResult
                        {
                            Tag = details.Tag,
                            Name = details.Name,
                            EmblemBackgroundId = details.Emblem?.Background?.Id ?? 0,
                            EmblemBackgroundColors = details.Emblem?.Background?.Colors?.Select(c => (int)c).ToList() ?? new List<int>(),
                            EmblemForegroundId = details.Emblem?.Foreground?.Id ?? 0,
                            EmblemForegroundColors = details.Emblem?.Foreground?.Colors?.Select(c => (int)c).ToList() ?? new List<int>(),
                            EmblemFlags = details.Emblem?.Flags?.Select(f => f.ToString()).ToList() ?? new List<string>()
                        };
                    },
                    route2RawJsonFactory: () => ApiFallbackExecutor.GetRawAsync($"guild/{guildGuid}", _apiKey),
                    route2Parser: json =>
                    {
                        using (var doc = JsonDocument.Parse(json))
                        {
                            var root = doc.RootElement;
                            var r = new GuildDetailsResult
                            {
                                Tag = root.TryGetProperty("tag", out var t) ? t.GetString() : "",
                                Name = root.TryGetProperty("name", out var n) ? n.GetString() : "Unknown Guild Name"
                            };

                            if (root.TryGetProperty("emblem", out var emblem))
                            {
                                if (emblem.TryGetProperty("background", out var bg))
                                {
                                    r.EmblemBackgroundId = bg.TryGetProperty("id", out var bgId) ? bgId.GetInt32() : 0;
                                    if (bg.TryGetProperty("colors", out var bgColors))
                                        r.EmblemBackgroundColors = bgColors.EnumerateArray().Select(c => c.GetInt32()).ToList();
                                }
                                if (emblem.TryGetProperty("foreground", out var fg))
                                {
                                    r.EmblemForegroundId = fg.TryGetProperty("id", out var fgId) ? fgId.GetInt32() : 0;
                                    if (fg.TryGetProperty("colors", out var fgColors))
                                        r.EmblemForegroundColors = fgColors.EnumerateArray().Select(c => c.GetInt32()).ToList();
                                }
                                if (emblem.TryGetProperty("flags", out var flagsEl))
                                    r.EmblemFlags = flagsEl.EnumerateArray().Select(f => f.GetString()).ToList();
                            }

                            return r;
                        }
                    });

                var guildData = new MyGuildData
                {
                    Id = guildId,
                    Tag = !string.IsNullOrEmpty(result.Tag) ? $"[{result.Tag.ToUpper()}]" : "",
                    Name = result.Name ?? "Unknown Guild Name",
                    EmblemBackgroundId = result.EmblemBackgroundId,
                    EmblemBackgroundColors = result.EmblemBackgroundColors,
                    EmblemForegroundId = result.EmblemForegroundId,
                    EmblemForegroundColors = result.EmblemForegroundColors,
                    EmblemFlags = result.EmblemFlags
                };
                MyAccountGuilds[guildId] = guildData;
                GuildTagCache[guildId] = guildData.Tag;
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"LoadGuildDetailsAsync({guildId}) failed: {ex.Message}");
            }
        }

        private async Task<(string matchId, DateTime endTime, JsonElement rawMapsElement)> ResolveActiveMatchAsync()
        {
            try
            {
                string json = await ApiFallbackExecutor.GetRawAsync($"wvw/matches?world={MyTeamId}");
                ApiCallTracker.RecordDirectCall();
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    var matchEl = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().FirstOrDefault() : root;
                    if (matchEl.ValueKind != JsonValueKind.Object) return (null, DateTime.MinValue, default);

                    string matchId = matchEl.GetProperty("id").GetString();
                    DateTime endTime = DateTime.MinValue;
                    if (matchEl.TryGetProperty("end_time", out var endEl) && DateTime.TryParse(endEl.GetString(), out var parsedEnd))
                        endTime = parsedEnd.ToUniversalTime();

                    if (matchEl.TryGetProperty("all_worlds", out var allWorlds))
                    {
                        MyTeamColor = ResolveColorFromAllWorlds(allWorlds);
                    }

                    JsonElement mapsClone = default;
                    if (matchEl.TryGetProperty("maps", out var mapsEl))
                    {
                        mapsClone = JsonDocument.Parse(mapsEl.GetRawText()).RootElement;
                    }

                    return (matchId, endTime, mapsClone);
                }
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"ResolveActiveMatchAsync failed: {ex.Message}");
                return (null, DateTime.MinValue, default);
            }
        }

        private string ResolveColorFromAllWorlds(JsonElement allWorlds)
        {
            if (allWorlds.TryGetProperty("blue", out var blue) && blue.EnumerateArray().Any(w => w.GetInt32() == MyTeamId)) return "Blue";
            if (allWorlds.TryGetProperty("red", out var red) && red.EnumerateArray().Any(w => w.GetInt32() == MyTeamId)) return "Red";
            if (allWorlds.TryGetProperty("green", out var green) && green.EnumerateArray().Any(w => w.GetInt32() == MyTeamId)) return "Green";
            return "Neutral";
        }

        /// <summary>15-second periodic loop. Caller (module Update-tick timer) invokes this on a background task.</summary>
        public async Task ExecutePeriodicUpdateLoopAsync()
        {
            if (!HasPreloaded) return;

            if (DateTime.UtcNow > MatchEndTime && MatchEndTime != DateTime.MinValue)
            {
                HasPreloaded = false;
                await PreloadAsync();
                return;
            }

            try
            {
                string json = await ApiFallbackExecutor.GetRawAsync($"wvw/matches?ids={ActiveMatchId}");
                ApiCallTracker.RecordDirectCall();

                if (string.IsNullOrEmpty(json)) return;

                using (var doc = JsonDocument.Parse(json))
                {
                    var matchElement = doc.RootElement.EnumerateArray().FirstOrDefault();
                    if (matchElement.ValueKind != JsonValueKind.Object) return;

                    if (matchElement.TryGetProperty("maps", out var mapsArray))
                    {
                        try
                        {
                            string rawMapsText = mapsArray.GetRawText();
                            string currentHash = ComputeMd5Hash(rawMapsText);

                            if (string.IsNullOrEmpty(_lastMapsHash) || currentHash != _lastMapsHash)
                            {
                                _lastMapsHash = currentHash;
                                LastMatchDataChangeTime = DateTime.UtcNow;
                            }
                        }
                        catch (Exception ex)
                        {
                            ApiCallTracker.RecordCatch();
                            ApiCallTracker.Log($"Map hash computation failed: {ex.Message}");
                        }

                        ProcessLiveMatchDataRaw(mapsArray);
                    }

                    LastUpdateTime = DateTime.UtcNow;
                    RaiseOnMainThread(DataUpdated);

                    var guildsToProcess = GuildTagCache
                        .Where(kvp => kvp.Value.StartsWith("?"))
                        .Select(kvp => kvp.Key)
                        .Take(10)
                        .ToList();

                    if (guildsToProcess.Count > 0 && !_isProcessingGuildBatch)
                    {
                        _isProcessingGuildBatch = true;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                foreach (var guildId in guildsToProcess)
                                {
                                    await FetchGuildTagAsync(guildId);
                                    await Task.Delay(50);
                                }
                            }
                            catch (Exception ex)
                            {
                                ApiCallTracker.RecordCatch();
                                ApiCallTracker.Log($"Guild batch processing failed: {ex.Message}");
                            }
                            finally
                            {
                                _isProcessingGuildBatch = false;
                                RaiseOnMainThread(DataUpdated);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"ExecutePeriodicUpdateLoopAsync failed: {ex.Message}");
            }
        }

        private static string ComputeMd5Hash(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            using (var md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                var sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }

        private void ProcessLiveMatchDataRaw(JsonElement mapsArray)
        {
            if (mapsArray.ValueKind != JsonValueKind.Array) return;

            foreach (var mapElement in mapsArray.EnumerateArray())
            {
                if (!mapElement.TryGetProperty("objectives", out var objectivesArray) || objectivesArray.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var objective in objectivesArray.EnumerateArray())
                {
                    if (!objective.TryGetProperty("id", out var idProp)) continue;
                    string objId = idProp.GetString();
                    if (string.IsNullOrEmpty(objId)) continue;

                    if (!LiveStates.ContainsKey(objId)) LiveStates[objId] = new LiveObjectiveState { Id = objId };
                    var state = LiveStates[objId];

                    state.Owner = objective.TryGetProperty("owner", out var ownerProp) ? ownerProp.GetString() : "Neutral";
                    state.IsContested = false;

                    int yaksDelivered = 0;
                    if (objective.TryGetProperty("yaks_delivered", out var yaksProp) && yaksProp.ValueKind == JsonValueKind.Number)
                        yaksDelivered = yaksProp.GetInt32();
                    state.YaksDelivered = yaksDelivered;

                    var objType = WvwCatalogService.ById.TryGetValue(objId, out var info) ? info.Type : Gw2Sharp.WebApi.V2.Models.WvwObjectiveType.Camp;
                    state.Tier = WvwStaticData.GetTier(objType, yaksDelivered);

                    bool isStaticSpawn = info != null && info.IsStaticSpawn;
                    bool builtWaypoint = false;
                    if (!isStaticSpawn)
                    {
                        int upgradePathId = WvwStaticData.GetUpgradePathId(objType);
                        builtWaypoint = WvwUpgradeCatalogService.IsBuildWaypointUnlockedAtTier(upgradePathId, state.Tier);
                    }
                    state.HasWaypoint = isStaticSpawn || builtWaypoint;

                    if (objective.TryGetProperty("last_flipped", out var flippedProp) && flippedProp.ValueKind == JsonValueKind.String
                        && DateTime.TryParse(flippedProp.GetString(), null,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                            out var flippedDt))
                    {
                        state.LastFlipped = flippedDt;
                    }
                    else
                    {
                        state.LastFlipped = DateTime.MinValue;
                    }

                    state.ClaimedByGuildId = (objective.TryGetProperty("claimed_by", out var claimedProp) && claimedProp.ValueKind == JsonValueKind.String)
                        ? claimedProp.GetString()
                        : null;

                    if (!string.IsNullOrEmpty(state.ClaimedByGuildId)) RegisterGuild(state.ClaimedByGuildId);

                    if (state.GuildUpgrades == null) state.GuildUpgrades = new List<int>();
                    else state.GuildUpgrades.Clear();

                    if (objective.TryGetProperty("guild_upgrades", out var upgradesProp) && upgradesProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var idElem in upgradesProp.EnumerateArray())
                        {
                            if (idElem.ValueKind == JsonValueKind.Number) state.GuildUpgrades.Add(idElem.GetInt32());
                        }
                    }
                }
            }
        }

        private void RegisterGuild(string guildId)
        {
            if (string.IsNullOrEmpty(guildId)) return;
            if (!GuildTagCache.ContainsKey(guildId)) GuildTagCache[guildId] = "?";
        }

        private async Task FetchGuildTagAsync(string guildId)
        {
            try
            {
                if (!Guid.TryParse(guildId, out Guid guildGuid)) return;

                string json = await ApiFallbackExecutor.GetRawAsync($"guild/{guildGuid}");
                ApiCallTracker.RecordDirectCall();

                if (!string.IsNullOrEmpty(json))
                {
                    using (var doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("tag", out var tagProp) && tagProp.ValueKind == JsonValueKind.String)
                        {
                            string finalTag = tagProp.GetString();
                            if (!string.IsNullOrEmpty(finalTag))
                            {
                                GuildTagCache[guildId] = $"[{finalTag.ToUpper()}]";
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"FetchGuildTagAsync({guildId}) failed: {ex.Message}");
            }

            if (GuildTagCache.TryGetValue(guildId, out var currentProgress) && currentProgress.StartsWith("?"))
            {
                string updatedProgress = currentProgress + "?";
                GuildTagCache[guildId] = updatedProgress.Length >= 5 ? "-?-" : updatedProgress;
            }
        }

        public async Task WipeAndReloadAsync()
        {
            LiveStates.Clear();
            GuildTagCache.Clear();
            MyAccountGuilds.Clear();
            HasPreloaded = false;
            await PreloadAsync();
        }
    }
}