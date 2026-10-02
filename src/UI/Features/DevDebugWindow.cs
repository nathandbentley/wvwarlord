using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WvWarlord.Api;
using WvWarlord.Core;
using static System.Collections.Specialized.BitVector32;

namespace WvWarlord.UI.Features
{
    /// <summary>
    /// DevDebug: a standalone diagnostic window, hidden by default and only
    /// reachable when its setting is checked. Includes a live asset verification 
    /// tester that injects diagnostic tabs straight into the primary window.
    /// </summary>
    public class DevDebugWindow : IDisposable
    {
        private readonly WvwModuleContext _ctx;
        private readonly Action _triggerModuleReload;
        private readonly Texture2D _windowIcon;
        private readonly SettingEntry<bool> _skipRoute1Setting;

        private StandardWindow _window;
        private Label _metricsLabel;
        private Label _matchInfoLabel;
        private MultilineTextBox _logBox;
        private bool _suppressLogTextChanged;

        // Tracks diagnostic tester tab allocations so they don't leak memory or duplicate
        private readonly List<Tab> _createdTesterTabs = new List<Tab>();
        private int _testerTabCounter = 0;

        public DevDebugWindow(WvwModuleContext ctx, Action triggerModuleReload, Texture2D windowIcon, SettingEntry<bool> skipRoute1Setting)
        {
            _ctx = ctx;
            _triggerModuleReload = triggerModuleReload;
            _windowIcon = windowIcon;
            _skipRoute1Setting = skipRoute1Setting;
        }

        public void Toggle()
        {
            if (_window == null)
            {
                Build();
                return;
            }

            if (_window.Visible) _window.Hide();
            else { _window.Show(); RefreshMetrics(); RefreshLog(); }
        }

        private void Build()
        {
            var windowRegion = new Rectangle(0, 0, 720, 680);
            var contentRegion = new Rectangle(10, 36, 700, 634);

            _window = new StandardWindow(_windowIcon, windowRegion, contentRegion)
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "WvWarlord - Dev Debug",
                SavesPosition = true,
                Id = "wvwarlord_dev_debug_window",
                Opacity = 0.97f
            };

            var topFrame = new Panel { Parent = _window, Location = new Point(0, 0), Size = new Point(contentRegion.Width, 140), BackgroundColor = new Color(10, 10, 12) * 0.97f };

            _metricsLabel = new Label
            {
                Parent = topFrame,
                Location = new Point(6, 4),
                Size = new Point(topFrame.Width - 12, 60),
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.LightGray
            };

            _matchInfoLabel = new Label
            {
                Parent = topFrame,
                Location = new Point(6, 62),
                Size = new Point(topFrame.Width - 12, 18),
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.Gold * 0.85f
            };

            var clearBtn = new StandardButton { Parent = topFrame, Text = "Clear Logs", Location = new Point(6, 82), Size = new Point(90, 24) };
            clearBtn.Click += (s, e) => ApiCallTracker.ClearLogs();

            var reloadBtn = new StandardButton { Parent = topFrame, Text = "Reload Module", Location = new Point(100, 82), Size = new Point(110, 24) };
            reloadBtn.Click += (s, e) => _triggerModuleReload?.Invoke();

            var wipeBtn = new StandardButton { Parent = topFrame, Text = "Wipe & Rebuild", Location = new Point(214, 82), Size = new Point(120, 24) };
            wipeBtn.Click += async (s, e) => await _ctx.DataService.WipeAndReloadAsync();

            var copyBtn = new StandardButton { Parent = topFrame, Text = "Copy Log", Location = new Point(338, 82), Size = new Point(90, 24) };
            copyBtn.Click += (s, e) => _ = Blish_HUD.ClipboardUtil.WindowsClipboardService.SetTextAsync(ApiCallTracker.GetLogText());

            // ?? THE ASSET INJECTION BUTTON ??
            var injectTabBtn = new StandardButton
            {
                Parent = topFrame,
                Text = "Create Tester Tab",
                Location = new Point(434, 82),
                Size = new Point(130, 24),
                BasicTooltipText = "Injects an interactive asset diagnostic tab into the main window"
            };
            injectTabBtn.Click += (s, e) => SpawnDiagnosticTab();

            var mumbleButton = new StandardButton
            {
                Parent = topFrame,
                Text = "Mumble Report",
                Location = new Point(570, 82), // Placed right next to the Inject Tab button
                Size = new Point(120, 24)
            };
            mumbleButton.Click += (sender, args) =>
            {
                var mumble = GameService.Gw2Mumble.PlayerCharacter;

                string dump = $"[MUMBLE DUMP] Pos: X:{mumble.Position.X:F2}, Y:{mumble.Position.Y:F2}, Z:{mumble.Position.Z:F2}";
                ApiCallTracker.Log(dump);
                System.Diagnostics.Debug.WriteLine(dump);
            };



