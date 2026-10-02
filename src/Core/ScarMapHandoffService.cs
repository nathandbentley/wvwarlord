using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Settings;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;
using WvWarlord.Api;
using WvWarlord.Models;
using WvWarlord.UI;

namespace WvWarlord.Core
{
    /// <summary>Which objectives are eligible for Auto-Destination hand-off. Same three modes ScarMapPanel used to filter on before it moved out to its own module.</summary>
    public enum ScarMapFilterMode { AllStructures, OnlyEnemy, OnlyFriendly }

    /// <summary>
    /// What's left in WvWarlord now that ScarMap is a standalone module:
    /// the Filter and Auto-Destination settings (per SillyHuman's request to
    /// keep those here), and the two ways a destination gets handed off to
    /// ScarMap --
    ///   1. Explicitly: the "Send to ScarMap" context menu entry on any card
    ///      (WvwCards.cs) and the ScarMap ChatLinkRoute both still funnel
    ///      through ChatLinkRouter.ScarMapTarget, which this class now owns
    ///      instead of a panel.
    ///   2. Automatically: when Auto-Destination is on, Tick() (called from
    ///      WvWarlordModule.Update -- see the Control.Update-is-sealed note
    ///      in learnings.md) finds the closest Filter-eligible objective on
    ///      the player's current map, same logic ScarMapPanel used to run
    ///      locally, and sends it whenever the pick changes.
    /// Either way, what actually crosses over is the chatlink plus a
    /// CardPro-equivalent snapshot (see ScarMapInterop.ScarMapHandoff) --
    /// ScarMap has no access to WvwCatalogService/WvwLiveDataService anymore,
    /// so it needs the full picture handed to it up front.
    /// </summary>
    public class ScarMapHandoffService
    {
        private readonly WvwModuleContext _ctx;
        private readonly SettingEntry<string> _filterSetting;
        private readonly SettingEntry<bool> _autoDestinationSetting;

        private string _lastAutoSentObjectiveId;
        private double _tickAccumulatorMs;

        public ScarMapHandoffService(WvwModuleContext ctx, SettingCollection settings)
        {
            _ctx = ctx;
            _filterSetting = settings.DefineSetting("ScarMapFilter", "AllStructures");
            _autoDestinationSetting = settings.DefineSetting("ScarMapAutoDestination", true);
        }

