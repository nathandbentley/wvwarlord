using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WvWarlord.Core;
using WvWarlord.Models;
using WvWarlord.UI.Cards;
using WvWarlord.UI.Features;

namespace WvWarlord.UI
{
    /// <summary>
    /// GW2-style tabbed console -- replaces the old StandardWindow console
    /// that used to live in WvWarlordModule (BuildConsole/_consoleWindow;
    /// see git history). That window and its duplicate panel instances have
    /// been retired: this window is now the ONLY place TacControlPanel,
    /// QuickTravelPanel, GuildClaimsPanel, UserDebugContent, and
    /// TacContentPanel get constructed.
    ///
    /// Each of those is built exactly ONCE (BuildFeaturePanelsOnce, called
    /// from WvWarlordModule after preload succeeds).
    ///
    /// ROOT CAUSE, confirmed round 4c by diffing against an old (working)
    /// copy of this file: controls parented directly to the window itself
    /// (Parent = this) DO NOT RENDER AT ALL on this Blish HUD version,
    /// despite existing in the Children tree, having a non-zero size, and
    /// TabbedWindow2's own docs describing contentRegion as "where the
    /// view / child controls will be displayed." The old file never parents
    /// anything to the window directly -- every control is parented into
    /// the Container that View.Build(Container buildPanel) receives, and
    /// that version renders fine. So buildPanel (not the window) is
    /// apparently the only thing whose child tree Blish HUD actually
    /// composites into the visible content area for a TabbedWindow2 tab.
    ///
    /// Fix: the persistent panels are still built exactly once (so
    /// PanelSettingsBinder only ever binds each PanelId once and
    /// Enabled/Minimized/Roaming/Battle state survives tab switches -- the
    /// actual problem the old "every tab gets its own instances" design
    /// had), but each tab's View now explicitly reparents them into ITS
    /// buildPanel on Build() via ReparentShellInto, instead of leaving them
    /// parented to the window forever. This does reintroduce the ordering
    /// risk noted below (previous tab's buildPanel disposal vs. next tab's
    /// Build() -- unclear which runs first), but that's a "might misbehave
    /// on fast tab-switching" risk, not "nothing ever renders" -- worth
    /// taking to get a working window. If controls go missing/get disposed
    /// specifically right after switching tabs, that ordering is the next
    /// thing to chase.
    /// </summary>
    public class WvWarlordMainWindow : TabbedWindow2
    {
        // ---- Left column layout, carried over from the old console ----
        // Content width + assumed scrollbar width, same pattern as
        // TacContentPanel.MapColumnWidth -- ScrollbarWidthAssumption is
        // still an unverified guess at this Blish HUD version's scrollbar
        // width (tune alongside TacContentPanel.ScrollbarWidthAssumption).
        private const int LeftColumnContentWidth = 250;
        private const int ScrollbarWidthAssumption = 20;
        private const int LeftColumnWidth = LeftColumnContentWidth + ScrollbarWidthAssumption; // 270

        // Height "ceiling" for the whole left column (scrollable stack +
        // fixed control block below it) -- keeping this constant is what
        // makes the console's height semi-stable regardless of how many
        // claims/etc. are showing; the scrollable part just scrolls
        // internally once content exceeds it instead of growing the window.
        // NOTE: this reuses the old shipped value (20 cards, ~720px) --
        // TacControlPanel.cs has a stray doc comment saying "19.5-card-tall"
        // instead. If 720 looks too tall/short now that it's paired with
        // the tab strip, change LeftColumnCards below; nothing else needs
        // to change.
        private const int CardHeightUnit = 36;
        private const float LeftColumnCards = 20f;
        private const int LeftColumnHeight = (int)(LeftColumnCards * CardHeightUnit); // ~720

        // The fixed, non-scrolling block pinned to the bottom of the left
        // column that hosts UserDebug -- this is the panel that was missing
        // above UserDebug in the tabbed rebuild (UserDebug had been just
        // another FlowPanel child, scrolling along with everything else
        // instead of staying put).
        private const int ControlPanelHeight = 108 + 8; // UserDebugContent.Height + padding
        private const int ColumnGap = 8;

