using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WvWarlord.Api;
using WvWarlord.Core;
using WvWarlord.Models;
using WvWarlord.UI.Cards;

namespace WvWarlord.UI.Features
{
    /// <summary>
    /// TacContentPanel: the right-hand card grid half of what used to be a
    /// single TacOverview panel. Has no controls of its own -- it just
    /// listens to WvwModuleContext.TacState.Changed (written by
    /// TacControlPanel) and ctx.CurrentMapChanged, and renders accordingly.
    ///   - Single Map: one vertical scrolling list, Card format (Ruins/Mercenary use CardTiny).
    ///   - All Map Grid: current map split into 4 type-columns (Castle/Keep, Tower, Camp, Ruins), Card format.
    ///   - Detail Grid: no longer 4 fixed building-type columns -- CardPro-eligible objectives are
    ///     packed left to right in the map's normal sort order, up to ProColumnItemLimit (8) per
    ///     column. Ruins/Mercenary (CardTiny) append to the bottom of the last column instead of
    ///     getting one of their own, uncapped.
    ///   - Map: macro coordinate targeting -- not yet implemented.
    /// </summary>
    public class TacContentPanel : FeaturePanelBase
    {
        /// <summary>
        /// One map column matches WvwCardStandard's fixed width (250) plus
        /// room for the column's own scrollbar, same "content width +
        /// scrollbar width" pattern as the left column. Single Map shows one
        /// of these; All Maps shows four side by side (one per
        /// WvwStaticData.MapLabels entry).
        /// </summary>
        public const int MapColumnWidth = WvwCardStandard.CardWidth + ScrollbarWidthAssumption;

        /// <summary>
        /// Legacy fixed-4-column Detail Grid width. Detail Grid's column
        /// count is now computed dynamically (see ComputeDetailGridColumnCount)
        /// since columns are packed by item count rather than building type --
        /// this constant is kept only in case something outside this file
        /// still reads it as an upper bound.
        /// </summary>
        public const int DetailGridWidth = 4 * (WvwCardPro.CardWidth + 20); // stale reference value from the old fixed-4-column/340-wide-card layout; not used for sizing anymore

        /// <summary>Max CardPro objectives packed into one Detail Grid column before wrapping to the next -- see BuildPackedProColumns. Doesn't apply to the trailing Ruins/Mercenary column.</summary>
        private const int ProColumnItemLimit = 8;

        // Same unverified Blish HUD scrollbar-width assumption as
        // WvWarlordModule.LeftScrollbarWidth -- kept as its own constant
        // here (rather than referencing that one) to avoid a cross-namespace
        // dependency for a single shared number; tune both together if it's wrong.
        private const int ScrollbarWidthAssumption = 20;

        private static readonly (WvwObjectiveType[] Types, string Header)[] TypeColumns =
        {
            (new[] { WvwObjectiveType.Castle, WvwObjectiveType.Keep }, "CASTLE / KEEP"),
            (new[] { WvwObjectiveType.Tower }, "TOWER"),
            (new[] { WvwObjectiveType.Camp }, "CAMP"),
            (new[] { WvwObjectiveType.Ruins }, "RUINS"),
        };

        private readonly WvwModuleContext _ctx;

        private readonly List<WvwCardStandard> _standardCards = new List<WvwCardStandard>();
        private readonly List<WvwCardTiny> _tinyCards = new List<WvwCardTiny>();
        private readonly List<WvwCardPro> _proCards = new List<WvwCardPro>();
        // Populated by SortObjectives when SortMode == PlayerDistance;
        // consulted by the card-creation loops below to actually display
        // the value the sort was already computing (it used to be thrown
        // away after establishing sort order -- the cards had no way to
        // show it and nothing ever passed it to them).
        private readonly Dictionary<string, float> _lastDistances = new Dictionary<string, float>();

