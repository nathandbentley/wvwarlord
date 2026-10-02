using System;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WvWarlord.Core;
using WvWarlord.Models;
using WvWarlord.UI;

namespace WvWarlord.UI.Features
{
    /// <summary>
    /// TacControlPanel: the left-column controls half of what used to be a
    /// single TacOverview panel -- View / Filter / Sort dropdowns and map
    /// selection buttons. Writes to WvwModuleContext.TacState and
    /// ctx.CurrentMapId; TacContentPanel (docked on the right) listens for
    /// both and re-renders. Sized to fit its own content, not the fixed
    /// 220px-wide/19.5-card-tall console ceiling -- it's a small panel.
    /// </summary>
    public class TacControlPanel : FeaturePanelBase
    {
        private readonly WvwModuleContext _ctx;
        private Dropdown _viewDropdown;
        private Dropdown _filterDropdown;

        public TacControlPanel(WvwModuleContext ctx, Point size, Texture2D windowIcon)
            : base("tac_control", "TAC OVERVIEW", size, windowIcon)
        {
            _ctx = ctx;
            InitializeChrome();
        }

        /// <summary>Settings button lives on UserDebug's title bar only now (see FeaturePanelBase.ShowSettingsButton) -- this panel's flyout section is still built via BuildFlyoutSection below, just opened from there instead.</summary>
        protected override bool ShowSettingsButton => false;

        protected override void BuildContent(Panel contentArea)
        {
            int width = contentArea.Width;
            int y = 4;
            const int margin = 6;
            const int gap = 4;

            // View / Filter stay here for quick access (per feedback) --
            // Sort moved entirely to the settings flyout (see
            // BuildFlyoutSection) along with a second copy of these two, so
            // everything Tac-related is also available in one place there.
            int dropdownWidth = (width - margin * 2 - gap) / 2;
            int dx = margin;

            _viewDropdown = MakeViewDropdown(contentArea, new Point(dx, y), dropdownWidth, persist: false);
            dx += dropdownWidth + gap;

            _filterDropdown = MakeFilterDropdown(contentArea, new Point(dx, y), dropdownWidth, persist: false);

            y += 30;

            // Map buttons: one row, side by side (WvwStaticData.MapLabels is
            // exactly 4 entries -- EBG + the 3 borderlands). No caption row.
            int mapCount = Math.Max(1, WvwStaticData.MapLabels.Count);
            int mapBtnWidth = (width - margin * 2 - gap * (mapCount - 1)) / mapCount;
            int mx = margin;

            foreach (var mapKvp in WvwStaticData.MapLabels)
            {
                var btn = new StandardButton { Parent = contentArea, Text = AbbreviateMapLabel(mapKvp.Value), Location = new Point(mx, y), Size = new Point(mapBtnWidth, 24), BasicTooltipText = mapKvp.Value };
                btn.Click += (s, e) => _ctx.CurrentMapId = mapKvp.Key;
                mx += mapBtnWidth + gap;
            }

            y += 30;
            ResizeExpandedHeight(y + 28); // + title bar
        }

        private Dropdown MakeViewDropdown(Panel parent, Point location, int width, bool persist)
        {
            var dd = new Dropdown { Parent = parent, Location = location, Size = new Point(width, 24), BasicTooltipText = "View" };
            foreach (var m in Enum.GetValues(typeof(TacOverviewViewMode)).Cast<TacOverviewViewMode>()) dd.Items.Add(FormatEnum(m.ToString()));
            dd.SelectedItem = FormatEnum(_ctx.TacState.ViewMode.ToString());
            // persist: true for the flyout copy (the "real" setting), false
            // for the inline docked copy -- per SillyHuman, the docked
            // dropdown should only change current on-screen state, not what
            // loads next reboot. Same Persist pattern as
            // PanelStateController.SetMinimizedLive.
            dd.ValueChanged += (s, e) =>
            {
                var value = ParseEnum<TacOverviewViewMode>(dd.SelectedItem);
                if (persist) _ctx.TacState.ViewMode = value;
                else _ctx.TacState.SetViewModeLive(value);
            };
            // Keeps this instance in sync when the OTHER copy (inline vs.
            // flyout) changes the value instead. TacOverviewState.Changed is
            // a plain EventHandler (EventArgs.Empty, no PropertyName to
            // check), so this just unconditionally re-syncs -- harmless
            // no-op when the value already matches.
            EventHandler sync = (s, e) =>
            {
                string display = FormatEnum(_ctx.TacState.ViewMode.ToString());
                if (dd.SelectedItem != display) dd.SelectedItem = display;
            };
            _ctx.TacState.Changed += sync;
            // Flyout copies are rebuilt every time Settings opens -- drop the
            // subscription when the flyout closes (the docked copy lives on).
            if (persist) SettingsFlyoutBuilder.RegisterCleanup(() => _ctx.TacState.Changed -= sync);
            return dd;
        }