        // Chrome padding: the difference between this window's own
        // windowRegion and contentRegion rectangles below (900-820=80
        // width, 630-600=30 height) -- reused directly from those numbers,
        // not a new guess, so Width/Height set below land on the same
        // outer-window-to-content-area ratio the constructor already uses.
        private const int ChromeWidthPadding = 80;
        private const int ChromeHeightPadding = 30;

        // "CardTiny" per SillyHuman's sizing request -- the actual unit is
        // WvwCardTiny.CardHeight (26), referenced directly (not
        // re-guessed) so this stays in sync if that card's height ever
        // changes.
        private const int CardTinyHeight = WvwCardTiny.CardHeight; // 26

        // TacOverview (the right-column content height passed to
        // TacContentPanel) needs to be half a CardTiny taller than the left
        // column, then rounded up to the next multiple of 10:
        // 720 + 13 = 733 -> 740.
        private static readonly int TacOverviewHeight = RoundUpToNextTen(LeftColumnHeight + CardTinyHeight / 2)+32;

        // The main window itself needs to end up 2.5 CardTinys taller than
        // whatever the content-driven formula in ResizeToFitContent would
        // otherwise give it, then rounded up to the next multiple of 10 --
        // applied as a flat bonus added just before that final rounding.
        private const int MainWindowHeightBonus = 2 * CardTinyHeight + CardTinyHeight / 2; // 65

        private static int RoundUpToNextTen(int value) => ((value + 9) / 10) * 10;

        private readonly WvwModuleContext _ctx;
        private readonly AsyncTexture2D _iconTexture;
        private readonly SettingCollection _settingsCollection;
        private readonly Func<Task> _retryPreload;

        private bool _featurePanelsBuilt;

        // Persistent left column: a fixed-height CanScroll viewport
        // (_leftViewport) whose content (_leftContent) grows taller than it
        // once panels are stacked -- same viewport/content split as
        // TacContentPanel's columns and the old console, since CanScroll
        // does nothing if the content never exceeds the viewport's bounds.
        private Panel _leftViewport;
        private Panel _leftContent;
        private Label _bodyPlaceholder;
        private Panel _controlBlock;
        private Panel _mapSecondaryBackground;
        private Panel _columnDivider;
        private readonly List<FeaturePanelBase> _leftColumnPanels = new List<FeaturePanelBase>();

        public TacControlPanel TacControl { get; private set; }
        public QuickTravelPanel QuickTravel { get; private set; }
        public GuildClaimsPanel GuildClaims { get; private set; }
        public UserDebugContent UserDebug { get; private set; }
        public TacContentPanel TacContent { get; private set; }

        /// <summary>Persistent host SettingsFlyoutBuilder builds its content into -- WvWarlordModule owns the flyout itself (it needs the module's SettingEntry&lt;&gt; objects), this window just gives it somewhere fixed to render.</summary>
        public Panel SettingsHost { get; private set; }

        public Tab TabNone { get; private set; }
        public Tab TabSingle { get; private set; }
        public Tab TabAll { get; private set; }
        public Tab TabDetail { get; private set; }
        public Tab TabMap { get; private set; }
        public Tab TabSettings { get; private set; }

        /// <summary>Fired when the Settings tab becomes the active tab -- WvWarlordModule's SettingsFlyoutBuilder should build itself into SettingsHost here.</summary>
        public event EventHandler SettingsTabActivated;
        /// <summary>Fired when switching AWAY from the Settings tab to any other tab -- WvWarlordModule's SettingsFlyoutBuilder should tear itself down (without navigating anywhere -- the tab switch already happened).</summary>
        public event EventHandler SettingsTabDeactivated;