        // No title bar: this is a passive data grid driven entirely by
        // TacControlPanel's dropdowns/buttons, so it doesn't need its own
        // Close/Minimize/Roam/Battle chrome or its own persisted settings --
        // see WvWarlordModule, which mirrors TacControlPanel's Enabled state
        // onto this panel's StateController instead of binding one of its own.
        public TacContentPanel(WvwModuleContext ctx, Point size, Texture2D windowIcon)
            : base("tac_content", "TAC OVERVIEW", size, windowIcon, showTitleBar: true)
        {
            _ctx = ctx;
            _ctx.TacState.Changed += (s, e) => RebuildContent();
            _ctx.CurrentMapChanged += (s, e) => RebuildContent();
            _ctx.DataService.DataUpdated += (s, e) => RefreshCardsOnly();
            InitializeChrome();
        }

        private double _distanceTickAccumulatorMs;
        private double _resortTickAccumulatorMs;
        private static readonly int[] FixedMapOrder = { 38, 1099, 95, 96 };
        /// <summary>
        /// Called once per module Update tick (see WvWarlordModule.Update).
        /// Distance used to only get computed once, inside SortObjectives,
        /// which only runs from RebuildContent (a real View/Filter/Sort/map
        /// change) -- so a card's shown distance was frozen at wherever the
        /// player was standing the last time one of those fired, not
        /// updated as they walked around. Throttled the same order of
        /// magnitude as ScarMapHandoffService.Tick -- recomputing every
        /// single frame would be wasteful for something this coarse-grained.
        /// Deliberately does NOT re-sort/rebuild the grid on every tick --
        /// only RebuildContent reorders cards; this just updates the number
        /// shown on cards already on screen, so the list doesn't visibly
        /// reshuffle every half second while you're moving. A full
        /// re-sort/RebuildContent runs on its own, much longer interval
        /// below instead, per SillyHuman -- close enough to live without
        /// the list jumping around constantly.
        /// </summary>
        public void Tick(GameTime gameTime)
        {
            if (_ctx.TacState.ViewMode == TacOverviewViewMode.Map) return; // Guild view has no distances; it refreshes from DataUpdated
            if (_ctx.TacState.SortMode != TacOverviewSortMode.PlayerDistance) return;

            double elapsedMs = gameTime.ElapsedGameTime.TotalMilliseconds;

            _resortTickAccumulatorMs += elapsedMs;
            if (_resortTickAccumulatorMs >= 15000)
            {
                _resortTickAccumulatorMs = 0;
                _distanceTickAccumulatorMs = 0; // RebuildContent already recomputes distances fresh via SortObjectives -- no need for the plain refresh below on the same tick
                RebuildContent();
                return;
            }

            _distanceTickAccumulatorMs += elapsedMs;
            if (_distanceTickAccumulatorMs < 500) return;
            _distanceTickAccumulatorMs = 0;

            RefreshDistances();
        }

        private void RefreshDistances()
        {
            if (!GameService.Gw2Mumble.IsAvailable) return;
            if (!WvwCatalogService.ByMap.TryGetValue(_ctx.CurrentMapId, out var mapObjectives)) return;

            var playerCoord = WvwCatalogService.WorldToLocalMapCoord(GameService.Gw2Mumble.PlayerCharacter.Position);
            foreach (var obj in mapObjectives)
            {
                _lastDistances[obj.Id] = Vector2.Distance(playerCoord, obj.LocalCoord)*24f;
            }

            foreach (var c in _standardCards) if (c.ObjectiveId != null && _lastDistances.TryGetValue(c.ObjectiveId, out var d)) { c.SetDistance(d); c.Refresh(); }
            foreach (var c in _tinyCards) if (c.ObjectiveId != null && _lastDistances.TryGetValue(c.ObjectiveId, out var d)) { c.SetDistance(d); c.Refresh(); }
            foreach (var c in _proCards) if (c.ObjectiveId != null && _lastDistances.TryGetValue(c.ObjectiveId, out var d)) { c.SetDistance(d); c.Refresh(); }
        }

        protected override void BuildContent(Panel contentArea)
        {
            RebuildContent();
        }