        private Dropdown MakeFilterDropdown(Panel parent, Point location, int width, bool persist)
        {
            var dd = new Dropdown { Parent = parent, Location = location, Size = new Point(width, 24), BasicTooltipText = "Filter" };
            foreach (var m in Enum.GetValues(typeof(TacOverviewFilterMode)).Cast<TacOverviewFilterMode>()) dd.Items.Add(FormatEnum(m.ToString()));
            dd.SelectedItem = FormatEnum(_ctx.TacState.FilterMode.ToString());
            dd.ValueChanged += (s, e) =>
            {
                var value = ParseEnum<TacOverviewFilterMode>(dd.SelectedItem);
                if (persist) _ctx.TacState.FilterMode = value;
                else _ctx.TacState.SetFilterModeLive(value);
            };
            EventHandler sync = (s, e) =>
            {
                string display = FormatEnum(_ctx.TacState.FilterMode.ToString());
                if (dd.SelectedItem != display) dd.SelectedItem = display;
            };
            _ctx.TacState.Changed += sync;
            // Flyout copies are rebuilt every time Settings opens -- drop the
            // subscription when the flyout closes (the docked copy lives on).
            if (persist) SettingsFlyoutBuilder.RegisterCleanup(() => _ctx.TacState.Changed -= sync);
            return dd;
        }

        private Dropdown MakeSortDropdown(Panel parent, Point location, int width)
        {
            var dd = new Dropdown { Parent = parent, Location = location, Size = new Point(width, 24), BasicTooltipText = "Sort" };
            foreach (var m in Enum.GetValues(typeof(TacOverviewSortMode)).Cast<TacOverviewSortMode>()) dd.Items.Add(FormatEnum(m.ToString()));
            dd.SelectedItem = FormatEnum(_ctx.TacState.SortMode.ToString());
            dd.ValueChanged += (s, e) => _ctx.TacState.SortMode = ParseEnum<TacOverviewSortMode>(dd.SelectedItem);
            EventHandler sync = (s, e) =>
            {
                string display = FormatEnum(_ctx.TacState.SortMode.ToString());
                if (dd.SelectedItem != display) dd.SelectedItem = display;
            };
            _ctx.TacState.Changed += sync;
            SettingsFlyoutBuilder.RegisterCleanup(() => _ctx.TacState.Changed -= sync);
            return dd;
        }

        /// <summary>
        /// Builds this panel's section of the shared settings flyout: a
        /// second copy of View and Filter (kept in sync with the inline
        /// ones above via TacState.Changed), plus Sort, which lives ONLY
        /// here now. Each row is label-left/control-right ("unstacked"
        /// rather than three dropdowns crammed side by side) -- there's
        /// room for that in the flyout that there isn't in the narrow
        /// docked column.
        /// </summary>
        public void BuildFlyoutSection(Panel parent, int x, int width, ref int y)
        {
            int controlWidth = width / 2 - 16;

            SettingsFlyoutBuilder.AddLabeledRow(parent, x, width, ref y, "View", MakeViewDropdown(parent, Point.Zero, controlWidth, persist: true));
            SettingsFlyoutBuilder.AddLabeledRow(parent, x, width, ref y, "Filter", MakeFilterDropdown(parent, Point.Zero, controlWidth, persist: true));
            SettingsFlyoutBuilder.AddLabeledRow(parent, x, width, ref y, "Sort", MakeSortDropdown(parent, Point.Zero, controlWidth));
        }

        private static string AbbreviateMapLabel(string label) => label.Replace(" Map", "");
        private static string FormatEnum(string raw) => System.Text.RegularExpressions.Regex.Replace(raw, "(?<!^)([A-Z])", " $1");
        private static T ParseEnum<T>(string display) where T : struct => (T)Enum.Parse(typeof(T), display.Replace(" ", ""));
    }
}