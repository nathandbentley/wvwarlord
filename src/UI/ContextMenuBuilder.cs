using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD.Controls;
using WvWarlord.Api;
using WvWarlord.Core;
using WvWarlord.Models;

namespace WvWarlord.UI
{
    public static class ContextMenuBuilder
    {
        private static readonly Dictionary<string, int> HomeColumnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Blue"] = 3,
            ["Red"] = 1,
            ["Green"] = 2
        };
        private static readonly int[] FixedMapOrder = { 38, 1099, 95, 96 }; // EBG, Red BL, Green BL, Blue BL -- column-index-aligned

        private class Entry
        {
            public string Text;
            public string ChatLink; // null = disabled label/separator
        }

        public static void Attach(CornerIcon icon, WvwModuleContext ctx)
        {
            string lastSignature = null;

            // Builds the entry list first (cheap) and only builds a new
            // ContextMenuStrip when it differs from what's already attached.
            // This used to build and attach a brand-new strip on every 15s
            // data update and every map change, never disposing the old one.
            void Rebuild(object s, EventArgs e)
            {
                var entries = BuildEntries(ctx);
                string signature = string.Join("\n", entries.Select(x => x.Text + "|" + x.ChatLink));
                if (signature == lastSignature) return;
                lastSignature = signature;

                var menu = new ContextMenuStrip();
                foreach (var entry in entries)
                {
                    var item = menu.AddMenuItem(entry.Text);
                    if (entry.ChatLink == null) item.Enabled = false;
                    else
                    {
                        string link = entry.ChatLink;
                        item.Click += (_, __) => _ = ctx.Router.RouteAsync(link);
                    }
                }

                var old = icon.Menu;
                icon.Menu = menu;
                old?.Dispose();
            }

            Rebuild(null, EventArgs.Empty);
            ctx.DataService.DataUpdated += Rebuild;
            ctx.CurrentMapChanged += Rebuild;
        }

        private static List<Entry> BuildEntries(WvwModuleContext ctx)
        {
            var entries = new List<Entry>();
            string myColor = ctx.MyTeamColor;
            var allShortcuts = WvwStaticShortcuts.ForColor(myColor);
            int myHomeColumn = HomeColumnIndex.TryGetValue(myColor, out var col) ? col : 3;

            var groupColumns = new List<int> { myHomeColumn, 0 };
            foreach (int c in new[] { 1, 2, 3 })
                if (c != myHomeColumn) groupColumns.Add(c);

            bool first = true;
            foreach (int column in groupColumns)
            {
                int mapId = FixedMapOrder[column];
                string mapLabel = WvwStaticData.MapLabels.TryGetValue(mapId, out var lbl) ? lbl : "WvW";

                if (!first) entries.Add(new Entry { Text = "──────────" });
                first = false;

                string headerText = column == myHomeColumn ? $"{myColor.ToUpper()} (HOME)" : mapLabel.ToUpper();
                entries.Add(new Entry { Text = $"-- {headerText} --" });

                // Row 1: the static, uncapturable waypoint for this map -- always shown.
                var wpShortcut = allShortcuts.Find(s => s.ColumnIndex == column && s.IsWaypointSlot);
                if (wpShortcut != null)
                    entries.Add(new Entry { Text = wpShortcut.Name, ChatLink = wpShortcut.ChatLink });

                // Row 2: the home keep (EBG / own-home column only) -- only while I currently hold it.
                string shownKeepObjectiveId = null;
                var keepShortcut = allShortcuts.Find(s => s.ColumnIndex == column && s.IsKeepIcon);
                if (keepShortcut != null)
                {
                    string owner = ctx.DataService.LiveStates.TryGetValue(keepShortcut.ObjectiveId, out var st) ? st.Owner : "Neutral";
                    if (owner.Equals(myColor, StringComparison.OrdinalIgnoreCase))
                    {
                        entries.Add(new Entry { Text = keepShortcut.Name, ChatLink = keepShortcut.ChatLink });
                        shownKeepObjectiveId = keepShortcut.ObjectiveId;
                    }
                }

                // Row 3+: any other Keep on this map, mine, Tier 3 -- the dynamic section.
                if (WvwCatalogService.ByMap.TryGetValue(mapId, out var mapObjs))
                {
                    var dynamicKeeps = mapObjs
                        .Where(o => o.Type == Gw2Sharp.WebApi.V2.Models.WvwObjectiveType.Keep && o.Id != shownKeepObjectiveId)
                        .Where(o => ctx.DataService.LiveStates.TryGetValue(o.Id, out var st)
                            && st.Owner.Equals(myColor, StringComparison.OrdinalIgnoreCase)
                            && st.Tier == 3 && st.HasWaypoint)
                        .OrderBy(o => o.Name);

                    foreach (var obj in dynamicKeeps)
                        entries.Add(new Entry { Text = obj.Name, ChatLink = obj.ChatLink });
                }
            }

            return entries;
        }
    }
}