        private void RebuildContent()
        {
            int requiredWidth;
            switch (_ctx.TacState.ViewMode)
            {
                case TacOverviewViewMode.AllMapGrid: requiredWidth = 4 * MapColumnWidth; break;
                case TacOverviewViewMode.DetailGrid: requiredWidth = ComputeDetailGridColumnCount() * (WvwCardPro.CardWidth + 20); break;
                case TacOverviewViewMode.Map: requiredWidth = 2 * WvwCardGuildActivity.CardWidth + 12; break;
                default: requiredWidth = MapColumnWidth; break; // SingleMap, Map
            }
            // Resize BEFORE rebuilding: BuildAllMapsColumns/BuildTypeColumns
            // both compute column positions/widths from ContentArea.Width,
            // so it needs to already reflect the new view mode's width.
            ResizeExpandedWidth(requiredWidth);

            // Columns are parented straight to ContentArea -- no
            // intermediate _contentHost wrapper Panel, and (see
            // BuildMapColumn/BuildTypeColumns) no FlowPanel either anymore;
            // both are plain Panel with manual stacking now.
            ContentArea.ClearChildren();
            _standardCards.Clear();
            _tinyCards.Clear();
            _proCards.Clear();
            _lastDistances.Clear();

            switch (_ctx.TacState.ViewMode)
            {
                case TacOverviewViewMode.SingleMap:
                    BuildMapColumn(_ctx.CurrentMapId, x: 0, width: MapColumnWidth);
                    break;
                case TacOverviewViewMode.AllMapGrid:
                    BuildAllMapsColumns();
                    break;
                case TacOverviewViewMode.DetailGrid:
                    BuildPackedProColumns();
                    break;
                case TacOverviewViewMode.Map:
                    BuildGuildActivityList();
                    break;
            }
        }

        private bool PassesFilter(WvwObjectiveInfo obj)
        {
            if (_ctx.TacState.FilterMode == TacOverviewFilterMode.All) return true;
            _ctx.DataService.LiveStates.TryGetValue(obj.Id, out var st);
            string owner = st?.Owner ?? "Neutral";
            bool isMyColor = owner.Equals(_ctx.MyTeamColor, StringComparison.OrdinalIgnoreCase);
            return _ctx.TacState.FilterMode == TacOverviewFilterMode.MyColor ? isMyColor : !isMyColor;
        }

        private IEnumerable<WvwObjectiveInfo> SortObjectives(IEnumerable<WvwObjectiveInfo> objectives, int mapId)
        {
            if (_ctx.TacState.SortMode == TacOverviewSortMode.PlayerDistance && mapId == _ctx.CurrentMapId && GameService.Gw2Mumble.IsAvailable)
            {
                var playerCoord = WvwCatalogService.WorldToLocalMapCoord(GameService.Gw2Mumble.PlayerCharacter.Position);
                var list = objectives.ToList();

                // Evaluate distance strictly using the pre-aligned LocalCoord property
                foreach (var o in list) _lastDistances[o.Id] = Vector2.Distance(playerCoord, o.LocalCoord)*24f;
                return list.OrderBy(o => _lastDistances[o.Id]);
            }
            return objectives.OrderByDescending(o => o.PointsValue).ThenBy(o => o.Name);
        }

        /// <summary>
        /// One map's objectives as a single vertical scrolling list (Card
        /// format; Ruins/Mercenary use CardTiny), exactly MapColumnWidth
        /// wide -- shared by both Single Map (one of these) and All Maps
        /// (four of these side by side).
        /// </summary>
        private void BuildMapColumn(int mapId, int x, int width)
        {
            if (!WvwCatalogService.ByMap.TryGetValue(mapId, out var mapObjectives)) return;

            // Viewport/content split: "column" is the fixed-height,
            // CanScroll=true viewport; "content" is a child of it that
            // grows TALLER than the viewport once cards are added. Plain
            // Panel doesn't auto-track its children's extent the way
            // FlowPanel does, so CanScroll had nothing to scroll to when
            // the column's own Size.Y never grew past the viewport height --
            // this is the fix for "scrollbar appears but doesn't reach the
            // rest of the content".
            var column = new Panel
            {
                Parent = ContentArea,
                Location = new Point(x, 0),
                Size = new Point(width, ContentArea.Height),
                CanScroll = true
            };
            var content = new Panel { Parent = column, Location = Point.Zero, Size = new Point(width, ContentArea.Height) };

            int y = 0;
            string label = WvwStaticData.MapLabels.TryGetValue(mapId, out var lbl) ? lbl : "MAP";
            new Label { Parent = content, Location = new Point(0, y), Text = label.ToUpper(), Size = new Point(width, 18), HorizontalAlignment = HorizontalAlignment.Center, Font = GameService.Content.DefaultFont16, TextColor = WvwStaticData.GetFactionColor(label) };
            y += 20;

            var objectives = SortObjectives(mapObjectives.Where(o => o.Type != WvwObjectiveType.Spawn).Where(PassesFilter), mapId);

            foreach (var obj in objectives)
            {
                float? distance = _lastDistances.TryGetValue(obj.Id, out var d) ? d : (float?)null;

                if (obj.Type == WvwObjectiveType.Ruins || obj.Type == WvwObjectiveType.Mercenary)
                {
                    var tiny = new WvwCardTiny(_ctx.DataService, obj.Id, _ctx.Router) { Parent = content, Location = new Point(0, y) };
                    tiny.SetDistance(distance);
                    tiny.Refresh();
                    _tinyCards.Add(tiny);
                    y += tiny.Height + 2;
                }
                else
                {
                    var card = new WvwCardStandard(_ctx.DataService, obj.Id, _ctx.Router) { Parent = content, Location = new Point(0, y) };
                    card.SetDistance(distance);
                    card.Refresh();
                    _standardCards.Add(card);
                    y += card.Height + 2;
                }
            }

            content.Height = Math.Max(y, column.Height);
        }