            var skipRoute1Check = new Checkbox
            {
                Parent = topFrame,
                Text = "[Dev] Skip Route1 (Gw2ApiManager) Globally",
                Location = new Point(6, 112),
                Checked = _skipRoute1Setting?.Value ?? false
            };
            skipRoute1Check.CheckedChanged += (s, e) => { if (_skipRoute1Setting != null) _skipRoute1Setting.Value = skipRoute1Check.Checked; };

            var logBackdrop = new Panel { Parent = _window, Location = new Point(0, 144), Size = new Point(contentRegion.Width, contentRegion.Height - 144), BackgroundColor = new Color(6, 6, 8) * 0.98f, CanScroll = true };

            _logBox = new MultilineTextBox
            {
                Parent = logBackdrop,
                Location = new Point(4, 4),
                Size = new Point(logBackdrop.Width - 8, logBackdrop.Height - 8),
                Font = GameService.Content.DefaultFont14
            };
            _logBox.TextChanged += (s, e) =>
            {
                if (_suppressLogTextChanged) return;
                RefreshLog();
            };

            ApiCallTracker.LogUpdated += OnLogUpdated;
            _ctx.DataService.DataUpdated += OnDataUpdated;

            RefreshMetrics();
            RefreshLog();
        }

        /// <summary>
        /// Allocates a fresh dynamic Tab and injects it straight into your active TabbedWindow2.
        /// </summary>
        private void SpawnDiagnosticTab()
        {
            var activeWindow = GameService.Graphics.SpriteScreen.Children
        .OfType<WvWarlordMainWindow>()
        .FirstOrDefault(w => w.Id == "wvw_warlord_main_window_v2" || w.Id == "wvw_warlord_main_window");


            if (activeWindow == null)
            {
                ApiCallTracker.Log("[DevDebug] FAILED to inject tab: WvWarlordMainWindow instance not found on SpriteScreen.");
                return;
            }

            _testerTabCounter++;
            string tabName = $"Test {_testerTabCounter}";

            // Default placeholder circle layout icon to start
            var initialIcon = AsyncTexture2D.FromAssetId(156675);

            Tab testerTab = null;
            testerTab = new Tab(initialIcon, () => new DynamicAssetTesterView(activeWindow, () => testerTab), tabName);

            _createdTesterTabs.Add(testerTab);
            activeWindow.Tabs.Add(testerTab);

            // Instantly transition focus directly to our newly allocated view panel sandbox
            activeWindow.SelectedTab = testerTab;

            ApiCallTracker.Log($"[DevDebug] Successfully injected diagnostic window tab: {tabName}");
        }

        private int _logRefreshQueued;

        // LogUpdated fires from any thread (every ApiCallTracker.Log call)
        // and used to rebuild the whole 500-line log text and touch the
        // textbox immediately, even with this window closed. Now: skipped
        // while hidden, coalesced, and marshalled to the main thread.
        private void OnLogUpdated(object sender, EventArgs e)
        {
            if (_window == null || !_window.Visible) return;
            if (Interlocked.Exchange(ref _logRefreshQueued, 1) == 1) return;
            GameService.Overlay.QueueMainThreadUpdate(_ =>
            {
                Interlocked.Exchange(ref _logRefreshQueued, 0);
                RefreshLog();
            });
        }

        private void OnDataUpdated(object sender, EventArgs e)
        {
            if (_window != null && _window.Visible) RefreshMetrics();
        }

        public void Dispose()
        {
            ApiCallTracker.LogUpdated -= OnLogUpdated;
            if (_ctx?.DataService != null) _ctx.DataService.DataUpdated -= OnDataUpdated;
            _window?.Dispose();
            _window = null;
        }

        private void RefreshMetrics()
        {
            if (_metricsLabel == null) return;
            _metricsLabel.Text =
                $"Total Weighted Calls: {ApiCallTracker.TotalWeightedCalls:N0}\n" +
                $"Route1 (Blish) Calls: {ApiCallTracker.BlishCallCount:N0}\n" +
                $"Route2 (Direct) Calls: {ApiCallTracker.DirectCallCount:N0}\n" +
                $"Catches Logged: {ApiCallTracker.CatchCount:N0}\n" +
                $"Route1 Globally Skipped: {ApiFallbackExecutor.SkipRoute1Globally}";

            _matchInfoLabel.Text = $"Match: {_ctx.DataService.ActiveMatchId} | Objectives: {_ctx.DataService.LiveStates.Count}/91 | Last Update: {_ctx.DataService.LastUpdateTime.ToLocalTime():HH:mm:ss}";
        }

        private void RefreshLog()
        {
            if (_logBox == null) return;
            string logText = ApiCallTracker.GetLogText();
            if (_logBox.Text == logText) return;

            _suppressLogTextChanged = true;
            _logBox.Text = logText;
            _suppressLogTextChanged = false;
        }

        // =========================================================================
        // ?? THE LIVE ASSET EXPERIMENT VIEW INTERFACE
        // =========================================================================
        private class DynamicAssetTesterView : View
        {
            private readonly WvWarlordMainWindow _window;
            private readonly Func<Tab> _tabRefProvider;

            private TextBox _assetInput;
            private Image _previewDisplay;
            private Label _errorLabel;

