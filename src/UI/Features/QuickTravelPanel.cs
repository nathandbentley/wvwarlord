using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WvWarlord.Api;
using WvWarlord.Core;
using WvWarlord.Models;
using WvWarlord.UI;
using WvWarlord.UI.Cards;

namespace WvWarlord.UI.Features
{
    public enum QuickTravelFilter { MyMapAndColor, MyColor, MyMap, All }

    /// <summary>
    /// Quick Travel: a static per-team shortcut tray (home spawns/keeps/border
    /// waypoints -- hardcoded, see WvwStaticShortcuts) on top, and a dynamic
    /// CardTiny strip below showing ONLY objectives that currently have an
    /// active/built waypoint (state.HasWaypoint). Static spawns are already
    /// covered by the tray, so the dynamic list excludes Spawn-type entirely.
    /// </summary>
    public class QuickTravelPanel : FeaturePanelBase
    {
        private const int MaxHeight = 420;

        private readonly WvwModuleContext _ctx;
        private readonly SettingEntry<string> _filterSetting;
        private Panel _staticTray;
        private FlowPanel _travelList;
        private QuickTravelFilter _filter;
        private readonly List<WvwCardTiny> _cards = new List<WvwCardTiny>();

        public QuickTravelPanel(WvwModuleContext ctx, Point size, Texture2D windowIcon, SettingEntry<string> filterSetting)
            : base("quick_travel", "QUICK TRAVEL", size, windowIcon)
        {
            _ctx = ctx;
            // Per SillyHuman ("all these settings should be how things load
            // on reboot") -- was a plain field that reset to
            // MyMapAndColor every reload. Same string-enum storage pattern
            // as TacOverviewState, for the same reason (enum round-trip
            // through Blish HUD's settings serializer isn't verified here).
            _filterSetting = filterSetting;
            _filter = Enum.TryParse(_filterSetting.Value, out QuickTravelFilter parsed) ? parsed : QuickTravelFilter.MyMapAndColor;
            _ctx.CurrentMapChanged += (s, e) => RebuildList(force: true);
            _ctx.DataService.DataUpdated += (s, e) => { RebuildStaticTray(); RebuildList(); };
            InitializeChrome();
        }

        /// <summary>Settings button lives on UserDebug's title bar only now (see FeaturePanelBase.ShowSettingsButton) -- this panel's flyout section is still built via BuildFlyoutSection below, just opened from there instead.</summary>
        protected override bool ShowSettingsButton => false;

        protected override void BuildContent(Panel contentArea)
        {
            _staticTray = new Panel { Parent = contentArea, Location = new Point(0, 0), Size = new Point(contentArea.Width, 36) };
            RebuildStaticTray();

            _travelList = new FlowPanel
            {
                Parent = contentArea,
                Location = new Point(0, 44),
                Size = new Point(contentArea.Width, contentArea.Height - 44),
                FlowDirection = ControlFlowDirection.LeftToRight,
                ControlPadding = new Vector2(2, 2),
                CanScroll = true
            };

            RebuildList(force: true);
        }

        /// <summary>Filter dropdown moved here entirely (no inline copy) -- see the settings flyout.</summary>
        public void BuildFlyoutSection(Panel parent, int x, int width, ref int y)
        {
            int controlWidth = width / 2 - 16;
            var dd = new Dropdown { Size = new Point(controlWidth, 24) };
            foreach (var f in Enum.GetValues(typeof(QuickTravelFilter)).Cast<QuickTravelFilter>()) dd.Items.Add(FormatFilter(f));
            dd.SelectedItem = FormatFilter(_filter);
            dd.ValueChanged += (s, e) => { _filter = ParseFilter(dd.SelectedItem); _filterSetting.Value = _filter.ToString(); RebuildList(force: true); };
            SettingsFlyoutBuilder.AddLabeledRow(parent, x, width, ref y, "Filter", dd);
        }

        /// <summary>Back to the default filter (My Map &amp; Color).</summary>
        public void ResetToDefaults()
        {
            _filter = QuickTravelFilter.MyMapAndColor;
            _filterSetting.Value = _filter.ToString();
            RebuildList(force: true);
        }

        private string _traySignature;
        private string _listSignature;