        public WvWarlordMainWindow(
            WvwModuleContext ctx,
            AsyncTexture2D windowBackground,
            AsyncTexture2D iconTexture,
            SettingCollection settingsCollection,
            Func<Task> retryPreload)
            : base(windowBackground, new Rectangle(24, 30, 900, 630), new Rectangle(82, 30, 820, 600))
        {
            _ctx = ctx;
            _iconTexture = iconTexture;
            _settingsCollection = settingsCollection;
            _retryPreload = retryPreload;

            Title = "WvWarlord";
            Emblem = _iconTexture;
            // Emblem was set fine when _iconTexture was a synchronously-
            // loaded Texture2D (ContentsManager.GetTexture reading a local
            // embedded file). Now that it's DatAssetCache.GetTextureFromAssetId
            // (genuinely async), this constructor almost certainly runs
            // before that load finishes -- CornerIcon.Icon is itself
            // AsyncTexture2D-typed and live-updates once its texture
            // arrives, so it recovered fine; Emblem apparently doesn't get
            // that same live behavior, so it's stuck with whatever was
            // there at this exact line (likely nothing yet). Re-assigning
            // once the real texture actually swaps in fixes that, and is a
            // harmless no-op if Emblem turns out to update live after all.
            _iconTexture.TextureSwapped += (s, e) => Emblem = _iconTexture;
            SavesPosition = true;
            Id = "wvw_warlord_main_window";
            Parent = GameService.Graphics.SpriteScreen;

            BuildPersistentShell();
            BuildTabs();

            // NOTE: subscribed here (not sooner) deliberately -- see the end
            // of BuildFeaturePanelsOnce for why.
        }

        /// <summary>
        /// Builds everything that doesn't depend on preload having
        /// succeeded: the scroll viewport shell, the placeholder message,
        /// the fixed control block, and UserDebug (whose retry button is
        /// exactly what a failed preload needs). Mirrors the old
        /// BuildConsole/BuildFeaturePanelsOnce split.
        /// </summary>
        private void BuildPersistentShell()
        {
            // NOTE: an earlier round gave these persistent panels an
            // explicit ZIndex (well above buildPanel's default of 0) to try
            // to stop each tab-switch's fresh buildPanel from painting over
            // them / stealing their clicks. That was REVERTED (see
            // overview.md, round 3) because it shipped alongside a worse
            // regression -- content not rendering at all, only window
            // chrome/tabs visible -- and was pulled as the prime suspect.
            // This upload still had the ZIndex additions in it (they were
            // re-added or never actually removed from this copy); removed
            // again here. Left column ordering now relies purely on
            // insertion order (added first = behind), same as
            // _mapSecondaryBackground below.

            // GW2-style frame sitting slightly behind (and larger than) the
            // left column -- only shown on the Map tab, per SillyHuman: the
            // macro map fills the whole right side there and the left
            // column looked bare next to it without a second backdrop.
            _mapSecondaryBackground = new Panel
            {
                Parent = this,
                Location = new Point(-8, -8),
                Size = new Point(LeftColumnWidth + 16, LeftColumnHeight + 16),
                BackgroundTexture = AsyncTexture2D.FromAssetId(155985),
                Visible = false
            };

            _leftViewport = new Panel
            {
                Parent = this,
                Location = new Point(0, 0),
                Size = new Point(LeftColumnWidth, LeftColumnHeight - ControlPanelHeight - 6),
                CanScroll = true,
                BackgroundColor = new Color(10, 10, 12) * 0.75f
            };
            _leftContent = new Panel
            {
                Parent = _leftViewport,
                Location = Point.Zero,
                Size = _leftViewport.Size
            };

            _bodyPlaceholder = new Label
            {
                Parent = _leftContent,
                Size = new Point(LeftColumnContentWidth - 8, 60),
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.Gray,
                Text = "Waiting on preload -- enter your API key in Settings if this doesn't clear on its own."
            };

            _controlBlock = new Panel
            {
                Parent = this,
                Location = new Point(0, LeftColumnHeight - ControlPanelHeight),
                Size = new Point(LeftColumnWidth, ControlPanelHeight),
                BackgroundColor = new Color(10, 10, 12) * 0.9f
            };

            UserDebug = new UserDebugContent(_ctx, _retryPreload, _controlBlock, LeftColumnWidth, y: 4);

            SettingsHost = new Panel
            {
                Parent = this,
                Location = new Point(LeftColumnWidth + ColumnGap, 0),
                Size = new Point(SettingsFlyoutBuilder.PanelWidth, LeftColumnHeight),
                Visible = false
            };

            // Thin gold accent divider between the left column and whatever's
            // showing on the right -- same gold accent color FeaturePanelBase
            // uses for its title text (Color(240,210,150)), just as a solid
            // panel rather than a texture stretched to an unverified aspect
            // ratio. Centered in ColumnGap.
            _columnDivider = new Panel
            {
                Parent = this,
                Location = new Point(LeftColumnWidth + ColumnGap / 2 - 1, 0),
                Size = new Point(2, LeftColumnHeight),
                BackgroundColor = new Color(240, 210, 150) * 0.5f
            };

            ResizeToFitContent();
        }