            public DynamicAssetTesterView(WvWarlordMainWindow window, Func<Tab> tabRefProvider)
            {
                _window = window;
                _tabRefProvider = tabRefProvider;
            }

            protected override void Build(Container buildPanel)
            {
                // Force transparent pass-through so it clears out your background masking systems
                buildPanel.BackgroundColor = Color.Transparent;

                var layoutFlow = new FlowPanel
                {
                    Parent = buildPanel,
                    Location = new Point(10, 10),
                    Size = new Point(buildPanel.Width - 20, buildPanel.Height - 20),
                    FlowDirection = ControlFlowDirection.SingleTopToBottom,
                    ControlPadding = new Vector2(0, 8)
                };

                new Label
                {
                    Parent = layoutFlow,
                    Text = "ASSET TESTING LAB",
                    Font = GameService.Content.DefaultFont14,
                    TextColor = Color.Gold,
                    Size = new Point(300, 20)
                };

                // ID Entry Box
                _assetInput = new TextBox
                {
                    Parent = layoutFlow,
                    Size = new Point(200, 26),
                    Text = "156675",
                    PlaceholderText = "Enter GW2 Asset ID..."
                };

                var controlsRow = new Panel { Parent = layoutFlow, Size = new Point(400, 32) };
                // Update Trigger Button
                var updateBtn = new StandardButton { Parent = controlsRow, Location = new Point(0, 2), Size = new Point(110, 26), Text = "Update Asset" };
                // Remove/Close Trigger Button
                var closeBtn = new StandardButton
                {
                    Parent = controlsRow,
                    Location = new Point(120, 22),
                    // Offset to sit next to Update button
                    Size = new Point(100, 26),
                    Text = "Close Tab",
                    BackgroundColor = Color.Maroon * 0.8f
                }; closeBtn.Location = new Point(120, 2); _errorLabel = new Label { Parent = layoutFlow, Size = new Point(400, 20), TextColor = Color.Red, Visible = false };
                // Large Preview Display Frame Box
                _previewDisplay = new Image
                {
                    Parent = layoutFlow,
                    Size = new Point(128, 128),
                    // 128x128 showcases high-definition fidelity or clipping bleeding instantly
                    BackgroundColor = Color.Black * 0.3f,
                    Texture = AsyncTexture2D.FromAssetId(156675)
                };
                // Update Trigger Processing Code Loop
                // Helper method to load textures without repeating code or using TriggerClick
                Action<int> loadAssetAction = (parsedId) =>
                {
                    _errorLabel.Visible = false;
                    try
                    {
                        var targetTexture = AsyncTexture2D.FromAssetId(parsedId);

                        if (targetTexture == null)
                        {
                            throw new Exception("Asset ID returned a null reference.");
                        }

                        _previewDisplay.Texture = targetTexture;

                        var currentTab = _tabRefProvider();
                        if (currentTab != null)
                        {
                            currentTab.Icon = targetTexture;
                        }
                    }
                    catch (Exception ex)
                    {
                        _errorLabel.Text = "Invalid or missing Asset ID context.";
                        _errorLabel.Visible = true;
                        ApiCallTracker.Log($"[AssetTester] Refused invalid asset ID {parsedId}: {ex.Message}");
                    }
                };

                Action triggerUpdateAction = () =>
                {
                    if (int.TryParse(_assetInput.Text, out int parsedId))
                    {
                        loadAssetAction(parsedId);
                    }
                    else
                    {
                        _errorLabel.Text = "Invalid numerical ID entered.";
                        _errorLabel.Visible = true;
                    }
                };
                updateBtn.Click += (s, e) => triggerUpdateAction();
                _assetInput.EnterPressed += (s, e) => triggerUpdateAction();

                var prevBtn = new StandardButton
                {
                    Parent = controlsRow,
                    Location = new Point(120, 2),
                    Size = new Point(50, 26),
                    Text = "<"
                };

                prevBtn.Click += (s, e) =>
                {
                    if (int.TryParse(_assetInput.Text, out int parsedId))
                    {
                        int nextId = parsedId - 1;
                        _assetInput.Text = nextId.ToString();
                        loadAssetAction(nextId);
                    }
                };

                var nextBtn = new StandardButton
                {
                    Parent = controlsRow,
                    Location = new Point(180, 2),
                    Size = new Point(50, 26),
                    Text = ">"
                };

                nextBtn.Click += (s, e) =>
                {
                    if (int.TryParse(_assetInput.Text, out int parsedId))
                    {
                        int nextId = parsedId + 1;
                        _assetInput.Text = nextId.ToString();
                        loadAssetAction(nextId);
                    }
                };

                closeBtn.Location = new Point(240, 2);
                closeBtn.Click += (s, e) =>
                {
                    var currentTab = _tabRefProvider();
                    if (currentTab != null && _window != null)
                    {
                        if (_window.TabNone != null)
                        {
                            _window.SelectedTab = _window.TabNone;
                        }
                        _window.Tabs.Remove(currentTab);
                    }
                };
            }
        }
    }
}