        /// <summary>Entry point wired to ChatLinkRouter.ScarMapTarget -- used by every card's "Send to ScarMap" context-menu entry and by ChatLinkRoute.ScarMap.</summary>
        public void SendChatLink(string chatLink)
        {
            if (string.IsNullOrEmpty(chatLink)) return;

            var target = WvwCatalogService.ById.Values.FirstOrDefault(o => o.ChatLink.Equals(chatLink, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                ScreenNotification.ShowNotification("Chat link did not match any known WvW objective.", ScreenNotification.NotificationType.Error, null, 3);
                return;
            }

            SendObjective(target);
        }

        /// <summary>Call once per module Update tick. Throttled internally -- checking the closest objective every single frame would be wasteful and isn't needed for something this coarse-grained.</summary>
        public void Tick(GameTime gameTime)
        {
            if (!_autoDestinationSetting.Value)
            {
                _lastAutoSentObjectiveId = null; // force a fresh pick+send next time Auto-Destination is re-enabled
                return;
            }

            // Auto-Destination only makes sense on an actual WvW map, and
            // only if ScarMap is there to receive the hand-off -- otherwise
            // this used to still run on every map (WvW or not) and re-fire
            // as soon as ANY map change made the closest-objective pick
            // different from whatever was last sent on the previous map,
            // including a Send() that just errors out when ScarMap isn't
            // installed. Resetting _lastAutoSentObjectiveId here (same as
            // the Auto-Destination-off branch above) means re-entering a
            // WvW map, or (re)installing ScarMap, always gets a fresh pick
            // rather than silently reusing a stale one from before the gap.
            if (!WvwStaticData.MapLabels.ContainsKey(_ctx.CurrentMapId) || !ScarMapInterop.IsAvailable || !GameService.Gw2Mumble.IsAvailable)
            {
                _lastAutoSentObjectiveId = null;
                return;
            }

            _tickAccumulatorMs += gameTime.ElapsedGameTime.TotalMilliseconds;
            if (_tickAccumulatorMs < 1000) return;
            _tickAccumulatorMs = 0;

            // Use pure synchronous conversion to scale Mumble meters to Game Units
            var playerPos = GameService.Gw2Mumble.PlayerCharacter.Position;
            var playerCoord = WvwCatalogService.WorldToLocalMapCoord(playerPos);

            var picked = TryAutoPickClosest(playerCoord);
            if (picked == null || picked.Id == _lastAutoSentObjectiveId) return;

            _lastAutoSentObjectiveId = picked.Id;
            SendObjective(picked);
        }

        private WvwObjectiveInfo TryAutoPickClosest(Vector2 playerMapCoord)
        {
            if (!WvwCatalogService.ByMap.TryGetValue(_ctx.CurrentMapId, out var mapObjs)) return null;
            return mapObjs.Where(PassesFilter).OrderBy(o => Vector2.Distance(playerMapCoord, o.LocalCoord)).FirstOrDefault();
        }

        private bool PassesFilter(WvwObjectiveInfo obj)
        {
            bool isNavigableType = obj.Type == WvwObjectiveType.Camp || obj.Type == WvwObjectiveType.Tower
                                 || obj.Type == WvwObjectiveType.Keep || obj.Type == WvwObjectiveType.Castle
                                 || obj.Type == WvwObjectiveType.Ruins;
            if (!isNavigableType) return false;

            var filter = ParseFilter(_filterSetting.Value);
            if (filter == ScarMapFilterMode.AllStructures) return true;

            _ctx.DataService.LiveStates.TryGetValue(obj.Id, out var state);
            string owner = state?.Owner ?? "Neutral";

            if (filter == ScarMapFilterMode.OnlyEnemy) return !owner.Equals(_ctx.MyTeamColor, StringComparison.OrdinalIgnoreCase); // enemy-held or neutral
            return owner.Equals(_ctx.MyTeamColor, StringComparison.OrdinalIgnoreCase); // OnlyFriendly
        }

        private void SendObjective(WvwObjectiveInfo obj)
        {
            _ctx.DataService.LiveStates.TryGetValue(obj.Id, out var state);

            string guildTag = null;
            if (!string.IsNullOrEmpty(state?.ClaimedByGuildId))
            {
                _ctx.DataService.GuildTagCache.TryGetValue(state.ClaimedByGuildId, out guildTag);
            }

            var handoff = new ScarMapInterop.ScarMapHandoff
            {
                ChatLink = obj.ChatLink,
                ObjectiveId = obj.Id,
                Name = obj.Name,
                Type = obj.Type.ToString(),
                MapId = obj.MapId,
                MapLabel = WvwStaticData.MapLabels.TryGetValue(obj.MapId, out var mapLabel) ? mapLabel : null,
                CoordX = obj.Coord.X,
                CoordY = obj.Coord.Y,
                Owner = state?.Owner ?? "Neutral",
                Tier = state?.Tier ?? 0,
                YaksDelivered = state?.YaksDelivered ?? 0,
                IsContested = state?.IsContested ?? false,
                ClaimedByGuildTag = guildTag,
                ActiveGuildTacticIds = state?.GuildUpgrades != null ? new List<int>(state.GuildUpgrades) : new List<int>(),
                HasWaypoint = state?.HasWaypoint ?? false,
                LastFlippedUtc = state != null && state.LastFlipped != DateTime.MinValue ? (DateTime?)state.LastFlipped : null
            };

            if (ScarMapInterop.Send(handoff))
            {
                ScreenNotification.ShowNotification($"Sent to ScarMap: {obj.Name}", ScreenNotification.NotificationType.Green, null, 3);
            }
        }

        private static string FormatFilter(ScarMapFilterMode f)
        {
            switch (f)
            {
                case ScarMapFilterMode.OnlyEnemy: return "Only Enemy";
                case ScarMapFilterMode.OnlyFriendly: return "Only Friendly";
                default: return "All Structures";
            }
        }

        private static ScarMapFilterMode ParseFilter(string stored)
        {
            switch (stored)
            {
                case "Only Enemy":
                case "OnlyEnemy":
                case "Only Occupied": // values saved before the rename
                case "OnlyOccupied": return ScarMapFilterMode.OnlyEnemy;
                case "Only Friendly":
                case "OnlyFriendly": return ScarMapFilterMode.OnlyFriendly;
                default: return ScarMapFilterMode.AllStructures;
            }
        }

        /// <summary>Back to defaults: All Structures, Auto-Destination on.</summary>
        public void ResetToDefaults()
        {
            _filterSetting.Value = ScarMapFilterMode.AllStructures.ToString();
            _autoDestinationSetting.Value = true;
        }

        /// <summary>Filter dropdown and Auto-Destination checkbox -- same UI ScarMapPanel used to contribute to the settings flyout, just owned here now.</summary>
        public void BuildFlyoutSection(Panel parent, int x, int width, ref int y)
        {
            // One row (label + dropdown + toggle), matching Core Settings'
            // "ChatLink Handling" row format per SillyHuman -- was two
            // stacked rows (AddLabeledRow then AddCheckboxRow underneath).
            var filterLabel = new Label { Text = "ScarMap Filter", Size = new Point(170, 24), Font = GameService.Content.DefaultFont14, TextColor = Microsoft.Xna.Framework.Color.LightGray };
            var filterDd = new Dropdown { Size = new Point(170, 24) };
            foreach (var f in Enum.GetValues(typeof(ScarMapFilterMode)).Cast<ScarMapFilterMode>()) filterDd.Items.Add(FormatFilter(f));
            filterDd.SelectedItem = FormatFilter(ParseFilter(_filterSetting.Value));
            filterDd.ValueChanged += (s, e) => _filterSetting.Value = ParseFilter(filterDd.SelectedItem).ToString();

            var autoDestCheck = new Checkbox { Text = "Auto-Destination", Checked = _autoDestinationSetting.Value };
            autoDestCheck.CheckedChanged += (s, e) => _autoDestinationSetting.Value = autoDestCheck.Checked;

            SettingsFlyoutBuilder.AddFlowRow(parent, x, ref y, filterLabel, filterDd, autoDestCheck);
        }
    }
}