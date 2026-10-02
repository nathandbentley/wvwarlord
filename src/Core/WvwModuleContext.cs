using System;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using WvWarlord.Api;

namespace WvWarlord.Core
{
    /// <summary>
    /// Bundles the handful of shared references every feature panel needs
    /// (the API manager, the live data service, the chatlink router, and the
    /// currently selected map) so panels aren't threaded through half a dozen
    /// constructor parameters each.
    /// </summary>
    public class WvwModuleContext
    {
        public Gw2ApiManager Gw2ApiManager { get; }
        public WvwLiveDataService DataService { get; }
        public ChatLinkRouter Router { get; }

        private int _currentMapId = 38;
        public int CurrentMapId
        {
            get => _currentMapId;
            set
            {
                if (_currentMapId == value) return;
                _currentMapId = value;
                CurrentMapChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string MyTeamColor => DataService?.MyTeamColor ?? "Neutral";

        public TacOverviewState TacState { get; }

        public event EventHandler CurrentMapChanged;

        public WvwModuleContext(Gw2ApiManager gw2ApiManager, WvwLiveDataService dataService, ChatLinkRouter router, SettingCollection settingsCollection)
        {
            Gw2ApiManager = gw2ApiManager;
            DataService = dataService;
            Router = router;
            // Per SillyHuman ("all these settings should be how things load
            // on reboot") -- TacState used to be a bare field-initializer
            // instance with no persistence at all. Now backed by real
            // SettingEntries (see TacOverviewState itself).
            TacState = new TacOverviewState(settingsCollection);
        }
    }
}