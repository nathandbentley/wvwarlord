using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Blish_HUD.Settings.UI.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using WvWarlord.Api;
using WvWarlord.Core;
using WvWarlord.UI;
using WvWarlord.UI.Features;
using WvWarlord.Models;
using Blish_HUD.Content;
using Blish_HUD.Graphics.UI;

namespace WvWarlord
{
    [Export(typeof(Module))]
    public class WvWarlordModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<WvWarlordModule>();

        private readonly Gw2ApiManager _gw2ApiManager;
        private readonly ContentsManager _contentsManager;
        private readonly SettingCollection _settingsCollection;

        private SettingEntry<string> _settingApiKey;
        private SettingEntry<bool> _settingResetToDefaults;
        private SettingEntry<KeyBinding> _windowHotkeySetting;
        private SettingEntry<bool> _settingSkipRoute1;
        private SettingEntry<bool> _settingUseDevDebugWindow;
        private SettingEntry<string> _settingChatLinkRoute;
        private SettingEntry<bool> _settingAutoSend;

        private WvwModuleContext _ctx;
        private ChatLinkRouter _router;
        private WvwLiveDataService _dataService;
        private readonly GuildEmblemService _guildEmblemService = new GuildEmblemService();

        private AsyncTexture2D _iconTexture;
        private CornerIcon _cornerIcon;
        private DevDebugWindow _devDebugWindow;
        private SettingsFlyoutBuilder _settingsFlyout;
        private ScarMapHandoffService _scarMapHandoff;

        // Reused every frame -- Update used to allocate a new array each tick.
        private readonly FeaturePanelBase[] _tickPanels = new FeaturePanelBase[4];

        private double _runningTime = 0;
        private bool _isUpdating = false;

        // The one and only console window -- owns TacControlPanel,
        // TacContentPanel, QuickTravelPanel, GuildClaimsPanel, and
        // UserDebugContent (see WvWarlordMainWindow). The old StandardWindow
        // console + its own duplicate set of these panels has been retired;
        // that dead-code/dual-window state is what made "every tab" and the
        // old sizing/background rules hard to bring over intact.
        private WvWarlordMainWindow _mainWindow;