        /// <summary>
        /// Per the spec: if the API key is missing/invalid, nothing else
        /// should run. The four data-driven feature panels are only ever
        /// constructed once preload actually succeeds; until then only the
        /// shell above and UserDebug's retry button exist. Called once from
        /// WvWarlordModule's PreloadCompleted handler.
        /// </summary>
        public void BuildFeaturePanelsOnce()
        {
            if (_featurePanelsBuilt) return;
            _featurePanelsBuilt = true;

            _bodyPlaceholder.Visible = false;

            TacControl = new TacControlPanel(_ctx, new Point(LeftColumnContentWidth, 220), _iconTexture) { Parent = _leftContent };
            var quickTravelFilterSetting = _settingsCollection.DefineSetting("QuickTravelFilter", QuickTravelFilter.MyMapAndColor.ToString());
            QuickTravel = new QuickTravelPanel(_ctx, new Point(LeftColumnContentWidth, 150), _iconTexture, quickTravelFilterSetting) { Parent = _leftContent };
            GuildClaims = new GuildClaimsPanel(_ctx, new Point(LeftColumnContentWidth, 200), _iconTexture) { Parent = _leftContent };

            // These three no longer have their own gear button
            // (ShowSettingsButton overridden to false) -- UserDebug's gear
            // (wired in BuildPersistentShell) is the one remaining entry
            // point into Settings, and it just switches to the Settings tab.

            // TacContentPanel starts at SingleMap's width (one column) --
            // it resizes itself (and fires Resized, see below) whenever the
            // view mode changes. Built once here, then reparented into
            // whichever tab's buildPanel is current (see ReparentShellInto)
            // -- each tab's View just toggles its Visible and sets the view
            // mode. Height uses TacOverviewHeight (LeftColumnHeight + half a
            // CardTiny, rounded up to a multiple of 10) per SillyHuman.
            TacContent = new TacContentPanel(_ctx, new Point(TacContentPanel.MapColumnWidth, TacOverviewHeight), _iconTexture)
            {
                Parent = this,
                Location = new Point(LeftColumnWidth + ColumnGap, 0),
                Visible = false
            };
            TacContent.Resized += (s, e) => ResizeToFitContent();

            // TacContentPanel has no title bar of its own and doesn't get a
            // PanelSettingsBinder entry -- it just mirrors TacControlPanel's
            // Enabled state (hiding the controls also hides the grid --
            // there's nothing left to control).
            foreach (var panel in new FeaturePanelBase[] { TacControl, TacContent, QuickTravel, GuildClaims })
            {
                PanelSettingsBinder.Bind(_settingsCollection, panel.StateController);
            }
            
            TacContent.StateController.Enabled = TacControl.StateController.Enabled;
            TacContent.StateController.Minimized = TacControl.StateController.Minimized;
            TacControl.StateController.Changed += (s, e) =>
            {
                if (e.PropertyName == nameof(TacControl.StateController.Enabled))
                {
                    TacContent.StateController.Enabled = e.NewValue;
                }
                else if (e.PropertyName == nameof(TacControl.StateController.Minimized))
                {
                    TacContent.StateController.Minimized = e.NewValue;
                    ResizeToFitContent();
                }
            };

            // Manual left-column stacking (plain Panel, not a FlowPanel --
            // see the class doc comment on the old console for why:
            // FlowPanel only re-flows on Add/Remove, not when an existing
            // child's Height changes later, which is exactly what
            // Minimize/Roam/Enable toggles and GuildClaims' own claim-count
            // resizing do).
            _leftColumnPanels.Clear();
            _leftColumnPanels.AddRange(new FeaturePanelBase[] { TacControl, QuickTravel, GuildClaims });
            foreach (var panel in _leftColumnPanels)
            {
                panel.Resized += (s, e) => RelayoutLeftColumn();
            }
            RelayoutLeftColumn();

            // Subscribed here rather than in the constructor, deliberately:
            // TacContentPanel's own "_ctx.TacState.Changed += RebuildContent"
            // subscription (in its constructor, just above) needs to fire
            // and finish resizing TacContent's Width BEFORE this handler's
            // nested SelectedTab reassignment (below) reads that Width via
            // ResizeToFitContent. Events fire subscribers in subscription
            // order, so subscribing after TacContent exists guarantees that
            // order. This was the actual cause of last round's "Single too
            // wide / All+Detail too narrow": with this subscribed in the
            // constructor (before TacContent existed), switching view mode
            // via TacControlPanel's own dropdown (rather than clicking a
            // tab) fired this handler FIRST, which re-entered
            // ActivateContentTab and read TacContent's stale, previous-tab
            // Width before TacContentPanel's own handler had a chance to
            // update it.
            _ctx.TacState.Changed += OnTacStateChanged;

            RelayoutLeftColumn();
            ResizeToFitContent();

            // Land on whatever tab matches the current (default) view mode.
            OnTacStateChanged(this, EventArgs.Empty);
        }

