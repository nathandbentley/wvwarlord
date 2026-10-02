using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WvWarlord.Core;

namespace WvWarlord.UI
{
    /// <summary>
    /// Floating window used when a FeaturePanelBase is "roamed out" of the
    /// console. Deliberately a plain Panel -- NOT a StandardWindow/TabbedWindow
    /// (WindowBase2) -- because Blish HUD's global Escape-closes-everything
    /// behavior only walks the active WindowBase2 stack. A control that never
    /// joins that stack never gets told to hide, so it needs no "was this
    /// Escape or a real close" guesswork at all; it simply never reacts to
    /// Escape. This replaces the previous StandardWindow + keyboard-state
    /// hack entirely (see FeaturePanelBase history).
    ///
    /// Trade-off: WindowBase2's built-in SavesPosition (position remembered
    /// across game restarts) goes away with it, since that's implemented on
    /// WindowBase2 itself. FeaturePanelBase now remembers the last on-screen
    /// position in memory for the rest of the session (resets on relog) --
    /// wiring that into module settings for true cross-session persistence
    /// is a reasonable follow-up if it's wanted.
    /// </summary>
    public class RoamingPanel : Panel
    {
        /// <summary>Raised when the close (X) button is clicked -- the caller decides what "close" means (here: un-pop back to docked).</summary>
        public event EventHandler CloseRequested;

        /// <summary>Raised after Minimized changes, so the caller can show/hide its own content control accordingly.</summary>
        public event EventHandler MinimizedChanged;

        /// <summary>Offset, inside this panel, where the caller's content control should be positioned.</summary>
        public Point ContentOffset { get; }

        private const int TitleBarHeight = 32;
        private const int Border = 2;
        private const int IconSize = 24;

        private int _contentWidth;
        private int _contentHeight;
        private int _labelX = 6;
        private Label _titleLabel;
        private Image _closeButton;
        private readonly Panel _titleBar;
        private readonly Image _minMaxButton;

        private bool _minimized;
        private bool _dragging;
        private Point _dragOffset;

        // Control.Update turned out to be sealed (see the CS0506 this
        // replaced) -- there's no override point on the control itself to
        // poll from. Instead, every live RoamingPanel registers itself here,
        // and WvWarlordModule.Update (already proven to run every frame --
        // it already ticks StateController.TickCombatState there) calls
        // TickAllDrags() once per frame.
        private static readonly List<RoamingPanel> _liveInstances = new List<RoamingPanel>();
      