        [ImportingConstructor]
        public WvWarlordModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters)
        {
            _gw2ApiManager = moduleParameters.Gw2ApiManager;
            _contentsManager = moduleParameters.ContentsManager;
            _settingsCollection = moduleParameters.SettingsManager.ModuleSettings;
        }

        public override IView GetSettingsView()
           => new NativeSettingsHostView(_settingApiKey, _settingResetToDefaults, _settingUseDevDebugWindow, _windowHotkeySetting, OpenSettingsTab);

        private void OpenSettingsTab()
        {
            if (_mainWindow == null) return;
            _mainWindow.Show();
            _mainWindow.SelectedTab = _mainWindow.TabSettings;
        }
        internal class NativeSettingsHostView : View
        {
            private readonly SettingEntry<string> _apiKey;
            private readonly SettingEntry<bool> _reset;
            private readonly SettingEntry<bool> _debug;
            private readonly SettingEntry<KeyBinding> _hotkey;
            private readonly Action _openSettings;

            public NativeSettingsHostView(SettingEntry<string> apiKey, SettingEntry<bool> reset,
                SettingEntry<bool> debug, SettingEntry<KeyBinding> hotkey, Action openSettings)
            {
                _apiKey = apiKey; _reset = reset; _debug = debug; _hotkey = hotkey; _openSettings = openSettings;
            }

            protected override void Build(Container buildPanel)
            {
                var open = new StandardButton { Parent = buildPanel, Text = "Open WvWarlord Settings", Width = 220, Location = new Point(10, 10) };
                open.Click += (s, e) => _openSettings();

                new Label { Parent = buildPanel, Text = "GW2 API Access Key", AutoSizeWidth = true, Location = new Point(10, 55) };
                var key = new TextBox { Parent = buildPanel, Text = _apiKey.Value, Width = 460, Location = new Point(10, 75) };
                key.TextChanged += (s, e) => _apiKey.Value = key.Text;

                // Reset + Dev on the same row
                var reset = new StandardButton { Parent = buildPanel, Text = "Reset to Defaults", Width = 160, Location = new Point(10, 120) };
                reset.Click += (s, e) => { _reset.Value = false; _reset.Value = true; }; // force a change so ApplyDefaults fires
                var debug = new Checkbox { Parent = buildPanel, Text = "Enable Dev Debug Window", Checked = _debug.Value, Location = new Point(190, 124) };
                debug.CheckedChanged += (s, e) => _debug.Value = debug.Checked;

                // Blish HUD's own setting view for a KeyBinding (what the
                // default settings page uses) -- it labels the row from the
                // setting's display name ("Open Main Window") and handles
                // reassignment. A bare KeybindingAssigner here had no name and
                // couldn't be re-bound.
                var hotkeyHost = new ViewContainer { Parent = buildPanel, Location = new Point(10, 165), Size = new Point(460, 32) };
                hotkeyHost.Show(SettingView.FromType(_hotkey, 460));
            }
        }
        protected override void DefineSettings(SettingCollection settings)
        {
            _settingApiKey = settings.DefineSetting(
                "WvWarlordApiKey", "",
                () => "GW2 API Access Key",
                () => "Required to resolve your WvW Team ID via account/wvw.");

            _windowHotkeySetting = settings.DefineSetting(
                "WindowToggleHotkey",
                new KeyBinding(ModifierKeys.Shift, Keys.B),
                () => "Open Main Window",
                () => "Opens or closes the main WvWarlord window (default Shift + B).");

            _settingResetToDefaults = settings.DefineSetting(
                "ResetToDefaults", false,
                () => "Reset to Defaults",
                () => "Check to immediately reset every setting below (except your API key) back to default.");

            _settingUseDevDebugWindow = settings.DefineSetting(
                "UseDevDebugWindow", false,
                () => "Enable Dev Debug Window",
                () => "Enable diagnostics window (call counters + raw log).");

            _settingChatLinkRoute = settings.DefineSetting(
                "ChatLinkRoute", "ClipboardOnly",
                () => "ChatLink Route (Ping Process)",
                () => "How clicking a card / using the Ping process delivers its chatlink.");

            _settingAutoSend = settings.DefineSetting(
                "AutoSend", false,
                () => "Auto-Send",
                () => "Appends a final Enter after the chatlink is pasted, for routes that type into chat.");

            // Own key: this used to be "AutoSend", the same key as Auto-Send
            // above, so both names pointed at one stored value and turning
            // Auto-Send on also switched Route 1 off.
            _settingSkipRoute1 = settings.DefineSetting("SkipRoute1", false);

            _settingSkipRoute1.SettingChanged += (s, e) => ApiFallbackExecutor.SkipRoute1Globally = e.NewValue;
            _settingChatLinkRoute.SettingChanged += (s, e) => { if (_router != null) _router.CurrentRoute = ParseStoredRoute(e.NewValue); };
            _settingAutoSend.SettingChanged += (s, e) => { if (_router != null) _router.AutoSend = e.NewValue; };
            _settingResetToDefaults.SettingChanged += (s, e) => { if (e.NewValue) ApplyDefaults(); };
        }

        private static ChatLinkRoute ParseStoredRoute(string stored)
        {
            return Enum.TryParse<ChatLinkRoute>(stored, out var route) ? route : ChatLinkRoute.ClipboardOnly;
        }

        /// <summary>
        /// Fired by checking ResetToDefaults. Applies every default value
        /// "as if you'd set it there yourself" -- kept as a flat list of
        /// actions (SettingEntry doesn't expose a way to read its own
        /// default back out, and SettingCollection doesn't confirm a public
        /// way to enumerate its entries, so this avoids guessing at either).
        /// Add one line here for any new setting that should reset too.
        /// Everything except the API key is reset: module settings here,
        /// each panel's Enabled/Minimized/Popped Out/Stay in Battle (via its
        /// PanelStateController, which PanelSettingsBinder persists),
        /// Tac Overview View/Filter/Sort, the Quick Travel filter, and the
        /// ScarMap filter + Auto-Destination.
        /// </summary>
        private void ApplyDefaults()
        {
            var resets = new Action[]
            {
                () => { var hk = _windowHotkeySetting.Value; hk.ModifierKeys = ModifierKeys.Shift; hk.PrimaryKey = Keys.B; }, // edit in place: replacing the object would orphan the Activated subscription
                () => _settingSkipRoute1.Value = false,
                () => _settingUseDevDebugWindow.Value = false,
                () => _settingChatLinkRoute.Value = "ClipboardOnly",
                () => _settingAutoSend.Value = false,
            };
            foreach (var reset in resets) reset();

            if (_mainWindow != null)
            {
                foreach (var panel in new FeaturePanelBase[] { _mainWindow.TacControl, _mainWindow.TacContent, _mainWindow.QuickTravel, _mainWindow.GuildClaims })
                {
                    if (panel == null) continue;
                    var sc = panel.StateController;
                    sc.Roaming = false;
                    sc.Enabled = true;
                    sc.Minimized = false;
                    sc.Battle = false;
                }
                _mainWindow.QuickTravel?.ResetToDefaults();
            }
            _ctx?.TacState.ResetToDefaults();
            _scarMapHandoff?.ResetToDefaults();

            _settingResetToDefaults.Value = false;
        }

        protected override void Initialize()
        {
            ApiFallbackExecutor.SkipRoute1Globally = _settingSkipRoute1?.Value ?? false;
            GameService.Gw2Mumble.CurrentMap.MapChanged += OnMapChanged;
        }

        protected override async Task LoadAsync()
        {
            // Corner icon (Blish HUD's own top bar, where ContextMenuBuilder
            // attaches the right-click menu) -- AssetID 358416 per
            // SillyHuman, confirmed as the intended asset, not a mistake to
            // revert (round 10 had this backwards). wvw_icon.png kept here
            // commented out as the prior embedded fallback, not the target.
            _iconTexture = AsyncTexture2D.FromAssetId(358416);
            //_iconTexture = _contentsManager.GetTexture("wvw_icon.png");
            _router = new ChatLinkRouter
            {
                CurrentRoute = ParseStoredRoute(_settingChatLinkRoute.Value),
                AutoSend = _settingAutoSend.Value
            };

            _dataService = new WvwLiveDataService(_gw2ApiManager);
            _dataService.SetApiKey(_settingApiKey.Value);
            _dataService.PreloadCompleted += (s, e) =>
            {
                _router.AccountNameForWhisper = _dataService.MyAccountName;
                _mainWindow.BuildFeaturePanelsOnce();
                _ = ApplyGuildEmblemIconAsync(); // fire-and-forget: falls back to the default icon on any failure
            };

            _ctx = new WvwModuleContext(_gw2ApiManager, _dataService, _router, _settingsCollection);

            // ScarMap is a standalone module now -- this is what's left of it
            // here: the Filter/Auto-Destination settings plus the send logic,
            // wired straight into the router the same way the panel used to
            // wire itself (see ScarMapTarget's doc comment).
            _scarMapHandoff = new ScarMapHandoffService(_ctx, _settingsCollection);
            _router.ScarMapTarget = _scarMapHandoff.SendChatLink;

            // Objective/upgrade catalogs are public endpoints -- load regardless of API key state.
            await Task.WhenAll(
                WvwCatalogService.LoadAsync(_gw2ApiManager),
                WvwUpgradeCatalogService.LoadAsync(_gw2ApiManager));

            foreach (var mapId in WvwStaticData.MapLabels.Keys)
            {
                var rect = await WvwCatalogService.GetMapRectAsync(_gw2ApiManager, mapId);
                if (rect.HasValue)
                {
                    var rectData = rect.Value;

                    // 1. Calculate raw center from continent bounds
                    float centerX = (rectData.continentRect.Left + rectData.continentRect.Right) / 2f;
                    float centerY = (rectData.continentRect.Top + rectData.continentRect.Bottom) / 2f;

                    // 2. Invert Y to convert screen-space bounds to Cartesian space
                    var mapCenter = new Vector2(centerX, -centerY);

                    // 3. Normalize objectives to center (0,0) with inverted Y
                    if (WvwCatalogService.ByMap.TryGetValue(mapId, out var objectives))
                    {
                        foreach (var obj in objectives)
                        {
                            var invertedObjCoord = new Vector2(obj.Coord.X, -obj.Coord.Y);
                            obj.LocalCoord = invertedObjCoord - mapCenter;
                        }
                    }
                }
            }
           
            if (_windowHotkeySetting?.Value != null)
            {
                _windowHotkeySetting.Value.Enabled = true;
                _windowHotkeySetting.Value.Activated += OnHotkeyActivated;
                bool isOnWvwMap = WvwStaticData.MapLabels.ContainsKey(GameService.Gw2Mumble.CurrentMap.Id);
            }

            _devDebugWindow = new DevDebugWindow(_ctx, TriggerModuleReload, _iconTexture, _settingSkipRoute1);

            // NOTE (round 4): tried switching this to the documented
            // GameService.Content.GetTexture("controls/window/155985") --
            // SillyHuman confirmed that renders the missing/error texture
            // in-game for this asset, unlike the docs' own sample. So
            // DatAssetCache.GetTextureFromAssetId(155985) below is the
            // version that actually works here and is intentional, not a
            // regression -- despite AsyncTexture2D.FromAssetId(155997)
            // having been the wrong registry for THAT asset back in round 1.
            // Bottom line: don't re-"fix" this back to GetTexture() again
            // without a fresh in-game confirmation.
            var windowBg = GameService.Content.DatAssetCache.GetTextureFromAssetId(155985);


            _mainWindow = new WvWarlordMainWindow(_ctx, AsyncTexture2D.FromAssetId(155985), _iconTexture, _settingsCollection, retryPreload: () => _dataService.PreloadAsync());

            _settingsFlyout = new SettingsFlyoutBuilder(
                _settingApiKey, _settingUseDevDebugWindow, _settingChatLinkRoute, _settingAutoSend,
                panelsProvider: () =>
                {
                    var list = new List<(string, PanelStateController)>();
                    if (_mainWindow.TacControl != null) list.Add(("Tac Overview", _mainWindow.TacControl.StateController));
                    if (_mainWindow.TacContent != null) list.Add(("Tac Overview Content", _mainWindow.TacContent.StateController));
                    if (_mainWindow.QuickTravel != null) list.Add(("Quick Travel", _mainWindow.QuickTravel.StateController));
                    if (_mainWindow.GuildClaims != null) list.Add(("Guild Claims", _mainWindow.GuildClaims.StateController));
                    // ScarMap is a standalone module now -- no StateController
                    // (Enabled/Minimized/Roaming) here anymore to re-enable.
                    return list;
                },
                tacControlPanelProvider: () => _mainWindow.TacControl,
                tacContentPanelProvider: () => _mainWindow.TacContent,
                quickTravelPanelProvider: () => _mainWindow.QuickTravel,
                scarMapProvider: () => _scarMapHandoff,
                devDebugToggle: () => _devDebugWindow.Toggle(),
                applyAction: () => { _ = _dataService.PreloadAsync(); }, // fire-and-forget, discarded explicitly (CS4014)
                hostProvider: () => _mainWindow.SettingsHost,
                // Only the flyout's OWN in-panel Close button should navigate
                // (back to "None") -- ordinary tab switches away from
                // Settings tear the flyout down via SettingsTabDeactivated
                // -> ForceClose() below instead, which does NOT navigate,
                // since the window already knows which tab it's going to.
                setSettingsMode: on => { if (!on) _mainWindow.SelectedTab = _mainWindow.TabNone; });

            _mainWindow.SettingsTabActivated += (s, e) => _settingsFlyout.Toggle();
            _mainWindow.SettingsTabDeactivated += (s, e) => _settingsFlyout.ForceClose();

            _cornerIcon = new CornerIcon
            {
                IconName = "WvWarlord",
                Icon = _iconTexture,
                Priority = 5
            };
            _cornerIcon.Click += (s, e) => ToggleConsole();
            ContextMenuBuilder.Attach(_cornerIcon, _ctx);

            // Per SillyHuman: the main console shouldn't auto-open on Blish
            // HUD startup -- only popped-out panels should (they already do,
            // independently, via PanelSettingsBinder restoring each
            // PanelStateController.Roaming from its own persisted setting).
            // The window is still fully built and ready; it just stays
            // hidden until the corner icon or hotkey calls ToggleConsole().

            try
            {
                Logger.Info("Starting WvWarlord data preload...");
                await _dataService.PreloadAsync();
                Logger.Info("WvWarlord data preload finished successfully.");
                // Resolve the real starting map now that MyTeamColor is
                // known -- fixes both starting already off a WvW map (used
                // to default to EBG/38 regardless of color) and starting
                // already on one (used to ignore the real map entirely
                // until the player's first map change).
                ResolveCurrentMapId(GameService.Gw2Mumble.CurrentMap.Id);
            }
            catch (Exception ex)
            {
                Logger.Error($"CRITICAL: PreloadAsync failed! Message: {ex.Message} | StackTrace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// icon to the account's WvW guild emblem, built once
        /// (GuildEmblemService caches it) -- no timer, no randomness. "WvW
        /// guild" is MyWvwGuildId, resolved from account/wvw's guild field
        /// (that field was already being captured, just never used for
        /// anything -- see WvwLiveDataService.PreloadAsync). Falls back to
        /// leaving the default WvWarlord icon in place if the account has no
        /// WvW guild set, or the build fails for any reason.
        /// </summary>
        private async Task ApplyGuildEmblemIconAsync()
        {
            if (string.IsNullOrEmpty(_dataService.MyWvwGuildId) || _dataService.MyWvwGuildId == "None") return;
            if (!_dataService.MyAccountGuilds.TryGetValue(_dataService.MyWvwGuildId, out var guild)) return;

            var emblemTexture = await _guildEmblemService.GetOrBuildAsync(guild);
            if (emblemTexture != null && _mainWindow != null)
            {
                _mainWindow.Emblem = emblemTexture;
            }
        }


        private void OnHotkeyActivated(object sender, EventArgs e) => ToggleConsole();

        private void ToggleConsole()
        {
            if (_mainWindow == null) return;
            if (_mainWindow.Visible) _mainWindow.Hide();
            else _mainWindow.Show();
        }

        private void OnMapChanged(object sender, ValueEventArgs<int> e)
        {
            if (_ctx == null) return;
            ResolveCurrentMapId(e.Value);
        }

        /// <summary>
        /// Shared by OnMapChanged and the post-preload startup call below --
        /// previously this logic only ran reactively off the MapChanged
        /// event, so a module load that started while ALREADY off a WvW map
        /// (no change ever fires) left CurrentMapId stuck at
        /// WvwModuleContext's raw field default (38/EBG) regardless of the
        /// player's actual team color, instead of "our color" per
        /// SillyHuman.
        /// </summary>
        private void ResolveCurrentMapId(int rawMapId)
        {
            if (WvwStaticData.MapLabels.ContainsKey(rawMapId))
            {
                _ctx.CurrentMapId = rawMapId;
            }
            else
            {
                string color = _ctx.MyTeamColor;
                _ctx.CurrentMapId = color == "Blue" ? 96 : color == "Green" ? 95 : color == "Red" ? 1099 : 38;
            }
        }

        private async void TriggerModuleReload()
        {
            try
            {
                ApiCallTracker.Log("Manual module reload triggered from DevDebug.");
                await Task.WhenAll(
                    WvwCatalogService.LoadAsync(_gw2ApiManager),
                    WvwUpgradeCatalogService.LoadAsync(_gw2ApiManager));
                await _dataService.WipeAndReloadAsync();
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"Manual reload failed: {ex.Message}");
            }
        }

        protected override void Update(GameTime gameTime)
        {
            RoamingPanel.TickAllDrags();

            bool isOnWvwMap = WvwStaticData.MapLabels.ContainsKey(GameService.Gw2Mumble.CurrentMap.Id);
            bool isWindowOpen = _mainWindow != null && _mainWindow.Visible;
            bool suppressAmbientWindows = !isOnWvwMap && !isWindowOpen;

            if (_mainWindow != null)
            {
                _tickPanels[0] = _mainWindow.TacControl;
                _tickPanels[1] = _mainWindow.TacContent;
                _tickPanels[2] = _mainWindow.QuickTravel;
                _tickPanels[3] = _mainWindow.GuildClaims;
                foreach (var panel in _tickPanels)
                {
                    if (panel == null) continue;
                    panel.StateController.TickCombatState();
                    panel.StateController.ApplyAmbientVisibility(suppressAmbientWindows);
                }
            }

            // ScarMap is a standalone module now -- this just runs the
            // Auto-Destination pick-and-forward check (throttled internally).
            _scarMapHandoff?.Tick(gameTime);

            // Keeps PlayerDistance sort's shown numbers live as the player
            // walks around -- see TacContentPanel.Tick for why this can't
            // just piggyback on RebuildContent/DataUpdated. Throttled
            // internally same as ScarMap's Tick above.
            _mainWindow?.TacContent?.Tick(gameTime);

            _runningTime += gameTime.ElapsedGameTime.TotalMilliseconds;
            if (_runningTime < 15000) return;

            _runningTime = 0;
            if (_isUpdating || _dataService == null) return;

            // SMART GATE: reuses isOnWvwMap/isWindowOpen computed above --
            // if you're NOT on a WvW map AND the UI console is closed,
            // there's nothing that needs fresh data, so skip the API call.
            if (suppressAmbientWindows)
            {
                // Optional: reset the stale tracker color so it doesn't open bright red later
                return;
            }
     

            _isUpdating = true;
            Task.Run(async () =>
            {
                try { await _dataService.ExecutePeriodicUpdateLoopAsync(); }
                finally { _isUpdating = false; }
            });
        }

        protected override void Unload()
        {
            GameService.Gw2Mumble.CurrentMap.MapChanged -= OnMapChanged;
            if (_windowHotkeySetting?.Value != null) _windowHotkeySetting.Value.Activated -= OnHotkeyActivated;

            _settingsFlyout?.Dispose();
            _cornerIcon?.Dispose();
            // _iconTexture is a shared DatAssetCache texture -- Blish HUD owns
            // it, so it's deliberately not disposed here.

            // Popped-out panels are parented to the screen, not the main
            // window, so disposing the window alone could leave them behind.
            if (_mainWindow != null)
            {
                foreach (var panel in new FeaturePanelBase[] { _mainWindow.TacControl, _mainWindow.TacContent, _mainWindow.QuickTravel, _mainWindow.GuildClaims })
                    panel?.StateController.Dispose();
            }
            _mainWindow?.Dispose();
            _devDebugWindow?.Dispose();
            _guildEmblemService.Dispose();
            ApiFallbackExecutor.SkipRoute1Globally = false;
        }
    }
}