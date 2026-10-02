using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WvWarlord.Core;
using WvWarlord.UI.Features;

namespace WvWarlord.UI
{
    /// <summary>
    /// Shared settings panel, opened via the gear button on ANY feature
    /// panel's title bar (FeaturePanelBase.SettingsRequested) -- not one per
    /// panel. Renders IN PLACE of the Tac grid in the console's right column
    /// (per feedback) rather than a separate floating window: no minimize or
    /// popout controls to manage, no escape-proofing concerns, and closing
    /// is just the Close button here or clicking any gear button again.
    ///
    /// Each panel contributes its own bordered section (TacControlPanel,
    /// QuickTravelPanel, GuildClaimsPanel, and -- now folded under Core
    /// Settings rather than getting its own top-level section --
    /// ScarMapHandoffService via its BuildFlyoutSection method), plus a
    /// Core Settings section (chat route, ScarMap, etc. -- GW2 API Key
    /// lives on Blish HUD's native settings page only, not here).
    ///
    /// Every row is label-left/control-right ("unstacked", rather than
    /// several dropdowns crammed into one row), and every section is
    /// bordered on top/right/bottom (no left border) in the same color as
    /// its title, via AddLabeledRow/AddCheckboxRow/BeginSection/EndSection
    /// below -- other panels' BuildFlyoutSection methods call these too.
    /// </summary>
    public class SettingsFlyoutBuilder
    {
        public const int PanelWidth = 500; // was 700 -- that was too wide for the window's actual content region (the window's outer Width/Height resize on tab switch is unverified to also grow the underlying buildPanel/contentRegion it composites into, so anything wider than the ~542px actually available past the left column could render outside the visible container). 500 keeps the wider two-column layout from round 6 while staying safely inside that budget. Was 400 before round 5.

        // _apiKey/_applyAction are no longer used inside this class -- the
        // GW2 API Key field + Apply button were dropped from this panel
        // (native settings page owns the key now, see PanelWidth's comment
        // history). Left as constructor params/fields rather than removed
        // so WvWarlordModule's existing call site doesn't need touching
        // this round; safe to actually drop both next time that file's up.
        private readonly SettingEntry<string> _apiKey;
        private readonly SettingEntry<bool> _useDevDebugWindow;
        private readonly SettingEntry<string> _chatLinkRoute;
        private readonly SettingEntry<bool> _autoSend;
        private readonly Func<List<(string Label, PanelStateController Controller)>> _panelsProvider;
        private readonly Func<TacControlPanel> _tacControlPanelProvider;
        private readonly Func<TacContentPanel> _tacContentPanelProvider;
        private readonly Func<QuickTravelPanel> _quickTravelPanelProvider;
        private readonly Func<ScarMapHandoffService> _scarMapProvider;
        private readonly Action _devDebugToggle;
        private readonly Action _applyAction;
        private readonly Func<Panel> _hostProvider;
        private readonly Action<bool> _setSettingsMode;

        private Panel _panel;

        // Event subscriptions made by controls built into the flyout
        // (TacState.Changed / PanelStateController.Changed). The flyout is
        // rebuilt every time the Settings tab opens, and those handlers used
        // to be left subscribed forever -- one more set (plus the disposed
        // controls they pointed at) per visit. Run and cleared on close.
        private static readonly List<Action> _cleanups = new List<Action>();
        public static void RegisterCleanup(Action undo) => _cleanups.Add(undo);
        private static void RunCleanups()
        {
            foreach (var undo in _cleanups) undo();
            _cleanups.Clear();
        }

