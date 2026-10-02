using System;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;

namespace WvWarlord.Core
{
    public class PanelStateChangedEventArgs : EventArgs
    {
        public string PropertyName { get; }
        public bool NewValue { get; }

        /// <summary>
        /// True (the default) for changes that should overwrite the
        /// persisted "load on reboot" default -- the settings-flyout
        /// checkboxes (SettingsFlyoutBuilder.AddPanelToggleColumn) and the
        /// initial load in PanelSettingsBinder.Bind both go through this
        /// path. False for a live-only change -- e.g. clicking a panel's own
        /// title bar to minimize it, or its pop-out icon -- which should
        /// only affect the panel's current on-screen state for this session,
        /// per SillyHuman ("changing settings outside of settings should not
        /// update the settings"). Either way Changed still fires, so a
        /// flyout checkbox stays visually in sync with a live-only toggle --
        /// it just doesn't persist it.
        /// </summary>
        public bool Persist { get; }

        public PanelStateChangedEventArgs(string propertyName, bool newValue, bool persist = true)
        {
            PropertyName = propertyName;
            NewValue = newValue;
            Persist = persist;
        }
    }

    /// <summary>
    /// State engine each feature panel owns: Enabled / Minimized / Roaming / Battle.
    ///   - Minimized: caller collapses to a title bar (id label + Maximize + Roaming toggle only).
    ///   - Battle: mirrors GameService.Gw2Mumble.PlayerCharacter.IsInCombat; non-Battle
    ///     panels fade while in combat, Battle panels always stay fully visible.
    ///   - Roaming: detaches into a standalone RoamingPanel (see WvWarlord.UI).
    ///     Roaming windows are inherently escape-proof because RoamingPanel is a
    ///     plain Panel, not a WindowBase2 -- it never joins the stack Escape
    ///     walks, so there's nothing to guard against. There is no separate
    ///     "Sticky" concept anymore; a docked panel always responds to Escape,
    ///     a roaming one never does.
    /// The Main Console itself does not use this controller: it is neither
    /// minimizable, roamable, nor battle-aware.
    /// </summary>
    public class PanelStateController : IDisposable
    {
        public string PanelId { get; }

        private bool _enabled = true;
        private bool _minimized = false;
        private bool _roaming = false;
        private bool _battle = false;

        public bool Enabled
        {
            get => _enabled;
            set => SetFlag(ref _enabled, value, nameof(Enabled));
        }

        public bool Minimized
        {
            get => _minimized;
            set => SetFlag(ref _minimized, value, nameof(Minimized));
        }

        /// <summary>Inline/title-bar toggle -- changes current on-screen state only, does not overwrite the persisted default. See PanelStateChangedEventArgs.Persist.</summary>
        public void SetMinimizedLive(bool value) => SetFlag(ref _minimized, value, nameof(Minimized), persist: false);

        public bool Roaming
        {
            get => _roaming;
            set => SetRoamingCore(value, persist: true);
        }

        /// <summary>Inline/title-bar (pop-out icon) toggle -- same real roaming-window creation/teardown as the property setter, just doesn't overwrite the persisted default. See PanelStateChangedEventArgs.Persist.</summary>
        public void SetRoamingLive(bool value) => SetRoamingCore(value, persist: false);

        private void SetRoamingCore(bool value, bool persist)
        {
            if (_roaming == value) return;
            _roaming = value;
            if (_roaming)
            {
                RoamingWindow = _roamingWindowFactory?.Invoke();
                if (RoamingWindow != null) RoamingWindow.Visible = !_ambientSuppressed;
            }
            else if (RoamingWindow != null)
            {
                // Captured here (still valid, pre-dispose) so it survives
                // across close/reopen and, via PanelSettingsBinder, across
                // restarts -- per SillyHuman's request to remember
                // roaming window positions.
                LastRoamingLocation = RoamingWindow.Location;

                // Give the caller a chance to reclaim anything it parented
                // into the roaming window (e.g. reparent ContentArea back
                // onto the docked panel) BEFORE it gets disposed -- this
                // used to be missing, so un-roaming (whether via the title
                // bar toggle or the floating window's own Close button)
                // would dispose ContentArea right along with the window.
                _beforeRoamingTeardown?.Invoke(RoamingWindow);
                RoamingWindow.Dispose();
                RoamingWindow = null;
            }
            Changed?.Invoke(this, new PanelStateChangedEventArgs(nameof(Roaming), value, persist));
        }

        /// <summary>
        /// Last known screen position of this panel's roaming window (set
        /// right before it's torn down, whether via the title-bar toggle or
        /// the floating window's own Close button). Null until the panel has
        /// been roamed at least once. FeaturePanelBase.BuildRoamingWindow
        /// reads this to reopen at the same spot; PanelSettingsBinder
        /// persists it so it survives restarts too.
        /// </summary>
        public Point? LastRoamingLocation { get; set; }

        public bool Battle
        {
            get => _battle;
            set => SetFlag(ref _battle, value, nameof(Battle));
        }

        public bool IsInCombatNow { get; private set; }
        public Control RoamingWindow { get; private set; }

        private Func<Control> _roamingWindowFactory;
        private Action<Control> _beforeRoamingTeardown;

        public event EventHandler<PanelStateChangedEventArgs> Changed;
        public event EventHandler<bool> VisibilityShouldChange;

        public PanelStateController(string panelId)
        {
            PanelId = panelId;
        }

        public void ConfigureRoaming(Func<Control> windowFactory, Action<Control> beforeTeardown = null)
        {
            _roamingWindowFactory = windowFactory;
            _beforeRoamingTeardown = beforeTeardown;
        }

        private void SetFlag(ref bool field, bool value, string name, bool persist = true)
        {
            if (field == value) return;
            field = value;
            Changed?.Invoke(this, new PanelStateChangedEventArgs(name, value, persist));
        }

        private bool _ambientSuppressed;

        /// <summary>
        /// Called every frame from WvWarlordModule.Update with the current
        /// (not-on-a-WvW-map AND console-closed) result. Hides this panel's
        /// roaming window specifically -- the docked panel doesn't need this,
        /// since it's already not visible whenever the console itself is
        /// closed. Deliberately ignores Battle: Battle is about staying
        /// visible during a lull in combat while you're on a map, not about
        /// keeping a window alive once you've left WvW entirely. No
        /// per-panel-type exception (e.g. Guild Claims) -- suppress means
        /// suppress.
        /// </summary>
        public void ApplyAmbientVisibility(bool suppressed)
        {
            if (_ambientSuppressed == suppressed) return;
            _ambientSuppressed = suppressed;
            if (RoamingWindow != null) RoamingWindow.Visible = !suppressed;
        }

        public void TickCombatState()
        {
            bool inCombat;
            try { inCombat = GameService.Gw2Mumble.PlayerCharacter.IsInCombat; }
            catch { inCombat = false; }

            if (inCombat == IsInCombatNow) return;
            IsInCombatNow = inCombat;

            if (Battle) return; // Battle panels always stay visible.
            VisibilityShouldChange?.Invoke(this, !inCombat);
        }

        public void Dispose()
        {
            if (RoamingWindow != null)
            {
                _beforeRoamingTeardown?.Invoke(RoamingWindow);
                RoamingWindow.Dispose();
                RoamingWindow = null;
            }
        }
    }
}