        /// <summary>All Maps: identical per-map column to Single Map, but the current map plus the other three side by side.</summary>
        private void BuildAllMapsColumns()
        {
            var orderedMapIds = WvwStaticData.MapLabels.Keys
                .OrderByDescending(id => id == _ctx.CurrentMapId ? 1 : 0)
                .ToList();

            for (int i = 0; i < orderedMapIds.Count; i++)
            {
                BuildMapColumn(orderedMapIds[i], x: i * MapColumnWidth, width: MapColumnWidth);
            }
        }

        /// <summary>
        /// Splits the current map's Detail Grid objectives into
        /// CardPro-eligible (Castle/Keep/Tower/Camp) and CardTiny-eligible
        /// (Ruins/Mercenary) lists, each in the normal sort order -- shared
        /// by the width calc in RebuildContent and BuildPackedProColumns so
        /// they can't disagree on column count.
        /// </summary>
        private (List<WvwObjectiveInfo> proObjectives, List<WvwObjectiveInfo> tinyObjectives) GetDetailGridSplit()
        {
            int mapId = _ctx.CurrentMapId;
            if (!WvwCatalogService.ByMap.TryGetValue(mapId, out var mapObjectives))
                return (new List<WvwObjectiveInfo>(), new List<WvwObjectiveInfo>());

            var all = SortObjectives(mapObjectives.Where(o => o.Type != WvwObjectiveType.Spawn).Where(PassesFilter), mapId).ToList();
            var pro = all.Where(o => o.Type != WvwObjectiveType.Ruins && o.Type != WvwObjectiveType.Mercenary).ToList();
            var tiny = all.Where(o => o.Type == WvwObjectiveType.Ruins || o.Type == WvwObjectiveType.Mercenary).ToList();
            return (pro, tiny);
        }

        private int ComputeDetailGridColumnCount()
        {
            var (pro, tiny) = GetDetailGridSplit();
            int proColumns = pro.Count > 0 ? (int)Math.Ceiling(pro.Count / (double)ProColumnItemLimit) : 0;
            return Math.Max(1, proColumns); // Ruins/Mercenary append to the last column, not a column of their own
        }