        private void RebuildStaticTray()
        {
            var shortcuts = WvwStaticShortcuts.ForColor(_ctx.MyTeamColor);

            // Only rebuild when what's shown actually changed (team color or
            // which keeps we hold) instead of every 15s data tick.
            string signature = _ctx.MyTeamColor + ":" + string.Join(",", shortcuts
                .Where(sc => !sc.IsKeepIcon || (_ctx.DataService.LiveStates.TryGetValue(sc.ObjectiveId, out var ks) && ks.Owner.Equals(_ctx.MyTeamColor, StringComparison.OrdinalIgnoreCase)))
                .Select(sc => sc.ObjectiveId));
            if (signature == _traySignature) return;
            _traySignature = signature;

            _staticTray.ClearChildren();

            // Fixed 4-column grid (see StaticShortcut.ColumnIndex), sized to
            // whatever width this panel actually has -- a missing keep slot
            // for an enemy home map just never gets an Image created at that
            // X, leaving genuine blank space rather than compacting the row,
            // so the tray lines up the same regardless of color or panel width.
            const int iconSize = 32;
            int colWidth = Math.Max(iconSize + 6, _staticTray.Width / 4);

            foreach (var shortcut in shortcuts)
            {
                if (shortcut.IsKeepIcon)
                {
                    string owner = _ctx.DataService.LiveStates.TryGetValue(shortcut.ObjectiveId, out var st) ? st.Owner : "Neutral";
                    if (!owner.Equals(_ctx.MyTeamColor, StringComparison.OrdinalIgnoreCase)) continue; // not currently ours -- leave this slot blank
                }
                int x = shortcut.ColumnIndex * colWidth + (shortcut.IsWaypointSlot ? colWidth / 2 : 0);

                var icon = new Image
                {
                    Parent = _staticTray,
                    Location = new Point(x, 2),
                    Size = new Point(iconSize, iconSize),
                    Texture = shortcut.IsKeepIcon ? WvwIconProvider.ForObjectiveType(Gw2Sharp.WebApi.V2.Models.WvwObjectiveType.Keep) : WvwIconProvider.Waypoint(),
                    Tint = shortcut.TintColor,
                    BasicTooltipText = shortcut.Name
                };
                icon.Click += (s, e) => _ = _ctx.Router.RouteAsync(shortcut.ChatLink);
            }
        }

        private static string FormatFilter(QuickTravelFilter f)
        {
            switch (f)
            {
                case QuickTravelFilter.MyMapAndColor: return "My Map & Color";
                case QuickTravelFilter.MyColor: return "My Color";
                case QuickTravelFilter.MyMap: return "My Map";
                default: return "All";
            }
        }

        private static QuickTravelFilter ParseFilter(string display)
        {
            switch (display)
            {
                case "My Map & Color": return QuickTravelFilter.MyMapAndColor;
                case "My Color": return QuickTravelFilter.MyColor;
                case "My Map": return QuickTravelFilter.MyMap;
                default: return QuickTravelFilter.All;
            }
        }

        private IEnumerable<WvwObjectiveInfo> GetActiveWaypointObjectives()
        {
            foreach (var kvp in WvwCatalogService.ByMap)
            {
                foreach (var obj in kvp.Value)
                {
                    if (obj.Type == Gw2Sharp.WebApi.V2.Models.WvwObjectiveType.Spawn) continue; // covered by the static tray
                    if (_ctx.DataService.LiveStates.TryGetValue(obj.Id, out var state) && state.HasWaypoint)
                    {
                        yield return obj;
                    }
                }
            }
        }

        private void RebuildList(bool force = false)
        {
            string myColor = _ctx.MyTeamColor;
            int myMapId = _ctx.CurrentMapId;

            IEnumerable<WvwObjectiveInfo> candidates = GetActiveWaypointObjectives();

            switch (_filter)
            {
                case QuickTravelFilter.MyMapAndColor:
                    // OR, not AND: current map OR my color, either qualifies.
                    candidates = candidates.Where(o => o.MapId == myMapId || ResolveOwner(o).Equals(myColor, StringComparison.OrdinalIgnoreCase));
                    break;
                case QuickTravelFilter.MyColor:
                    candidates = candidates.Where(o => ResolveOwner(o).Equals(myColor, StringComparison.OrdinalIgnoreCase));
                    break;
                case QuickTravelFilter.MyMap:
                    candidates = candidates.Where(o => o.MapId == myMapId);
                    break;
            }

            var orderedList = candidates
                .OrderByDescending(o => o.MapId == myMapId ? 1 : 0)
                .ThenByDescending(o => ResolveOwner(o).Equals(myColor, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(o => o.Name)
                .ToList();

            // DataUpdated used to only refresh the existing cards, so an
            // objective gaining/losing a waypoint or changing hands never
            // added/removed its card until a map or filter change. Rebuild
            // when the set/order changes; otherwise just refresh the text.
            string signature = string.Join(",", orderedList.Select(o => o.Id));
            if (!force && signature == _listSignature)
            {
                RefreshCardsOnly();
                return;
            }
            _listSignature = signature;

            _travelList.ClearChildren();
            _cards.Clear();

            foreach (var obj in orderedList)
            {
                var card = new WvwCardTiny(_ctx.DataService, obj.Id, _ctx.Router) { Parent = _travelList };
                card.Refresh();
                _cards.Add(card);
            }

            // Auto-height: tray (36) + however many rows of CardTiny the
            // list actually needs, at the 2px card spacing. Filter row moved
            // to the settings flyout, so that offset is gone from this calc.
            int cardsPerRow = Math.Max(1, _travelList.Width / (WvwCardTiny.CardWidth + 2));
            int rows = (int)Math.Ceiling(orderedList.Count / (double)cardsPerRow);
            int neededHeight = 44 + rows * (WvwCardTiny.CardHeight + 2) + 8 + 28; // + title bar
            ResizeExpandedHeight(Math.Min(neededHeight, MaxHeight));
            // _travelList was sized once at build time and never again, so
            // the list stayed at its first (tiny) height however much the
            // panel grew.
            _travelList.Height = Math.Max(0, ContentArea.Height - 44);
        }

        private string ResolveOwner(WvwObjectiveInfo obj) => _ctx.DataService.LiveStates.TryGetValue(obj.Id, out var state) ? state.Owner : "Neutral";

        private void RefreshCardsOnly()
        {
            foreach (var c in _cards) c.Refresh();
        }
    }
}