        private void RelayoutLeftColumn()
        {
            int y = 0;
            foreach (var panel in _leftColumnPanels)
            {
                panel.Location = new Point(0, y);
                y += panel.Height + 4;
            }
            _leftContent.Height = Math.Max(y, _leftViewport.Height);

            ResizeToFitContent();
        }

        /// <summary>
        /// Moves every persistent shell control into the given tab's fresh
        /// buildPanel. Cheap to call every tab switch -- Location values
        /// stay the same (buildPanel's local (0,0) lines up with the old
        /// window+ContentRegion-relative (0,0) these were already using),
        /// only Parent changes.
        /// </summary>
        private void ReparentShellInto(Container buildPanel)
        {
            if (_mapSecondaryBackground != null) _mapSecondaryBackground.Parent = buildPanel;
            _leftViewport.Parent = buildPanel;
            _controlBlock.Parent = buildPanel;
            _columnDivider.Parent = buildPanel;
            SettingsHost.Parent = buildPanel;
            if (TacContent != null) TacContent.Parent = buildPanel;
        }

        /// <summary>
        /// Shared by every TAC tab (Single/All/Detail/Map/None) -- they only
        /// differ in which TacOverviewViewMode they set. All the actual
        /// content lives in persistent panels built once, then reparented
        /// into each tab's buildPanel here (see the class doc comment for
        /// why the reparenting is necessary) -- their own state/Visible
        /// flags carry over untouched since they're the same instances.
        /// </summary>
        public void ActivateContentTab(Container buildPanel, TacOverviewViewMode? viewMode)
        {
            ReparentShellInto(buildPanel);

            if (viewMode.HasValue) _ctx.TacState.ViewMode = viewMode.Value;

            bool leavingSettings = SettingsHost.Visible;
            SettingsHost.Visible = false;
            if (TacContent != null) TacContent.HiddenByHost = !viewMode.HasValue;
            if (_mapSecondaryBackground != null) _mapSecondaryBackground.Visible = viewMode == TacOverviewViewMode.Map;

            if (leavingSettings) SettingsTabDeactivated?.Invoke(this, EventArgs.Empty);
            ResizeToFitContent();
        }

        public void ActivateSettingsTab(Container buildPanel)
        {
            ReparentShellInto(buildPanel);

            if (SettingsHost.Visible) return; // already active, but was still just reparented above -- avoid double-firing

            if (TacContent != null) TacContent.HiddenByHost = true;
            if (_mapSecondaryBackground != null) _mapSecondaryBackground.Visible = false;
            SettingsHost.Visible = true;
            SettingsTabActivated?.Invoke(this, EventArgs.Empty);
            ResizeToFitContent();
        }