        public bool Minimized
        {
            get => _minimized;
            set
            {
                if (_minimized == value) return;
                _minimized = value;
                _minMaxButton.Texture = _minimized ? WvwChromeIcons.Restore : WvwChromeIcons.Minimize;
                _minMaxButton.BasicTooltipText = _minimized ? "Restore" : "Minimize";
                // Same backup signal as the docked title bar's minimize
                // button -- visible regardless of whether the Restore
                // texture itself renders distinctly.
                _minMaxButton.Tint = _minimized ? Color.Gold : Color.White;
                Height = _minimized ? TitleBarHeight + Border * 2 : _contentHeight + TitleBarHeight + Border * 2;
                MinimizedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public RoamingPanel(string title, Texture2D windowIcon, Point contentSize)
        {
            _contentWidth = contentSize.X;
            _contentHeight = contentSize.Y;

            Size = new Point(_contentWidth + Border * 2, _contentHeight + TitleBarHeight + Border * 2);
            BackgroundColor = new Color(16, 16, 18) * 0.97f;
            ContentOffset = new Point(Border, Border + TitleBarHeight);

            _titleBar = new Panel
            {
                Parent = this,
                Location = new Point(Border, Border),
                Size = new Point(_contentWidth, TitleBarHeight),
                BackgroundColor = new Color(8, 8, 8) * 0.97f
            };

            if (windowIcon != null)
            {
                new Image { Parent = _titleBar, Texture = windowIcon, Size = new Point(24, 24), Location = new Point(4, 4) };
                _labelX = 32;
            }

            var titleLabel = _titleLabel = new Label
            {
                Parent = _titleBar,
                Location = new Point(_labelX, 6),
                Size = new Point(_contentWidth - _labelX - 60, 20),
                Text = title,
                TextColor = Color.LightGreen,
                Font = GameService.Content.DefaultFont16
            };

            int iconX = _contentWidth - IconSize - 4;

            var closeButton = _closeButton = new Image
            {
                Parent = _titleBar,
                Texture = WvwChromeIcons.Close,
                Size = new Point(IconSize, IconSize),
                Location = new Point(iconX, 4),
                BasicTooltipText = "Close (returns to console)"
            };
            closeButton.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
            iconX -= IconSize + 4;

            _minMaxButton = new Image
            {
                Parent = _titleBar,
                Texture = WvwChromeIcons.Minimize,
                Size = new Point(IconSize, IconSize),
                Location = new Point(iconX, 4),
                BasicTooltipText = "Minimize"
            };
            _minMaxButton.Click += (s, e) => Minimized = !Minimized;

            // Same "click anywhere on the bar toggles minimize" behavior as
            // the docked title bar -- the icons sit on top and hit-test
            // first, so their own Click handlers still win over these.
            _titleBar.Click += (s, e) => Minimized = !Minimized;
            titleLabel.Click += (s, e) => Minimized = !Minimized;

            // Drag-to-move. Position tracking is a per-frame poll (TickDrag,
            // via the module's Update loop) using only Location and
            // GameService.Input.Mouse.Position -- both already proven
            // elsewhere in this project. Start/stop use the title bar's own
            // Pressed/Released events. Bounds-gating isn't a concern for
            // Released here the way it was for the old MouseMoved-driven
            // approach: because TickDrag keeps the bar glued under the
            // cursor every single frame (not just when an event happens to
            // fire), the cursor is essentially always still over the bar at
            // the moment of release.
            _titleBar.LeftMouseButtonPressed += StartDrag;
            titleLabel.LeftMouseButtonPressed += StartDrag;
            _titleBar.LeftMouseButtonReleased += (s, e) => _dragging = false;
            titleLabel.LeftMouseButtonReleased += (s, e) => _dragging = false;

            _liveInstances.Add(this);
        }

        /// <summary>
        /// Re-fits the window to new content dimensions. The docked panel can
        /// change size while popped out (Tac Overview's width follows its
        /// view mode; Quick Travel and Guild Claims grow with their lists) --
        /// this used to be fixed at the size the window was created with.
        /// </summary>
        public void SetContentSize(int width, int height)
        {
            _contentWidth = width;
            _contentHeight = height;
            _titleBar.Width = width;
            _titleLabel.Width = Math.Max(20, width - _labelX - 60);
            _closeButton.Location = new Point(width - IconSize - 4, 4);
            _minMaxButton.Location = new Point(width - 2 * IconSize - 8, 4);
            Width = width + Border * 2;
            Height = _minimized ? TitleBarHeight + Border * 2 : height + TitleBarHeight + Border * 2;
        }

        private void StartDrag(object sender, Blish_HUD.Input.MouseEventArgs e)
        {
            _dragging = true;
            _dragOffset = GameService.Input.Mouse.Position - Location;
        }

        /// <summary>Call once per frame from WvWarlordModule.Update for every roaming panel to keep drags smooth.</summary>
        internal static void TickAllDrags()
        {
            foreach (var panel in _liveInstances) panel.TickDrag();
        }

        // UNVERIFIED API SURFACE (much lower risk than what this replaced):
        // Location and GameService.Input.Mouse.Position are both already
        // proven elsewhere in this project. The previous version of this
        // method also checked GameService.Input.Mouse.State.LeftButton to
        // detect release, which had never been used anywhere else in the
        // codebase -- if that API didn't behave the way I assumed, an
        // exception thrown here every frame (silently swallowed by
        // whatever wraps the module's Update loop) would explain windows
        // not moving at all despite drag starting successfully. Release is
        // now detected via LeftMouseButtonReleased instead (see the
        // constructor), which is a pattern already used throughout this
        // codebase.
        private void TickDrag()
        {
            if (_dragging) Location = GameService.Input.Mouse.Position - _dragOffset;
        }

        protected override void DisposeControl()
        {
            _liveInstances.Remove(this);
            base.DisposeControl();
        }
    }
}
