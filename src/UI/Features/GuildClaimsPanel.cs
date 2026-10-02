using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WvWarlord.Api;
using WvWarlord.Core;
using WvWarlord.UI.Cards;

namespace WvWarlord.UI.Features
{
    /// <summary>
    /// Guild Claims: consolidates every open claim into a uniform ledger,
    /// nesting a CardClaimed row under a header for each guild linked to the
    /// player's account whose tag currently holds at least one objective.
    /// </summary>
    public class GuildClaimsPanel : FeaturePanelBase
    {
        private const int TitleRowHeight = 20;
        private const int CardRowHeight = 36; // WvwCardClaimed.CardHeight
        private const double MaxVisibleCards = 18.5;

        private readonly WvwModuleContext _ctx;

        // Viewport/content split -- same fix as the left column and
        // TacContentPanel (see learnings.md): a single Panel/FlowPanel can't
        // be both a fixed-size scrollable viewport AND grow to fit its own
        // children at the same time. _ledger's Size used to be set once
        // here at construction and never touched again, so once claims
        // exceeded that frozen height, TopToBottom flow wrapped into a
        // second column instead of the panel expanding. Now _ledgerViewport
        // is the fixed/capped-height CanScroll frame, and _ledger (the
        // FlowPanel doing the actual layout) is free to grow taller than it
        // -- which is also what makes CanScroll do anything at all past the
        // 18.5-card cap (a CanScroll panel needs content taller than itself
        // to scroll).
        private Panel _ledgerViewport;
        private FlowPanel _ledger;

        public GuildClaimsPanel(WvwModuleContext ctx, Point size, Texture2D windowIcon)
            : base("guild_claims", "GUILD CLAIMS", size, windowIcon)
        {
            _ctx = ctx;
            _ctx.DataService.DataUpdated += (s, e) => RebuildLedger();
            _ctx.DataService.PreloadCompleted += (s, e) => RebuildLedger();
            InitializeChrome();
        }

        /// <summary>Settings button lives on UserDebug's title bar only now (see FeaturePanelBase.ShowSettingsButton).</summary>
        protected override bool ShowSettingsButton => false;

        protected override void BuildContent(Panel contentArea)
        {
            _ledgerViewport = new Panel
            {
                Parent = contentArea,
                Location = new Point(0, 0),
                Size = new Point(contentArea.Width, contentArea.Height),
                CanScroll = true
            };

            _ledger = new FlowPanel
            {
                Parent = _ledgerViewport,
                Location = Point.Zero,
                Size = _ledgerViewport.Size,
                FlowDirection = ControlFlowDirection.TopToBottom,
                ControlPadding = new Vector2(0, 3)
            };

            RebuildLedger();
        }

        private string _ledgerSignature;

        private void RebuildLedger()
        {
            // Cards show only name + tier, so skip the teardown/rebuild on
            // the 15s tick unless a claim or tier actually changed.
            string signature = string.Join(";", _ctx.DataService.MyAccountGuilds.Keys.Select(g =>
                g + "=" + string.Join(",", _ctx.DataService.LiveStates.Values
                    .Where(st => !string.IsNullOrEmpty(st.ClaimedByGuildId) && st.ClaimedByGuildId.Equals(g, StringComparison.OrdinalIgnoreCase))
                    .Select(st => st.Id + ":" + st.Tier))));
            if (signature == _ledgerSignature && _ledger.Children.Count > 0) return;
            _ledgerSignature = signature;

            _ledger.ClearChildren();

            if (_ctx.DataService.MyAccountGuilds.Count == 0)
            {
                new Label
                {
                    Parent = _ledger,
                    Text = "No guilds linked to this account yet.",
                    Size = new Point(_ledger.Width, 20),
                    Font = Blish_HUD.GameService.Content.DefaultFont14,
                    TextColor = Color.Gray
                };
                ApplyContentSizing(headerRows: 1, claimRows: 0);
                return;
            }

            int headerRows = 0, claimRows = 0;
            foreach (var guildKvp in _ctx.DataService.MyAccountGuilds)
            {
                string guildId = guildKvp.Key;
                var guildData = guildKvp.Value;

                var claims = _ctx.DataService.LiveStates.Values
                    .Where(st => !string.IsNullOrEmpty(st.ClaimedByGuildId) && st.ClaimedByGuildId.Equals(guildId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                new Label
                {
                    Parent = _ledger,
                    Text = $"{guildData.Tag} {guildData.Name}",
                    Location = new Point(4, 0),
                    Size = new Point(_ledger.Width - 4, 20),
                    Font = Blish_HUD.GameService.Content.DefaultFont14,
                    TextColor = Color.Gold
                };

                headerRows++;

                foreach (var state in claims)
                {
                    if (!WvwCatalogService.ById.TryGetValue(state.Id, out var objInfo)) continue;
                    new WvwCardClaimed(_ctx.DataService, objInfo, state, _ctx.Router) { Parent = _ledger };
                    claimRows++;
                }
            }

            ApplyContentSizing(headerRows, claimRows);
        }

        /// <summary>
        /// Sizes the panel to fit exactly what's currently in the ledger,
        /// capped at ~18.5 cards worth of height (plus header rows) so a very
        /// active account doesn't push the console off-screen -- beyond that
        /// cap the ledger scrolls instead of growing further.
        /// </summary>
        private void ApplyContentSizing(int headerRows, int claimRows)
        {
            // _ledger itself always sizes to the FULL, uncapped row count --
            // it holds every card that RebuildLedger created above, capped
            // or not. This is what lets _ledgerViewport's CanScroll work at
            // all once claimRows exceeds the cap: below the cap _ledger is
            // no taller than the viewport anyway (nothing to scroll, panel
            // just visually expands); above it, _ledger is genuinely taller
            // than _ledgerViewport, which is what makes it scrollable.
            int fullContentHeight = headerRows * TitleRowHeight + claimRows * CardRowHeight + (headerRows + claimRows) * 3;
            _ledger.Height = Math.Max(fullContentHeight, _ledgerViewport.Height);

            int cappedClaimRows = (int)Math.Min(claimRows, MaxVisibleCards);
            int viewportHeight = headerRows * TitleRowHeight + cappedClaimRows * CardRowHeight + (headerRows + cappedClaimRows) * 3;
            _ledgerViewport.Height = viewportHeight;

            ResizeExpandedHeight(viewportHeight + 28 + 8);
        }
    }
}