using System;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WvWarlord.UI;

namespace WvWarlord.Core
{
    public abstract class FeaturePanelBase : Panel
    {
        public PanelStateController StateController { get; }
        protected Panel ContentArea { get; private set; }
        protected Panel TitleBar { get; private set; }
        
        protected virtual bool ShowSettingsButton => false;

        private readonly string _displayTitle;
        private readonly Texture2D _windowIconTexture;
        private int _expandedWidth;
        private readonly bool _showTitleBar;
        private int _expandedHeight;

        private Label _titleLabel;
        private Image _minMaxButton;
        private Image _roamingButton;
        private Image _settingsButton;
        private Image _titleDivider;

        public event EventHandler SettingsRequested;
        private Label _dockedPlaceholder;

        protected FeaturePanelBase(string panelId, string displayTitle, Point size, Texture2D windowIconTexture, bool showTitleBar = true)
        {
            _displayTitle = displayTitle;
            _windowIconTexture = windowIconTexture;
            _expandedWidth = size.X;
            _expandedHeight = size.Y;
            _showTitleBar = showTitleBar;

            Size = size;

            // Standard GW2 Window frame texture background
            BackgroundTexture = GameService.Content.DatAssetCache.GetTextureFromAssetId(155985);

            StateController = new PanelStateController(panelId);
            StateController.Changed += (s, e) => RefreshChrome();
            StateController.VisibilityShouldChange += OnBattleVisibilityShouldChange;
            StateController.ConfigureRoaming(BuildRoamingWindow, control =>
            {
                ContentArea.Parent = this;
            });
        }

        protected void InitializeChrome()
        {
            int contentTop = 0;

            if (_showTitleBar)
            {
                TitleBar = new Panel
                {
                    Parent = this,
                    Location = new Point(0, 0),
                    Size = new Point(_expandedWidth, 30),
                    BackgroundColor = new Color(8, 8, 8) * 0.85f
                };

                _titleLabel = new Label
                {
                    Parent = TitleBar,
                    Location = new Point(8, 5),
                    Size = new Point(_expandedWidth - 130, 20),
                    Text = _displayTitle,
                    TextColor = Color.LightGreen, // matches UserDebugContent's STATUS label -- was gold (240,210,150), which read too similar to everything else and blended in
                    Font = GameService.Content.DefaultFont16
                };

                // Native GW2 Gold/Metal Divider separating title bar from panel content
                _titleDivider = new Image
                {
                    Parent = this,
                    Texture = AsyncTexture2D.FromAssetId(157085),
                    Location = new Point(0, 29),
                    Size = new Point(_expandedWidth, 2)
                };

                const int iconSize = 24;
                int iconX = _expandedWidth - iconSize - 4;

                _roamingButton = new Image
                {
                    Parent = TitleBar,
                    Texture = WvwChromeIcons.Popout,
                    Size = new Point(iconSize, iconSize),
                    Location = new Point(iconX, 3),
                    BasicTooltipText = "Pop out into its own window"
                };
                _roamingButton.Click += (s, e) => StateController.SetRoamingLive(!StateController.Roaming);
                iconX -= iconSize + 4;

                _minMaxButton = new Image
                {
                    Parent = TitleBar,
                    Texture = WvwChromeIcons.Minimize,
                    Size = new Point(iconSize, iconSize),
                    Location = new Point(iconX, 3),
                    BasicTooltipText = "Minimize"
                };
                _minMaxButton.Click += (s, e) => StateController.SetMinimizedLive(!StateController.Minimized);
                iconX -= iconSize + 4;

                int iconZoneStartX;
                if (ShowSettingsButton)
                {
                    _settingsButton = new Image
                    {
                        Parent = TitleBar,
                        Texture = WvwChromeIcons.Gear,
                        Size = new Point(iconSize, iconSize),
                        Location = new Point(iconX, 3),
                        BasicTooltipText = "Settings"
                    };
                    _settingsButton.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
                    iconZoneStartX = iconX;
                }
                else
                {
                    iconZoneStartX = iconX + iconSize + 4;
                }

                bool ClickIsInIconZone()
                {
                    int relativeX = GameService.Input.Mouse.Position.X - TitleBar.AbsoluteBounds.X;
                    return relativeX >= iconZoneStartX;
                }
                TitleBar.Click += (s, e) => { if (!ClickIsInIconZone()) StateController.SetMinimizedLive(!StateController.Minimized); };
                _titleLabel.Click += (s, e) => { if (!ClickIsInIconZone()) StateController.SetMinimizedLive(!StateController.Minimized); };

                _dockedPlaceholder = new Label
                {
                    Parent = this,
                    Location = new Point(6, 34),
                    Size = new Point(_expandedWidth - 12, 20),
                    Text = "Roaming -- content detached to standalone window.",
                    TextColor = Color.Gray,
                    Font = GameService.Content.DefaultFont14,
                    Visible = false
                };

                contentTop = 32;
            }

            ContentArea = new Panel
            {
                Parent = this,
                Location = new Point(0, contentTop),
                Size = new Point(_expandedWidth, _expandedHeight - contentTop)
            };

            BuildContent(ContentArea);
            RefreshChrome();
        }