        public SettingsFlyoutBuilder(
            SettingEntry<string> apiKey,
            SettingEntry<bool> useDevDebugWindow,
            SettingEntry<string> chatLinkRoute,
            SettingEntry<bool> autoSend,
            Func<List<(string Label, PanelStateController Controller)>> panelsProvider,
            Func<TacControlPanel> tacControlPanelProvider,
            Func<TacContentPanel> tacContentPanelProvider,
            Func<QuickTravelPanel> quickTravelPanelProvider,
            Func<ScarMapHandoffService> scarMapProvider,
            Action devDebugToggle,
            Action applyAction,
            Func<Panel> hostProvider,
            Action<bool> setSettingsMode)
        {
            _apiKey = apiKey;
            _useDevDebugWindow = useDevDebugWindow;
            _chatLinkRoute = chatLinkRoute;
            _autoSend = autoSend;
            _panelsProvider = panelsProvider;
            _tacControlPanelProvider = tacControlPanelProvider;
            _tacContentPanelProvider = tacContentPanelProvider;
            _quickTravelPanelProvider = quickTravelPanelProvider;
            _scarMapProvider = scarMapProvider;
            _devDebugToggle = devDebugToggle;
            _applyAction = applyAction;
            _hostProvider = hostProvider;
            _setSettingsMode = setSettingsMode;
        }

        public void Toggle()
        {
            if (_panel != null)
            {
                // Second click (from any panel's gear button) just closes it.
                Close();
                return;
            }
            Build();
        }

        private void Close()
        {
            RunCleanups();
            _panel?.Dispose();
            _panel = null;
            _setSettingsMode?.Invoke(false);
        }

        /// <summary>
        /// Tears the panel down WITHOUT invoking the setSettingsMode
        /// callback -- used when the Settings tab is being left because the
        /// user picked a DIFFERENT tab directly (WvWarlordMainWindow already
        /// knows where it's going; navigating again via the callback would
        /// fight that choice). Close() above (the in-panel Close button) is
        /// the only path that should navigate anywhere.
        /// </summary>
        public void ForceClose()
        {
            RunCleanups();
            _panel?.Dispose();
            _panel = null;
        }

        /// <summary>Called from WvWarlordModule.Unload.</summary>
        public void Dispose()
        {
            RunCleanups();
            _panel?.Dispose();
            _panel = null;
        }