        /// <summary>
        /// Detail Grid: replaces the old 4 fixed building-type columns.
        /// CardPro-eligible objectives (Castle/Keep/Tower/Camp) run in the
        /// same sort order as Single Map/All Maps, packed
        /// ProColumnItemLimit (8) per column. Ruins/Mercenary don't get a
        /// column of their own -- they're appended to the bottom of
        /// whichever column already ends up last, on top of however many
        /// pro cards (0-8) landed there, uncapped.
        /// </summary>
        private void BuildPackedProColumns()
        {
            var (proObjectives, tinyObjectives) = GetDetailGridSplit();
            if (proObjectives.Count == 0 && tinyObjectives.Count == 0) return;

            int columnCount = Math.Max(1, proObjectives.Count > 0 ? (int)Math.Ceiling(proObjectives.Count / (double)ProColumnItemLimit) : 0);
            int colWidth = WvwCardPro.CardWidth + 20;

            var columns = new Panel[columnCount];
            var contents = new Panel[columnCount];
            var yPositions = new int[columnCount];

            for (int c = 0; c < columnCount; c++)
            {
                columns[c] = new Panel
                {
                    Parent = ContentArea,
                    Location = new Point(c * colWidth, 0),
                    Size = new Point(colWidth - 6, ContentArea.Height),
                    CanScroll = true
                };
                contents[c] = new Panel { Parent = columns[c], Location = Point.Zero, Size = new Point(colWidth - 6, ContentArea.Height) };
            }

            for (int i = 0; i < proObjectives.Count; i++)
            {
                int col = i / ProColumnItemLimit;
                var content = contents[col];
                int y = yPositions[col];

                var pro = new WvwCardPro(_ctx.DataService, proObjectives[i].Id, _ctx.Router) { Parent = content, Location = new Point(0, y) };
                pro.SetDistance(_lastDistances.TryGetValue(proObjectives[i].Id, out var proDist) ? proDist : (float?)null);
                pro.Refresh();
                _proCards.Add(pro);
                y += pro.Height + 2;

                yPositions[col] = y;
            }

            // Ruins/Mercenary append to the last column (uncapped) rather
            // than getting their own -- "on the last column, not their own".
            if (tinyObjectives.Count > 0)
            {
                int lastCol = columnCount - 1;
                var content = contents[lastCol];
                int y = yPositions[lastCol];

                foreach (var obj in tinyObjectives)
                {
                    var tiny = new WvwCardTiny(_ctx.DataService, obj.Id, _ctx.Router) { Parent = content, Location = new Point(0, y), Width = content.Width - 10 };
                    tiny.SetDistance(_lastDistances.TryGetValue(obj.Id, out var tinyDist) ? tinyDist : (float?)null);
                    tiny.Refresh();
                    _tinyCards.Add(tiny);
                    y += tiny.Height + 2;
                }

                yPositions[lastCol] = y;
            }

            for (int c = 0; c < columnCount; c++)
            {
                contents[c].Height = Math.Max(yPositions[c], columns[c].Height);
            }
        }

        private void BuildGuildActivityList()
        {
            var byGuild = new Dictionary<string, (WvwObjectiveInfo, LiveObjectiveState)?[]>();
            var guildMostRecent = new Dictionary<string, DateTime>();
            var guildMostRecentSlot = new Dictionary<string, int>();

            foreach (var kvp in WvwCatalogService.ById)
            {
                var obj = kvp.Value;
                if (!_ctx.DataService.LiveStates.TryGetValue(obj.Id, out var state)) continue;
                if (string.IsNullOrEmpty(state.ClaimedByGuildId)) continue;

                int slot = Array.IndexOf(FixedMapOrder, obj.MapId);
                if (slot < 0) continue;

                if (!byGuild.TryGetValue(state.ClaimedByGuildId, out var slots))
                    byGuild[state.ClaimedByGuildId] = slots = new (WvwObjectiveInfo, LiveObjectiveState)?[4];

                if (!slots[slot].HasValue || state.LastFlipped > slots[slot].Value.Item2.LastFlipped)
                    slots[slot] = (obj, state);

                if (!guildMostRecent.TryGetValue(state.ClaimedByGuildId, out var prevMax) || state.LastFlipped > prevMax)
                {
                    guildMostRecent[state.ClaimedByGuildId] = state.LastFlipped;
                    guildMostRecentSlot[state.ClaimedByGuildId] = slot;
                }
            }

            string[] slotMapLabels = FixedMapOrder.Select(id => WvwStaticData.MapLabels.TryGetValue(id, out var lbl) ? lbl : "WvW").ToArray();
            string GuildTeam(string guildId) => byGuild[guildId].FirstOrDefault(s => s.HasValue)?.Item2.Owner ?? "Neutral";

            var ordered = byGuild.Keys.OrderByDescending(id => guildMostRecent[id]).ToList();
            var myColorGuilds = ordered.Where(id => GuildTeam(id).Equals(_ctx.MyTeamColor, StringComparison.OrdinalIgnoreCase)).ToList();
            var otherGuilds = ordered.Except(myColorGuilds).ToList();

            void BuildColumn(List<string> guildIds, int x)
            {
                int y = 0;
                foreach (var guildId in guildIds)
                {
                    string tag = _ctx.DataService.GuildTagCache.TryGetValue(guildId, out var t) ? t : "?";
                    bool isMine = _ctx.DataService.MyAccountGuilds.ContainsKey(guildId);
                    new WvwCardGuildActivity(tag, isMine, byGuild[guildId], slotMapLabels, guildMostRecentSlot[guildId], _ctx.Router) { Parent = ContentArea, Location = new Point(x, y) };
                    y += WvwCardGuildActivity.CardHeight + 2;
                }
            }
            BuildColumn(otherGuilds, 0);
                BuildColumn(myColorGuilds, WvwCardGuildActivity.CardWidth + 12);
            
        }