        protected abstract void BuildContent(Panel contentArea);

        protected void ResizeExpandedHeight(int newExpandedHeight)
        {
            int contentTop = _showTitleBar ? 32 : 0;
            _expandedHeight = Math.Max(contentTop, newExpandedHeight);
            ContentArea.Size = new Point(_expandedWidth, _expandedHeight - contentTop);
            RefreshChrome();
            SyncRoamingSize();
        }

        /// <summary>While popped out, the floating window must follow the content's size (it used to stay at its creation size).</summary>
        private void SyncRoamingSize()
        {
            if (StateController.RoamingWindow is RoamingPanel rp) rp.SetContentSize(ContentArea.Width, ContentArea.Height);
        }

        protected void ResizeExpandedWidth(int newExpandedWidth)
        {
            _expandedWidth = Math.Max(50, newExpandedWidth);
            ContentArea.Size = new Point(_expandedWidth, ContentArea.Height);
            if (_showTitleBar)
            {
                TitleBar.Width = _expandedWidth;
                _titleLabel.Width = _expandedWidth - 130;
                if (_titleDivider != null) _titleDivider.Width = _expandedWidth;
            }
            RefreshChrome();
            SyncRoamingSize();
        }
        private bool _hiddenByHost;

        /// <summary>Set by the main window (e.g. while the Settings tab is showing). RefreshChrome honors it, so internal rebuilds can't re-show the panel.</summary>
        public bool HiddenByHost
        {
            get => _hiddenByHost;
            set { _hiddenByHost = value; RefreshChrome(); }
        }

        private void RefreshChrome()
        {
            bool minimized = StateController.Minimized;
            bool roaming = _showTitleBar && StateController.Roaming;
            bool enabled = StateController.Enabled;

            if (_showTitleBar)
            {
                _minMaxButton.Texture = minimized ? WvwChromeIcons.Restore : WvwChromeIcons.Minimize;
                _minMaxButton.BasicTooltipText = minimized ? "Restore" : "Minimize";
                _minMaxButton.Tint = minimized ? Color.Gold : Color.White;
                _roamingButton.Tint = roaming ? Color.Gold : Color.White;

                _dockedPlaceholder.Visible = !minimized && roaming;
            }

            if (!enabled)
            {
                Visible = false;
                Width = 0;
                Height = 0;
                return;
            }

            Visible = !_hiddenByHost;
            Width = _expandedWidth;

            int minimizedHeight = _showTitleBar ? 30 : 0;
            Height = minimized ? minimizedHeight : _expandedHeight;

            ContentArea.Visible = !minimized;
        }

        private void OnBattleVisibilityShouldChange(object sender, bool visible)
        {
            float opacity = visible ? 1f : 0.15f;
            Opacity = opacity;
            if (StateController.RoamingWindow != null) StateController.RoamingWindow.Opacity = opacity;
        }

        private RoamingPanel BuildRoamingWindow()
        {
            var contentSize = new Point(ContentArea.Width, ContentArea.Height);
            var panel = new RoamingPanel(_displayTitle, _windowIconTexture, contentSize)
            {
                Parent = GameService.Graphics.SpriteScreen,
                Opacity = 0.97f
            };

            panel.Location = StateController.LastRoamingLocation
                ?? new Point(AbsoluteBounds.Right + 10, AbsoluteBounds.Top);

            panel.Minimized = false;
            ContentArea.Visible = true;

            ContentArea.Parent = panel;
            ContentArea.Location = panel.ContentOffset;
            ContentArea.Size = contentSize;

            panel.MinimizedChanged += (s, e) => ContentArea.Visible = !panel.Minimized;

            panel.CloseRequested += (s, e) =>
            {
                if (StateController.Roaming) StateController.SetRoamingLive(false);
            };

            RefreshChrome();
            return panel;
        }

        protected override void DisposeControl()
        {
            if (StateController.Roaming)
            {
                ContentArea.Parent = this;
            }
            StateController.Dispose();
            base.DisposeControl();
        }
    }
}