        /// <summary>
        /// UNVERIFIED API SURFACE: same caveat as the old console's
        /// ResizeConsoleToFitContent -- assumes setting Width/Height here
        /// reflows this window's chrome/content clip on this Blish HUD
        /// version. If the window doesn't visually resize, this needs a
        /// different approach (most likely rebuilding the window, which
        /// would also reset its saved screen position).
        ///
        /// Round 4 diagnostic (disabling this to rule it out as the cause
        /// of "nothing renders") came back negative -- content still didn't
        /// show with this fully disabled, so it wasn't the cause. Root
        /// cause turned out to be window-direct parenting (see class doc
        /// comment); re-enabled.
        /// </summary>
        private void ResizeToFitContent()
        {
            bool settingsActive = SettingsHost != null && SettingsHost.Visible;
            bool tacActive = TacContent != null && TacContent.Visible;

            int rightWidth = settingsActive ? SettingsFlyoutBuilder.PanelWidth : (tacActive ? TacContent.Width : 0);
            int rightHeight = settingsActive ? LeftColumnHeight : (tacActive ? TacContent.Height : 0);
            bool rightColumnActive = settingsActive || tacActive;

            int contentWidth = rightColumnActive ? LeftColumnWidth + ColumnGap + rightWidth : LeftColumnWidth;
            int contentHeight = Math.Max(LeftColumnHeight, rightHeight);

            Width = contentWidth + ChromeWidthPadding;
            // +MainWindowHeightBonus (2.5 CardTinys) per SillyHuman, then
            // rounded up to the next multiple of 10.
            Height = RoundUpToNextTen(contentHeight + ChromeHeightPadding + MainWindowHeightBonus);
        }

        private void BuildTabs()
        {
            var offIcon = AsyncTexture2D.FromAssetId(156675);
            var singleIcon = AsyncTexture2D.FromAssetId(733274);
            var allIcon = AsyncTexture2D.FromAssetId(156741);
            var detailIcon = AsyncTexture2D.FromAssetId(156702);
            var mapIcon = AsyncTexture2D.FromAssetId(156690);
            var settingsIcon = AsyncTexture2D.FromAssetId(157110);

            TabNone = new Tab(offIcon, () => new MainConsoleView(this, null), "None / Hide TAC");
            TabSingle = new Tab(singleIcon, () => new MainConsoleView(this, TacOverviewViewMode.SingleMap), "Single Map");
            TabAll = new Tab(allIcon, () => new MainConsoleView(this, TacOverviewViewMode.AllMapGrid), "All Maps Grid");
            TabDetail = new Tab(detailIcon, () => new MainConsoleView(this, TacOverviewViewMode.DetailGrid), "Detail Grid");
            TabMap = new Tab(mapIcon, () => new MainConsoleView(this, TacOverviewViewMode.Map), "Guild");
            TabSettings = new Tab(settingsIcon, () => new SettingsView(this), "Settings");

            Tabs.Add(TabNone);
            Tabs.Add(TabSingle);
            Tabs.Add(TabAll);
            Tabs.Add(TabDetail);
            Tabs.Add(TabMap);
            Tabs.Add(TabSettings);
        }

        private void OnTacStateChanged(object sender, EventArgs e)
        {
            if (SelectedTab == TabSettings) return; // background data refreshes must not kick the user out of Settings

            switch (_ctx.TacState.ViewMode)
            {
                case TacOverviewViewMode.SingleMap:
                    if (SelectedTab != TabSingle) SelectedTab = TabSingle;
                    break;
                case TacOverviewViewMode.AllMapGrid:
                    if (SelectedTab != TabAll) SelectedTab = TabAll;
                    break;
                case TacOverviewViewMode.DetailGrid:
                    if (SelectedTab != TabDetail) SelectedTab = TabDetail;
                    break;
                case TacOverviewViewMode.Map:
                    if (SelectedTab != TabMap) SelectedTab = TabMap;
                    break;
            }
        }

        protected override void DisposeControl()
        {
            if (_ctx?.TacState != null)
            {
                _ctx.TacState.Changed -= OnTacStateChanged;
            }
            base.DisposeControl();
        }

        /// <summary>
        /// Every TAC tab (including "None") just tells the window which
        /// view mode (if any) is now active; ActivateContentTab does the
        /// actual reparenting of the persistent shell into this View's
        /// buildPanel (see the class doc comment for why that's necessary).
        /// </summary>
        private class MainConsoleView : View
        {
            private readonly WvWarlordMainWindow _window;
            private readonly TacOverviewViewMode? _viewMode;

            public MainConsoleView(WvWarlordMainWindow window, TacOverviewViewMode? viewMode)
            {
                _window = window;
                _viewMode = viewMode;
            }

            protected override void Build(Container buildPanel) => _window.ActivateContentTab(buildPanel, _viewMode);
        }

        private class SettingsView : View
        {
            private readonly WvWarlordMainWindow _window;

            public SettingsView(WvWarlordMainWindow window)
            {
                _window = window;
            }

            protected override void Build(Container buildPanel) => _window.ActivateSettingsTab(buildPanel);
        }
    }
}