        /// <summary>Superseded for Detail Grid by BuildPackedProColumns -- kept in case type-grouped columns (the old Castle/Keep, Tower, Camp, Ruins split) are wanted again for some other view mode.</summary>
        private void BuildTypeColumns(bool useProCard)
        {
            int mapId = _ctx.CurrentMapId;
            if (!WvwCatalogService.ByMap.TryGetValue(mapId, out var mapObjectives)) return;

            int colWidth = ContentArea.Width / 4;

            for (int i = 0; i < TypeColumns.Length; i++)
            {
                var (types, header) = TypeColumns[i];
                if (useProCard && types.Contains(WvwObjectiveType.Ruins)) continue; // CardPro only applies to upgradeable structures.

                var column = new Panel
                {
                    Parent = ContentArea,
                    Location = new Point(i * colWidth, 0),
                    Size = new Point(colWidth - 6, ContentArea.Height),
                    CanScroll = true
                };
                var content = new Panel { Parent = column, Location = Point.Zero, Size = new Point(colWidth - 6, ContentArea.Height) };

                int y = 0;
                new Label { Parent = content, Location = new Point(0, y), Text = header, Size = new Point(content.Width, 16), HorizontalAlignment = HorizontalAlignment.Center, Font = GameService.Content.DefaultFont14, TextColor = Microsoft.Xna.Framework.Color.LightGreen };
                y += 18;

                var objectivesInColumn = SortObjectives(mapObjectives.Where(o => types.Contains(o.Type)).Where(PassesFilter), mapId);

                foreach (var obj in objectivesInColumn)
                {
                    if (useProCard)
                    {
                        var pro = new WvwCardPro(_ctx.DataService, obj.Id, _ctx.Router) { Parent = content, Location = new Point(0, y) };
                        pro.Refresh();
                        _proCards.Add(pro);
                        y += pro.Height + 2;
                    }
                    else if (obj.Type == WvwObjectiveType.Ruins)
                    {
                        var tiny = new WvwCardTiny(_ctx.DataService, obj.Id, _ctx.Router) { Parent = content, Location = new Point(0, y), Width = content.Width - 10 };
                        tiny.Refresh();
                        _tinyCards.Add(tiny);
                        y += tiny.Height + 2;
                    }
                    else
                    {
                        var card = new WvwCardStandard(_ctx.DataService, obj.Id, _ctx.Router) { Parent = content, Location = new Point(0, y), Width = content.Width - 10 };
                        card.Refresh();
                        _standardCards.Add(card);
                        y += card.Height + 2;
                    }
                }

                content.Height = Math.Max(y, column.Height);
            }
        }

        private void RefreshCardsOnly()
        {
            // The Guild view's rows aren't in any of the card lists below, so
            // refreshing "the cards" did nothing for it -- it only ever
            // updated on a view/filter/map change. Rebuild it from the new data.
            if (_ctx.TacState.ViewMode == TacOverviewViewMode.Map)
            {
                RebuildContent();
                return;
            }
            foreach (var c in _standardCards) c.Refresh();
            foreach (var c in _tinyCards) c.Refresh();
            foreach (var c in _proCards) c.Refresh();
        }
    }
}