        private void Build()
        {
            var host = _hostProvider?.Invoke();
            if (host == null) return;

            _setSettingsMode?.Invoke(true);

            // Viewport/content split, same fix as TacContentPanel's columns
            // and the left column: _panel is the fixed-height CanScroll
            // viewport; "content" is a child of it that grows taller than
            // the viewport to hold everything, since a plain Panel doesn't
            // auto-track its children's extent.
            _panel = new Panel
            {
                Parent = host,
                Location = Point.Zero,
                Size = new Point(PanelWidth, host.Height),
                CanScroll = true,
                BackgroundColor = new Color(16, 16, 18) * 0.97f
            };
            var content = new Panel { Parent = _panel, Location = Point.Zero, Size = new Point(PanelWidth, host.Height) };

            int x = 0;
            int width = PanelWidth;
            int y = 8;

            new Label
            {
                Parent = content,
                Text = "SETTINGS",
                Location = new Point(x + 4, y),
                Size = new Point(width - 8, 20),
                Font = GameService.Content.DefaultFont18,
                TextColor = Color.White
            };
            y += 26;

            // ---- Tac Overview: left = its own filters, right = panel toggles ----
            var tacControlPanel = _tacControlPanelProvider?.Invoke();
            var tacContentPanel = _tacContentPanelProvider?.Invoke();
            BeginSection(content, "TAC OVERVIEW", Color.LightGreen, x, width, out int tacStart, ref y);
            int tacLeftY = y, tacRightY = y;
            tacControlPanel?.BuildFlyoutSection(content, x, width / 2, ref tacLeftY);
            if (tacContentPanel != null) AddPanelToggleColumn(content, x + width / 2, width / 2, tacContentPanel.StateController, ref tacRightY);
            y = Math.Max(tacLeftY, tacRightY);
            EndSection(content, Color.LightGreen, x, width, tacStart, ref y);

            // ---- Quick Travel: left = its own filter, right = panel toggles ----
            var quickTravelPanel = _quickTravelPanelProvider?.Invoke();
            BeginSection(content, "QUICK TRAVEL", Color.LightGreen, x, width, out int qtStart, ref y);
            int qtLeftY = y, qtRightY = y;
            quickTravelPanel?.BuildFlyoutSection(content, x, width / 2, ref qtLeftY);
            if (quickTravelPanel != null) AddPanelToggleColumn(content, x + width / 2, width / 2, quickTravelPanel.StateController, ref qtRightY);
            y = Math.Max(qtLeftY, qtRightY);
            EndSection(content, Color.LightGreen, x, width, qtStart, ref y);

            // ---- Guild Claims ----
            // No filter/sort content of its own (unlike Tac Overview/Quick
            // Travel), but per SillyHuman it needs real representation, not
            // just the bare Enabled checkbox it was getting from the old
            // catch-all "PANELS" loop below -- pulled out into its own
            // section with the full 4-toggle column, same as the others.
            var allPanels = _panelsProvider?.Invoke() ?? new List<(string Label, PanelStateController Controller)>();
            var guildClaimsEntry = allPanels.FirstOrDefault(p => p.Label.IndexOf("Guild Claim", StringComparison.OrdinalIgnoreCase) >= 0);
            if (guildClaimsEntry.Controller != null)
            {
                BeginSection(content, "GUILD CLAIMS", Color.LightGreen, x, width, out int gcStart, ref y);
                int gcLeftY = y, gcRightY = y;
                // Left half filled with an explanatory label rather than
                // left empty, purely so this section keeps the same
                // label-left/toggles-right rhythm as Tac Overview/Quick
                // Travel above, per SillyHuman.
                new Label
                {
                    Parent = content,
                    Text = "No filter/sort options for this panel.",
                    Location = new Point(x + 8, gcLeftY + 5),
                    Size = new Point(width / 2 - 16, 40),
                    Font = GameService.Content.DefaultFont14,
                    TextColor = Color.Gray
                };
                AddPanelToggleColumn(content, x + width / 2, width / 2, guildClaimsEntry.Controller, ref gcRightY);
                y = Math.Max(gcLeftY, gcRightY);
                EndSection(content, Color.LightGreen, x, width, gcStart, ref y);
            }

            // ---- Core Settings ----
            // GW2 API Key + Apply used to be duplicated here -- both dropped
            // per SillyHuman; the Blish-native settings page (API Key,
            // Hotkey, Enable Dev Debug Window -- see round 6) is now the
            // only place the key lives, so there's nothing left in this
            // panel that needs an Apply/Cancel pair: every remaining control
            // (ChatLink route, Auto-Send, ScarMap fields, panel toggles)
            // already writes straight to its SettingEntry/controller live.
            BeginSection(content, "CORE SETTINGS", Color.Gold, x, width, out int coreStart, ref y);

            var routeLabel = new Label { Text = "ChatLink Handling", Size = new Point(170, 24), Font = GameService.Content.DefaultFont14, TextColor = Color.LightGray };
            var routeDropdown = new Dropdown { Size = new Point(170, 24) };
            foreach (var name in ChatLinkRouteNames.All) routeDropdown.Items.Add(name);
            routeDropdown.SelectedItem = ChatLinkRouteNames.ToDisplay(_chatLinkRoute.Value);
            routeDropdown.ValueChanged += (s, e) => _chatLinkRoute.Value = ChatLinkRouteNames.ToStored(routeDropdown.SelectedItem);
            var autoSendCheck = new Checkbox { Text = "Auto-Send", Checked = _autoSend.Value };
            autoSendCheck.CheckedChanged += (s, e) => _autoSend.Value = autoSendCheck.Checked;
            AddFlowRow(content, x, ref y, routeLabel, routeDropdown, autoSendCheck);

            // ScarMap folded into Core Settings, right under ChatLink, per
            // SillyHuman -- it's a chatlink destination, not a peer of
            // Tac Overview/Quick Travel, so it no longer gets its own
            // top-level bordered section. Its "ScarMap Filter" row label
            // (matching "ChatLink Handling" above) is added inside
            // ScarMapHandoffService.BuildFlyoutSection itself now.
            var scarMapHandoff = _scarMapProvider?.Invoke();
            scarMapHandoff?.BuildFlyoutSection(content, x, width, ref y);

            // Enable Dev Debug Window and [Dev] Skip Route1 both moved out of
            // here per SillyHuman -- the former is Blish HUD's native
            // settings page only now, the latter lives on the Dev Debug
            // window itself. This button still lives here for convenience,
            // but only shows up once that native setting is actually on.
            if (_useDevDebugWindow.Value)
            {
                var devDebugBtn = new StandardButton { Parent = content, Text = "Open Dev Debug", Location = new Point(x + 8, y), Size = new Point(width - 16, 24) };
                devDebugBtn.Click += (s, e) => _devDebugToggle?.Invoke();
                y += 30;
            }

            EndSection(content, Color.Gold, x, width, coreStart, ref y);

            // The old catch-all "Panels"/"Extra Panels" section is gone --
            // per SillyHuman, nothing was actually left in it: TacControl
            // and QuickTravel get their own dedicated sections above,
            // GuildClaims got pulled into its own section this round, and
            // TacContent has no independent PanelSettingsBinder entry of
            // its own (it just mirrors TacControl's Enabled state), so
            // `allPanels` above should only ever have contained GuildClaims.

            // Close: no minimize/popout here, and this isn't a separate
            // window so Escape doesn't apply to it either -- this button (or
            // clicking any gear icon again) is the only way to dismiss it.
            var closeBtn = new StandardButton { Parent = content, Text = "Close", Location = new Point(x + 8, y + 6), Size = new Point(width - 16, 28) };
            closeBtn.Click += (s, e) => Close();
            y += 40;

            content.Height = Math.Max(y, _panel.Height);
        }

