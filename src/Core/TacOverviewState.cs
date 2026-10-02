using System;
using Blish_HUD.Settings;
using WvWarlord.UI.Features;

namespace WvWarlord.Core
{
    /// <summary>
    /// TacOverview is now two separate docked panels -- TacControlPanel
    /// (view/filter/sort/map buttons) and TacContentPanel (the actual card
    /// grid). This is the shared state between them: the control panel
    /// writes to it, the content panel listens for Changed and re-renders.
    ///
    /// Per SillyHuman ("all these settings should be how things load on
    /// reboot"), View/Filter/Sort are now backed by real SettingEntries
    /// instead of plain fields that reset every reload. Stored as strings
    /// (enum names) rather than the enums themselves -- Point's JSON
    /// round-trip through Blish HUD's settings serializer isn't verified in
    /// this codebase (see round 6/PanelStateController's LastRoamingLocation
    /// note in learnings.md; KeyBinding is the only confirmed-working
    /// non-primitive SettingEntry type here), so this sidesteps needing to
    /// verify a THIRD (enum) type by reusing the already-proven string
    /// pattern instead. A stored value that no longer matches any enum
    /// member (e.g. after a future rename) falls back to the compiled-in
    /// default rather than throwing.
    /// </summary>
    public class TacOverviewState
    {
        private readonly SettingEntry<string> _viewModeSetting;
        private readonly SettingEntry<string> _filterModeSetting;
        private readonly SettingEntry<string> _sortModeSetting;

        private TacOverviewViewMode _viewMode;
        private TacOverviewFilterMode _filterMode;
        private TacOverviewSortMode _sortMode;

        public TacOverviewState(SettingCollection settings)
        {
            _viewModeSetting = settings.DefineSetting("TacOverviewViewMode", TacOverviewViewMode.SingleMap.ToString());
            _filterModeSetting = settings.DefineSetting("TacOverviewFilterMode", TacOverviewFilterMode.All.ToString());
            _sortModeSetting = settings.DefineSetting("TacOverviewSortMode", TacOverviewSortMode.BuildingType.ToString());

            _viewMode = ParseOrDefault(_viewModeSetting.Value, TacOverviewViewMode.SingleMap);
            _filterMode = ParseOrDefault(_filterModeSetting.Value, TacOverviewFilterMode.All);
            _sortMode = ParseOrDefault(_sortModeSetting.Value, TacOverviewSortMode.BuildingType);
        }

        public TacOverviewViewMode ViewMode
        {
            get => _viewMode;
            set { if (_viewMode == value) return; _viewMode = value; _viewModeSetting.Value = value.ToString(); Changed?.Invoke(this, EventArgs.Empty); }
        }

        /// <summary>Inline docked-dropdown change -- current on-screen state only, does not overwrite the persisted default (same Persist pattern as PanelStateController.SetMinimizedLive).</summary>
        public void SetViewModeLive(TacOverviewViewMode value)
        {
            if (_viewMode == value) return;
            _viewMode = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public TacOverviewFilterMode FilterMode
        {
            get => _filterMode;
            set { if (_filterMode == value) return; _filterMode = value; _filterModeSetting.Value = value.ToString(); Changed?.Invoke(this, EventArgs.Empty); }
        }

        /// <summary>Inline docked-dropdown change -- current on-screen state only, does not overwrite the persisted default.</summary>
        public void SetFilterModeLive(TacOverviewFilterMode value)
        {
            if (_filterMode == value) return;
            _filterMode = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public TacOverviewSortMode SortMode
        {
            get => _sortMode;
            set { if (_sortMode == value) return; _sortMode = value; _sortModeSetting.Value = value.ToString(); Changed?.Invoke(this, EventArgs.Empty); }
        }

        /// <summary>Back to the compiled-in defaults (persisted, like the Settings-tab dropdowns).</summary>
        public void ResetToDefaults()
        {
            ViewMode = TacOverviewViewMode.SingleMap;
            FilterMode = TacOverviewFilterMode.All;
            SortMode = TacOverviewSortMode.BuildingType;
        }

        public event EventHandler Changed;

        private static T ParseOrDefault<T>(string stored, T fallback) where T : struct
        {
            return Enum.TryParse(stored, out T parsed) ? parsed : fallback;
        }
    }
}