        // ---- Shared layout helpers, also used by other panels' BuildFlyoutSection methods ----

        /// <summary>Label on the left, the given control docked to the right half of the row -- "unstacked" rather than several controls crammed into one row.</summary>
        public static void AddLabeledRow(Panel parent, int x, int width, ref int y, string label, Control control)
        {
            new Label
            {
                Parent = parent,
                Text = label,
                Location = new Point(x + 8, y + 5),
                Size = new Point(width / 2 - 16, 20),
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.LightGray
            };
            control.Parent = parent;
            control.Location = new Point(x + width / 2, y);
            y += 30;
        }

        /// <summary>Checkbox aligned to the right half of the row, same rhythm as AddLabeledRow -- keeps its own built-in text as the label rather than adding a redundant separate one.</summary>
        public static void AddCheckboxRow(Panel parent, int x, int width, ref int y, Checkbox checkbox)
        {
            checkbox.Parent = parent;
            checkbox.Location = new Point(x + width / 2, y + 3);
            y += 26;
        }

        /// <summary>
        /// Stacks one panel's four state toggles (Enabled/Minimized/Popped
        /// Out/Stay in Battle), left-aligned within the given column --
        /// meant to sit alongside a feature's own BuildFlyoutSection called
        /// at half width, per SillyHuman's two-column layout (left =
        /// filter/sort/view, right = toggles).
        /// </summary>
        /// <summary>
        /// Controls flowing left-to-right with a small fixed gap, each sized
        /// to its own natural/explicit width -- per SillyHuman, for rows
        /// pairing two related settings (e.g. ChatLink Route + Auto-Send)
        /// that don't need a rigid half-width split.
        /// </summary>
        public static void AddPanelToggleColumn(Panel parent, int x, int width, PanelStateController controller, ref int y)
        {

            var togglesData = new[]
            {
                new { Prop = nameof(controller.Enabled),   Label = "Enabled",        Val = controller.Enabled,   Setter = (Action<bool>)(v => controller.Enabled = v) },
                new { Prop = nameof(controller.Minimized), Label = "Minimized",      Val = controller.Minimized, Setter = (Action<bool>)(v => controller.Minimized = v) },
                new { Prop = nameof(controller.Roaming),   Label = "Popped Out",     Val = controller.Roaming,   Setter = (Action<bool>)(v => controller.Roaming = v) },
                new { Prop = nameof(controller.Battle),    Label = "Stay in Battle", Val = controller.Battle,    Setter = (Action<bool>)(v => controller.Battle = v) }
            };

            // Using a standard loop isolates the lambdas into distinct stack allocations, fixing CS1628
            foreach (var item in togglesData)
            {
                var cb = new Checkbox { Parent = parent, Text = item.Label, Checked = item.Val, Location = new Point(x + 8, y) };

                var currentItem = item;
                var currentCb = cb;

                cb.CheckedChanged += (s, e) => currentItem.Setter(currentCb.Checked);

                EventHandler<PanelStateChangedEventArgs> sync = (s, e) =>
                {
                    if (e.PropertyName == currentItem.Prop)
                        currentCb.Checked = e.NewValue;
                };
                controller.Changed += sync;
                RegisterCleanup(() => controller.Changed -= sync);

                y += 24;
            }
        }


        /// <summary>
        /// Controls flowing left-to-right with a small fixed gap, each sized
        /// to its own natural/explicit width -- per SillyHuman, for rows
        /// pairing two related settings (e.g. ChatLink Route + Auto-Send)
        /// that don't need a rigid half-width split.
        /// </summary>
        public static void AddFlowRow(Panel parent, int x, ref int y, params Control[] controls)
        {
            int cx = x + 8;
            int rowHeight = 0;
            foreach (var control in controls)
            {
                control.Parent = parent;
                control.Location = new Point(cx, y);
                cx += control.Width + 16;
                rowHeight = Math.Max(rowHeight, control.Height);
            }
            y += rowHeight + 8;
        }

        /// <summary>Section title in the given accent color; call EndSection afterward to draw the matching top/right/bottom border around everything added in between.</summary>
        public static void BeginSection(Panel parent, string title, Color accent, int x, int width, out int sectionStartY, ref int y)
        {
            y += 10;
            new Label
            {
                Parent = parent,
                Text = title,
                Location = new Point(x + 4, y),
                Size = new Point(width - 8, 18),
                Font = GameService.Content.DefaultFont16,
                TextColor = accent
            };
            y += 24;
            sectionStartY = y;
        }

        /// <summary>Draws top/right/bottom border strips (no left border) around the section's content, in the same color as its title.</summary>
        public static void EndSection(Panel parent, Color accent, int x, int width, int sectionStartY, ref int y)
        {
            int height = Math.Max(4, y - sectionStartY + 6);
            new Panel { Parent = parent, Location = new Point(x, sectionStartY - 4), Size = new Point(width, 2), BackgroundColor = accent };
            new Panel { Parent = parent, Location = new Point(x + width - 2, sectionStartY - 4), Size = new Point(2, height + 4), BackgroundColor = accent };
            new Panel { Parent = parent, Location = new Point(x, sectionStartY + height), Size = new Point(width, 2), BackgroundColor = accent };
            y = sectionStartY + height + 8;
        }
    }

    /// <summary>Maps ChatLinkRoute enum values to display strings and back, shared by the settings flyout and module wiring.</summary>
    public static class ChatLinkRouteNames
    {
        public static readonly string[] All =
        {
            "Clipboard Only", "Manual", "/d (Squad)", "Shift+Enter /d",
            "/p (Party)", "/s (Say)", "/w [username]", "ScarMap"
        };

        public static string ToDisplay(string storedEnumName)
        {
            switch (storedEnumName)
            {
                case "ClipboardOnly": return "Clipboard Only";
                case "Manual": return "Manual";
                case "SquadD": return "/d (Squad)";
                case "ShiftEnterSquadD": return "Shift+Enter /d";
                case "PartyP": return "/p (Party)";
                case "SayS": return "/s (Say)";
                case "WhisperUsername": return "/w [username]";
                case "ScarMap": return "ScarMap";
                default: return "Clipboard Only";
            }
        }

        public static string ToStored(string display)
        {
            switch (display)
            {
                case "Clipboard Only": return "ClipboardOnly";
                case "Manual": return "Manual";
                case "/d (Squad)": return "SquadD";
                case "Shift+Enter /d": return "ShiftEnterSquadD";
                case "/p (Party)": return "PartyP";
                case "/s (Say)": return "SayS";
                case "/w [username]": return "WhisperUsername";
                case "ScarMap": return "ScarMap";
                default: return "ClipboardOnly";
            }
        }
    }
}