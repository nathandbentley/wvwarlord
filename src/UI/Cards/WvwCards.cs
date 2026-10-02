using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WvWarlord.Api;
using WvWarlord.Core;
using WvWarlord.Models;
using WvwObjectiveType = Gw2Sharp.WebApi.V2.Models.WvwObjectiveType;

namespace WvWarlord.UI.Cards
{
    /// <summary>Shared "Send to..." context menu builder used by every card below.</summary>
    internal static class CardContextMenu
    {
        public static void Attach(Control control, ChatLinkRouter router, Func<string> chatLinkProvider)
        {
            if (router == null) return;

            var menu = new ContextMenuStrip();

            var sendChat = menu.AddMenuItem("Send to Chat");
            sendChat.Click += (s, e) => { var link = chatLinkProvider(); if (link != null) _ = router.SendExplicitAsync(link, ChatLinkRoute.SayS); };

            var sendBroadcast = menu.AddMenuItem("Send to Broadcast");
            sendBroadcast.Click += (s, e) => { var link = chatLinkProvider(); if (link != null) _ = router.SendExplicitAsync(link, ChatLinkRoute.SquadD); };

            var sendClipboard = menu.AddMenuItem("Send to Clipboard");
            sendClipboard.Click += (s, e) => { var link = chatLinkProvider(); if (link != null) _ = router.SendExplicitAsync(link, ChatLinkRoute.ClipboardOnly); };

            var sendScarMap = menu.AddMenuItem("Send to ScarMap");
            sendScarMap.Click += (s, e) => { var link = chatLinkProvider(); if (link != null) router.ScarMapTarget?.Invoke(link); };

            control.Menu = menu;
        }
    }

    public class WvwCardStandard : Panel
    {
        public const int CardWidth = 250;
        public const int CardHeight = 38;

        private readonly WvwLiveDataService _dataService;
        private readonly ChatLinkRouter _router;
        private readonly WvwObjectiveInfo _staticInfo;

        private readonly Panel _factionStripe;
        private new readonly Image _icon;
        private readonly Label _nameLabel;
        private readonly Label _guildTagLabel;
        private readonly Label _line2Label;
        private float? _distance;

        public WvwCardStandard(WvwLiveDataService dataService, string objectiveId, ChatLinkRouter router = null)
        {
            _dataService = dataService;
            _router = router;
            WvwCatalogService.ById.TryGetValue(objectiveId, out _staticInfo);

            Height = CardHeight;
            Width = CardWidth;
            BackgroundColor = new Color(20, 20, 20) * 0.95f;

            _factionStripe = new Panel { Parent = this, Location = new Point(0, 0), Size = new Point(4, Height) };
            _icon = new Image { Parent = this, Location = new Point(6, 2), Size = new Point(36, 36) };

            _nameLabel = new Label
            {
                Parent = this,
                Location = new Point(44, 2),
                Size = new Point(160, 18),
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.White * 0.95f,
                Text = _staticInfo?.Name ?? "Unknown"
            };

            _guildTagLabel = new Label
            {
                Parent = this,
                Location = new Point(44, 2),
                Size = new Point(80, 18),
                Font = GameService.Content.DefaultFont14,
                Visible = false
            };

            _line2Label = new Label
            {
                Parent = this,
                Location = new Point(44, 22),
                Size = new Point(200, 16),
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.LightGray * 0.8f
            };

            Resized += (s, e) => LayoutChildren();
            LeftMouseButtonPressed += (s, e) => { if (_staticInfo != null) _ = _router?.RouteAsync(_staticInfo.ChatLink); };
            CardContextMenu.Attach(this, _router, () => _staticInfo?.ChatLink);

            LayoutChildren();
        }

        private void LayoutChildren()
        {
            _factionStripe.Height = Height;
            // Was Width - 90, leaving ~46px of unused space up to the card's
            // right edge (for CardWidth=250) -- fine for "T2 | Yaks 4 | 12m",
            // but appending " | <distance>" on top of that routinely ran
            // past the label's box and got clipped at the card's own edge
            // (Panels clip their children to their bounds). Widened to use
            // nearly the full remaining width, leaving just a small margin.
            _line2Label.Width = Math.Max(20, Width - 50);
        }

        /// <summary>
        /// Distance (raw map-coordinate units from WvwCatalogService/TacContentPanel's
        /// Vector2.Distance calc, NOT confirmed to equal real-world meters --
        /// shown unlabeled below rather than asserting a wrong unit) shown
        /// only when sorting by distance; TacContentPanel calls this before
        /// Refresh(). Null clears it (e.g. any other sort mode).
        /// </summary>
        public void SetDistance(float? distance) => _distance = distance;

        /// <summary>Used by TacContentPanel's per-tick distance refresh to map an existing card instance back to its objective without a full rebuild.</summary>
        public string ObjectiveId => _staticInfo?.Id;

        /// <summary>Refreshes icon tint, waypoint-swap, guild tag, and status text from live state.</summary>
        public void Refresh()
        {
            if (_staticInfo == null) return;

            _dataService.LiveStates.TryGetValue(_staticInfo.Id, out var state);
            string owner = state?.Owner ?? "Neutral";
            Color factionColor = WvwStaticData.GetFactionColor(owner);

            // Stripe reflects the objective's HOME map color (same rule as
            // WvwCardClaimed), not who currently owns it -- that's what lets
            // you tell columns apart at a glance in the All Maps grid. The
            // icon tint below is still owner-based, since that's the thing
            // you actually want to know at a glance per-objective.
            string mapLabel = WvwStaticData.MapLabels.TryGetValue(_staticInfo.MapId, out var mLbl) ? mLbl : null;
            _factionStripe.BackgroundColor = mapLabel != null ? WvwStaticData.GetFactionColor(mapLabel) : Color.DarkGray;
            _icon.Tint = factionColor;
            _icon.Texture = WvwIconProvider.ForObjective(_staticInfo.Type, state?.HasWaypoint ?? false);

            int maxNameWidth = Math.Max(40, Width - 120);
            int nameWidth = (int)GameService.Content.DefaultFont14.MeasureString(_nameLabel.Text).Width;
            if (nameWidth > maxNameWidth) nameWidth = maxNameWidth;
            _nameLabel.Width = nameWidth + 4;

            string guildTag = "";
            bool isMyGuild = false;
            if (!string.IsNullOrEmpty(state?.ClaimedByGuildId))
            {
                // Reverted: this should track ANY guild the account belongs
                // to, not just the WvW-representing guild (MyWvwGuildId) --
                // that distinction only matters for the console window icon.
                isMyGuild = _dataService.MyAccountGuilds.ContainsKey(state.ClaimedByGuildId);
                guildTag = _dataService.GuildTagCache.TryGetValue(state.ClaimedByGuildId, out var tag) ? tag : "?";
            }

            _guildTagLabel.Visible = !string.IsNullOrEmpty(guildTag);
            if (_guildTagLabel.Visible)
            {
                _guildTagLabel.Text = guildTag;
                _guildTagLabel.Location = new Point(44 + _nameLabel.Width + 6, 2);
                _guildTagLabel.TextColor = isMyGuild ? Color.Gold : Color.White * 0.85f;
            }

            bool locked = state != null && state.LastFlipped != DateTime.MinValue && (DateTime.UtcNow - state.LastFlipped) < TimeSpan.FromMinutes(5);

            if (owner.Equals("Neutral", StringComparison.OrdinalIgnoreCase))
            {
                _line2Label.Text = "Neutral";
                _line2Label.TextColor = Color.DarkGray;
            }
            else if (locked)
            {
                TimeSpan remaining = TimeSpan.FromMinutes(5) - (DateTime.UtcNow - state.LastFlipped);
                _line2Label.Text = $"Locked {remaining.Minutes:D2}:{remaining.Seconds:D2}";
                _line2Label.TextColor = Color.Red;
            }
            else
            {
                string heldText = "";
                if (state != null && state.LastFlipped != DateTime.MinValue)
                {
                    TimeSpan held = DateTime.UtcNow - state.LastFlipped;
                    heldText = " | " + (held.TotalHours >= 1 ? $"{(int)held.TotalHours}h {held.Minutes}m" : $"{held.Minutes}m");
                }
                _line2Label.Text = $"T{state?.Tier ?? 0} | Yaks {state?.YaksDelivered ?? 0}{heldText}";
                _line2Label.TextColor = Color.LightGray * 0.8f;
            }

            if (_distance.HasValue) _line2Label.Text += $" | {_distance.Value:N0}";
        }
    }

    /// <summary>
    /// CardPro: tactical inspection card. Top section is a straight copy of
    /// WvwCardStandard's layout (icon/name/guild-tag/line2) so Pro reads
    /// consistently with the other cards -- previously had a bespoke
    /// Gold-name + yak-count header. CardWidth now matches Standard's
    /// (250) instead of its old bespoke 340. Below the top, the
    /// Infrastructure Matrix is transposed from "3 stacked tier rows x
    /// [Building, Tactics] columns" into up to 2 rows (Building Unlocks,
    /// Guild Tactics) with tiers running left-to-right as columns, zero gap
    /// between tier columns. Building Unlocks was gated off for a while (see
    /// ShowBuildingUnlocks) rather than deleted; re-enabled per SillyHuman.
    /// Each row is exactly 1 icon tall; outline slots are drawn per actual
    /// catalog/slot count for that tier (not padded to a fixed grid), and
    /// only filled in / highlighted once that tier is unlocked. Icon
    /// lookups go through WvwIconProvider.FromApiIconUrl since the API
    /// returns full render-service URLs, not the bare signature/fileId
    /// pairs GetRenderServiceTexture expects.
    /// </summary>
    public class WvwCardPro : Panel
    {
        public const int CardWidth = WvwCardStandard.CardWidth; // 250 -- matches the other cards

        // Toggle to bring Building Unlocks back -- flip true and CardHeight
        // recomputes itself (see below); nothing else needs to change.
        // Commented back out per SillyHuman (was re-enabled briefly, showing
        // Objective/Building upgrades on the card again -- not wanted).
        private const bool ShowBuildingUnlocks = false;

        // Top section matches WvwCardStandard exactly.
        private const int TopHeight = WvwCardStandard.CardHeight; // 38

        // Matrix rows. Icon sizes follow the 6-vs-12-total-icons rule:
        // Guild Tactics maxes at 2 slots/tier * 3 tiers = 6 icons -> 32x32.
        // Building Unlocks maxes at 4 slots/tier * 3 tiers = 12 icons ->
        // 24x24 (a given tier's real slot count is catalog-driven and can
        // be less than 4 -- see PaintBeforeChildren).
        private const int MatrixTopOffset = TopHeight + 6;
        private const int MatrixLeft = 44; // aligned with the Name label's indent, per SillyHuman's request
        private const int RowGap = 6;
        private const int BuildingIconSize = 24;
        private const int TacticIconSize = 32;
        private const int MaxTacticSlotsPerTier = 2; // fixed game mechanic, not catalog-driven

        // Border thickness for unlocked/locked slots. The icon itself is
        // drawn inset by UnlockedBorderThickness inside its slot rect so the
        // border surrounds the icon instead of the icon painting over it --
        // previously both used the exact same rect, so the icon (drawn
        // second) covered the border pixels along every edge.
        private const int UnlockedBorderThickness = 2;
        private const int LockedBorderThickness = 1;

        // With Building Unlocks hidden, Guild Tactics moves up to sit right
        // under the top section instead of leaving an empty gap where Row 1
        // used to be.
        private const int TacticsRowY = ShowBuildingUnlocks ? (MatrixTopOffset + BuildingIconSize + RowGap) : MatrixTopOffset;
        public const int CardHeight = TacticsRowY + TacticIconSize + 8; // top + matrix (whichever rows are shown) + bottom padding

        private readonly WvwLiveDataService _dataService;
        private readonly ChatLinkRouter _router;
        private readonly WvwObjectiveInfo _staticInfo;
        private static Texture2D _pixelTexture;

        private readonly Panel _factionStripe;
        private new readonly Image _icon;
        private readonly Label _nameLabel;
        private readonly Label _guildTagLabel;
        private readonly Label _line2Label;
        private float? _distance;

        // Icon texture cache, populated by Refresh() (called on data update)
        // rather than looked up fresh in PaintBeforeChildren every frame.
        // Calling WvwIconProvider.FromApiIconUrl from PaintBeforeChildren
        // meant every single frame re-requested the async texture, which is
        // the most likely cause of the icons flashing -- caching the
        // AsyncTexture2D wrapper once and just reading its .Texture in
        // Paint should fix that.
        private readonly List<AsyncTexture2D>[] _buildingIconTex = new List<AsyncTexture2D>[3]; // per tier, one entry per sub-upgrade
        private readonly AsyncTexture2D[] _tacticIconTex = new AsyncTexture2D[3 * MaxTacticSlotsPerTier]; // flat, same tier1's-2-then-tier2's-2... ordering as activeTacticIds

        public WvwCardPro(WvwLiveDataService dataService, string objectiveId, ChatLinkRouter router = null)
        {
            _dataService = dataService;
            _router = router;
            if (_pixelTexture == null) _pixelTexture = ContentService.Textures.Pixel;
            WvwCatalogService.ById.TryGetValue(objectiveId, out _staticInfo);

            Size = new Point(CardWidth, CardHeight);
            BackgroundColor = new Color(20, 20, 24) * 0.98f;

            // --- Top section: identical to WvwCardStandard ---
            _factionStripe = new Panel { Parent = this, Location = new Point(0, 0), Size = new Point(4, Height) };
            _icon = new Image { Parent = this, Location = new Point(6, 2), Size = new Point(36, 36) };

            _nameLabel = new Label
            {
                Parent = this,
                Location = new Point(44, 2),
                Size = new Point(160, 18),
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.White * 0.95f,
                Text = _staticInfo?.Name ?? "Unknown"
            };

            _guildTagLabel = new Label
            {
                Parent = this,
                Location = new Point(44, 2),
                Size = new Point(80, 18),
                Font = GameService.Content.DefaultFont14,
                Visible = false
            };

            _line2Label = new Label
            {
                Parent = this,
                Location = new Point(44, 22),
                Size = new Point(200, 16),
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.LightGray * 0.8f
            };

            Resized += (s, e) => { _factionStripe.Height = Height; _line2Label.Width = Math.Max(20, Width - 50); };
            MouseMoved += OnMouseMoved;
            LeftMouseButtonPressed += (s, e) => { if (_staticInfo != null) _ = _router?.RouteAsync(_staticInfo.ChatLink); };
            CardContextMenu.Attach(this, _router, () => _staticInfo?.ChatLink);
        }

        /// <summary>See WvwCardStandard.SetDistance for the unit caveat.</summary>
        public void SetDistance(float? distance) => _distance = distance;

        /// <summary>Used by TacContentPanel's per-tick distance refresh to map an existing card instance back to its objective without a full rebuild.</summary>
        public string ObjectiveId => _staticInfo?.Id;

        /// <summary>Top-section refresh -- identical to WvwCardStandard.Refresh (see that class for the rationale behind each field).</summary>
        public void Refresh()
        {
            if (_staticInfo == null) return;

            _dataService.LiveStates.TryGetValue(_staticInfo.Id, out var state);
            string owner = state?.Owner ?? "Neutral";
            Color factionColor = WvwStaticData.GetFactionColor(owner);

            string mapLabel = WvwStaticData.MapLabels.TryGetValue(_staticInfo.MapId, out var mLbl) ? mLbl : null;
            _factionStripe.BackgroundColor = mapLabel != null ? WvwStaticData.GetFactionColor(mapLabel) : Color.DarkGray;
            _icon.Tint = factionColor;
            _icon.Texture = WvwIconProvider.ForObjective(_staticInfo.Type, state?.HasWaypoint ?? false);

            int maxNameWidth = Math.Max(40, Width - 120);
            int nameWidth = (int)GameService.Content.DefaultFont14.MeasureString(_nameLabel.Text).Width;
            if (nameWidth > maxNameWidth) nameWidth = maxNameWidth;
            _nameLabel.Width = nameWidth + 4;

            string guildTag = "";
            bool isMyGuild = false;
            if (!string.IsNullOrEmpty(state?.ClaimedByGuildId))
            {
                isMyGuild = _dataService.MyAccountGuilds.ContainsKey(state.ClaimedByGuildId);
                guildTag = _dataService.GuildTagCache.TryGetValue(state.ClaimedByGuildId, out var tag) ? tag : "?";
            }

            _guildTagLabel.Visible = !string.IsNullOrEmpty(guildTag);
            if (_guildTagLabel.Visible)
            {
                _guildTagLabel.Text = guildTag;
                _guildTagLabel.Location = new Point(44 + _nameLabel.Width + 6, 2);
                _guildTagLabel.TextColor = isMyGuild ? Color.Gold : Color.White * 0.85f;
            }

            bool locked = state != null && state.LastFlipped != DateTime.MinValue && (DateTime.UtcNow - state.LastFlipped) < TimeSpan.FromMinutes(5);

            if (owner.Equals("Neutral", StringComparison.OrdinalIgnoreCase))
            {
                _line2Label.Text = "Neutral";
                _line2Label.TextColor = Color.DarkGray;
            }
            else if (locked)
            {
                TimeSpan remaining = TimeSpan.FromMinutes(5) - (DateTime.UtcNow - state.LastFlipped);
                _line2Label.Text = $"Locked {remaining.Minutes:D2}:{remaining.Seconds:D2}";
                _line2Label.TextColor = Color.Red;
            }
            else
            {
                string heldText = "";
                if (state != null && state.LastFlipped != DateTime.MinValue)
                {
                    TimeSpan held = DateTime.UtcNow - state.LastFlipped;
                    heldText = " | " + (held.TotalHours >= 1 ? $"{(int)held.TotalHours}h {held.Minutes}m" : $"{held.Minutes}m");
                }
                _line2Label.Text = $"T{state?.Tier ?? 0} | Yaks {state?.YaksDelivered ?? 0}{heldText}";
                _line2Label.TextColor = Color.LightGray * 0.8f;
            }

            if (_distance.HasValue) _line2Label.Text += $" | {_distance.Value:N0}";

            // --- Icon texture cache (see field comments) ---
            int pathId = WvwStaticData.GetUpgradePathId(_staticInfo.Type);
            WvwUpgradeCatalogService.WvwUpgrades.TryGetValue(pathId, out var fullPath);
            List<int> activeTacticIds = state?.GuildUpgrades ?? new List<int>();

            for (int tierIdx = 0; tierIdx < 3 && ShowBuildingUnlocks; tierIdx++) // icons are only drawn when Building Unlocks is on; skipping saves a regex + list per tier on every refresh
            {
                if (fullPath != null && tierIdx < fullPath.Tiers.Count)
                {
                    var subUpgrades = fullPath.Tiers[tierIdx].SubUpgrades;
                    var texList = new List<AsyncTexture2D>(subUpgrades.Count);
                    foreach (var su in subUpgrades) texList.Add(WvwIconProvider.FromApiIconUrl(su.IconUrl));
                    _buildingIconTex[tierIdx] = texList;
                }
                else
                {
                    _buildingIconTex[tierIdx] = null;
                }
            }

            for (int idx = 0; idx < _tacticIconTex.Length; idx++)
            {
                _tacticIconTex[idx] = idx < activeTacticIds.Count && WvwUpgradeCatalogService.GuildUpgrades.TryGetValue(activeTacticIds[idx], out var tactic)
                    ? WvwIconProvider.FromApiIconUrl(tactic.IconUrl)
                    : null;
            }
        }

        public override void PaintBeforeChildren(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_pixelTexture == null || _staticInfo == null) return;

            Rectangle absBounds = AbsoluteBounds;
            _dataService.LiveStates.TryGetValue(_staticInfo.Id, out var state);
            int currentTier = state?.Tier ?? 0;
            // Guild Tactic slots unlock by TIME HELD, not the objective's
            // structural Tier (state.Tier / currentTier above) -- a
            // completely separate progression, per SillyHuman:
            //   Tier I  (10m held): all types (Camp/Tower/Keep/Castle)
            //   Tier II (30m held): Tower/Keep/Castle only (not Camp)
            //   Tier III(60m held): Tower/Keep/Castle only (not Camp)
            // This used to reuse currentTier for the Guild Tactics row too,
            // conflating two unrelated progressions.
            int unlockedTacticTiers = GetUnlockedTacticTierCount(_staticInfo.Type, state?.LastFlipped ?? DateTime.MinValue);
            Color factionColor = WvwStaticData.GetFactionColor(state?.Owner ?? "Neutral");

            spriteBatch.Draw(_pixelTexture, new Rectangle(absBounds.X, absBounds.Y, 4, absBounds.Height), factionColor);

            // Slot COUNTS still come from the catalog here (cheap dictionary
            // reads, not texture loads) -- only the icon TEXTURES themselves
            // come from the _buildingIconTex/_tacticIconTex cache populated
            // by Refresh(), instead of calling WvwIconProvider.FromApiIconUrl
            // fresh every frame the way this used to (see field comments;
            // that was the likely cause of the icons flashing).
            int pathId = WvwStaticData.GetUpgradePathId(_staticInfo.Type);
            WvwUpgradeCatalogService.WvwUpgrades.TryGetValue(pathId, out var fullPath);

            // Row 1: Building Unlocks -- hidden for now (ShowBuildingUnlocks
            // = false); kept intact so it's a one-line flip to restore. Tier
            // columns run left to right with zero gap between them; each
            // column is exactly 1 icon tall and as wide as that tier's real
            // sub-upgrade count -- an outline is drawn for every catalog
            // slot in the tier (no padding to a fixed 4-per-tier grid like
            // before the transpose), and only fills in / highlights once
            // that tier is unlocked.
            if (ShowBuildingUnlocks)
            {
                int buildingY = absBounds.Y + MatrixTopOffset;
                int bx = absBounds.X + MatrixLeft;
                for (int tierIdx = 0; tierIdx < 3; tierIdx++)
                {
                    if (fullPath == null || tierIdx >= fullPath.Tiers.Count) continue;

                    var subUpgrades = fullPath.Tiers[tierIdx].SubUpgrades;
                    var texList = _buildingIconTex[tierIdx];
                    bool unlocked = tierIdx < currentTier;
                    Color borderColor = unlocked ? Color.Gold * 0.75f : Color.White * 0.12f;

                    for (int slot = 0; slot < subUpgrades.Count; slot++)
                    {
                        var iconRect = new Rectangle(bx, buildingY, BuildingIconSize, BuildingIconSize);
                        DrawBorderOutline(spriteBatch, iconRect, borderColor, unlocked ? UnlockedBorderThickness : LockedBorderThickness);

                        if (unlocked)
                        {
                            var tex = texList != null && slot < texList.Count ? texList[slot] : null;
                            if (tex?.Texture != null)
                            {
                                var innerRect = new Rectangle(
                                    iconRect.X + UnlockedBorderThickness, iconRect.Y + UnlockedBorderThickness,
                                    iconRect.Width - 2 * UnlockedBorderThickness, iconRect.Height - 2 * UnlockedBorderThickness);
                                spriteBatch.Draw(tex.Texture, innerRect, Color.White);
                            }
                        }

                        bx += BuildingIconSize; // no spacing between icons or columns
                    }
                }
            }

            // Row 2: Guild Tactics. Always 2 outline slots per tier (fixed
            // game mechanic, unlike Building Unlocks). Deployed tactic IDs
            // are a flat list ordered tier1's 2 slots, then tier2's, then
            // tier3's -- same indexing convention as before the transpose.
            // TacticsRowY sits right under the top section when Building
            // Unlocks is hidden, so there's no dead gap above it. Gated by
            // unlockedTacticTiers (time-held-based), NOT currentTier.
            int tacticsY = absBounds.Y + TacticsRowY;
            int tx = absBounds.X + MatrixLeft;
            foreach (int tierIdx in GetEligibleTacticTierIndices(_staticInfo.Type))
            {
                bool unlocked = tierIdx < unlockedTacticTiers;
                Color borderColor = unlocked ? Color.Gold * 0.75f : Color.White * 0.12f;

                for (int slot = 0; slot < MaxTacticSlotsPerTier; slot++)
                {
                    var iconRect = new Rectangle(tx, tacticsY, TacticIconSize, TacticIconSize);
                    DrawBorderOutline(spriteBatch, iconRect, borderColor, unlocked ? UnlockedBorderThickness : LockedBorderThickness);

                    if (unlocked)
                    {
                        int idx = tierIdx * MaxTacticSlotsPerTier + slot;
                        var tex = idx < _tacticIconTex.Length ? _tacticIconTex[idx] : null;
                        if (tex?.Texture != null)
                        {
                            var innerRect = new Rectangle(
                                iconRect.X + UnlockedBorderThickness, iconRect.Y + UnlockedBorderThickness,
                                iconRect.Width - 2 * UnlockedBorderThickness, iconRect.Height - 2 * UnlockedBorderThickness);
                            spriteBatch.Draw(tex.Texture, innerRect, Color.White);
                        }
                    }

                    tx += TacticIconSize; // no spacing between icons or columns
                }
            }
        }

        private void DrawBorderOutline(SpriteBatch spriteBatch, Rectangle rect, Color color, int thickness)
        {
            spriteBatch.Draw(_pixelTexture, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
            spriteBatch.Draw(_pixelTexture, new Rectangle(rect.X, rect.Y + rect.Height - thickness, rect.Width, thickness), color);
            spriteBatch.Draw(_pixelTexture, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
            spriteBatch.Draw(_pixelTexture, new Rectangle(rect.X + rect.Width - thickness, rect.Y, thickness, rect.Height), color);
        }

        /// <summary>
        /// Guild Tactic slots unlock by TIME HELD, not the objective's
        /// structural Tier (state.Tier) -- a completely separate progression:
        ///   Tier I  (10m held): all types (Camp/Tower/Keep/Castle)
        ///   Tier II (30m held): Tower/Keep/Castle only (not Camp)
        ///   Tier III(60m held): Tower/Keep/Castle only (not Camp)
        /// Returns how many of the 3 tactic tiers are currently unlocked
        /// (0-3), for the same "tierIdx &lt; result" comparison Building
        /// Unlocks already uses against currentTier -- just a different
        /// underlying threshold. lastFlipped == DateTime.MinValue (never
        /// captured/unknown) means 0 minutes held, so nothing is unlocked.
        /// </summary>
        private static int GetUnlockedTacticTierCount(WvwObjectiveType type, DateTime lastFlipped)
        {
            if (lastFlipped == DateTime.MinValue) return 0;
            double minutesHeld = (DateTime.UtcNow - lastFlipped).TotalMinutes;

            int count = 0;
            if (minutesHeld >= 10) count = 1;
            if (minutesHeld >= 30 && IsTacticTierEligibleForType(type, 1)) count = 2;
            if (minutesHeld >= 60 && IsTacticTierEligibleForType(type, 2)) count = 3;
            return count;
        }

        /// <summary>Whether this building type can ever unlock the given tactic tierIdx (0/1/2), regardless of time held -- see GetUnlockedTacticTierCount.</summary>
        private static bool IsTacticTierEligibleForType(WvwObjectiveType type, int tierIdx)
        {
            switch (tierIdx)
            {
                case 0: return true; // Tier I: all types
                case 1: return type == WvwObjectiveType.Tower || type == WvwObjectiveType.Keep || type == WvwObjectiveType.Castle;
                case 2: return type == WvwObjectiveType.Tower || type == WvwObjectiveType.Keep || type == WvwObjectiveType.Castle;
                default: return false;
            }
        }

        /// <summary>
        /// Tier indices this type can EVER unlock, in draw/column order. A
        /// Camp, for instance, returns {0} -- only Tier I is available to
        /// Camps; Tower/Keep/Castle all return {0, 1, 2}. So this isn't just
        /// "locked", it's never coming for this type, and per SillyHuman
        /// shouldn't reserve a column at all (unlike a merely time-locked
        /// tier, which still gets its outline slots so you can see progress
        /// coming). Shared by the paint loop and the hit-test so column
        /// positions always agree between them.
        /// </summary>
        // Shared, never mutated -- this runs for every card every frame
        // (PaintBeforeChildren), and used to allocate a new list each time.
        private static readonly List<int> AllTacticTiers = new List<int> { 0, 1, 2 };
        private static readonly List<int> FirstTacticTierOnly = new List<int> { 0 };

        private static List<int> GetEligibleTacticTierIndices(WvwObjectiveType type)
        {
            return IsTacticTierEligibleForType(type, 1) ? AllTacticTiers : FirstTacticTierOnly;
        }

        private void OnMouseMoved(object sender, Blish_HUD.Input.MouseEventArgs e)
        {
            if (_staticInfo == null) return;

            Rectangle absBounds = AbsoluteBounds;
            Point relativeMouse = GameService.Input.Mouse.Position - new Point(absBounds.X, absBounds.Y);
            _dataService.LiveStates.TryGetValue(_staticInfo.Id, out var state);
            int currentTier = state?.Tier ?? 0;
            int unlockedTacticTiers = GetUnlockedTacticTierCount(_staticInfo.Type, state?.LastFlipped ?? DateTime.MinValue);
            int pathId = WvwStaticData.GetUpgradePathId(_staticInfo.Type);
            WvwUpgradeCatalogService.WvwUpgrades.TryGetValue(pathId, out var fullPath);
            List<int> activeTacticIds = state?.GuildUpgrades ?? new List<int>();

            // Building Unlocks (Row 1) -- only hit-tested while it's
            // actually drawn (see ShowBuildingUnlocks). Resolves down to the
            // specific sub-upgrade under the cursor so the tooltip can show
            // that item's own Name/Description, not just the tier's.
            // Assumption: sub-upgrade catalog entries expose Name and
            // Description the same way the tier itself does -- fix the
            // property names here if the model turns out to differ.
            if (ShowBuildingUnlocks && relativeMouse.X >= MatrixLeft
                && relativeMouse.Y >= MatrixTopOffset && relativeMouse.Y < MatrixTopOffset + BuildingIconSize)
            {
                int localX = relativeMouse.X - MatrixLeft;
                int x = 0;
                for (int tierIdx = 0; tierIdx < 3 && fullPath != null && tierIdx < fullPath.Tiers.Count; tierIdx++)
                {
                    var subUpgrades = fullPath.Tiers[tierIdx].SubUpgrades;
                    int colWidth = subUpgrades.Count * BuildingIconSize;
                    if (localX < x + colWidth)
                    {
                        int slot = (localX - x) / BuildingIconSize;
                        if (slot >= 0 && slot < subUpgrades.Count)
                        {
                            var item = subUpgrades[slot];
                            string unlockedText = tierIdx < currentTier ? "Unlocked" : $"Requires {fullPath.Tiers[tierIdx].YaksRequired} yaks";
                            BasicTooltipText = $"{item.Name}\n{item.Description}\n{unlockedText}";
                        }
                        return;
                    }
                    x += colWidth;
                }
            }

            if (relativeMouse.X >= MatrixLeft
                && relativeMouse.Y >= TacticsRowY && relativeMouse.Y < TacticsRowY + TacticIconSize)
            {
                var eligibleTiers = GetEligibleTacticTierIndices(_staticInfo.Type);
                int column = (relativeMouse.X - MatrixLeft) / TacticIconSize; // 0-based across only the drawn columns
                int eligibleTierPos = column / MaxTacticSlotsPerTier;
                if (eligibleTierPos >= 0 && eligibleTierPos < eligibleTiers.Count)
                {
                    int tierIdx = eligibleTiers[eligibleTierPos];
                    int slotInTier = column % MaxTacticSlotsPerTier;
                    int flatSlot = tierIdx * MaxTacticSlotsPerTier + slotInTier; // matches _tacticIconTex/activeTacticIds indexing
                    if (flatSlot < activeTacticIds.Count && WvwUpgradeCatalogService.GuildUpgrades.TryGetValue(activeTacticIds[flatSlot], out var tactic))
                    {
                        BasicTooltipText = $"{tactic.Name}\n{tactic.Description}";
                        return;
                    }
                    if (fullPath != null && tierIdx < fullPath.Tiers.Count)
                    {
                        var tier = fullPath.Tiers[tierIdx];
                        // Guild Tactic slots unlock by TIME HELD (and, for
                        // Tiers II/III, building type), NOT the objective's
                        // structural Tier -- see GetUnlockedTacticTierCount.
                        // This used to say "Unlocks at Tier N" here, which
                        // conflated the two progressions the same way the
                        // painting loop above used to.
                        string unlockedText;
                        if (tierIdx < unlockedTacticTiers)
                        {
                            unlockedText = "Unlocked (empty slot)";
                        }
                        else if (!IsTacticTierEligibleForType(_staticInfo.Type, tierIdx))
                        {
                            unlockedText = "Not available for this structure type";
                        }
                        else
                        {
                            int[] minuteThresholds = { 10, 30, 60 };
                            unlockedText = $"Unlocks after {minuteThresholds[tierIdx]}m held";
                        }
                        BasicTooltipText = $"{tier.Name}\nGuild Tactics\n{unlockedText}";
                        return;
                    }
                }
            }

            BasicTooltipText = $"{_staticInfo.Name}\nWvW Warlord tactical inspection card.";
        }
    }

    /// <summary>CardTiny: condensed color stripe + icon + "Name | timer" single line.</summary>
    public class WvwCardTiny : Panel
    {
        public const int CardWidth = WvwCardStandard.CardWidth; // 250 -- matches the other cards
        public const int CardHeight = 26;

        private readonly WvwLiveDataService _dataService;
        private readonly WvwObjectiveInfo _staticInfo;

        private readonly Panel _stripe;
        private new readonly Image _icon;
        private readonly Label _nameLabel;
        private float? _distance;

        public WvwCardTiny(WvwLiveDataService dataService, string objectiveId, ChatLinkRouter router = null)
        {
            _dataService = dataService;
            WvwCatalogService.ById.TryGetValue(objectiveId, out _staticInfo);

            Size = new Point(CardWidth, CardHeight);
            BackgroundColor = new Color(20, 20, 20) * 0.9f;

            _stripe = new Panel { Parent = this, Location = new Point(0, 0), Size = new Point(4, CardHeight) };

            _icon = new Image { Parent = this, Location = new Point(7, 5), Size = new Point(16, 16) };

            _nameLabel = new Label
            {
                Parent = this,
                Location = new Point(28, 6),
                Size = new Point(CardWidth - 28 - 4, 14),
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.White * 0.9f,
                Text = TrimName(_staticInfo?.Name)
            };

            LeftMouseButtonPressed += (s, e) => { if (_staticInfo != null) _ = router?.RouteAsync(_staticInfo.ChatLink); };
            CardContextMenu.Attach(this, router, () => _staticInfo?.ChatLink);
        }

        private static string TrimName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            return name.Length > 26 ? name.Substring(0, 25) + "…" : name;
        }

        public void SetDistance(float? distance) => _distance = distance;

        /// <summary>Used by TacContentPanel's per-tick distance refresh to map an existing card instance back to its objective without a full rebuild.</summary>
        public string ObjectiveId => _staticInfo?.Id;

        public void Refresh()
        {
            if (_staticInfo == null) return;
            _dataService.LiveStates.TryGetValue(_staticInfo.Id, out var state);
            string owner = state?.Owner ?? "Neutral";
            Color factionColor = WvwStaticData.GetFactionColor(owner);

            // Same split as WvwCardStandard: stripe = home map color (lets
            // you tell columns/maps apart at a glance), icon tint = current
            // owner (the thing you actually want to know per-objective).
            string mapLabel = WvwStaticData.MapLabels.TryGetValue(_staticInfo.MapId, out var mLbl) ? mLbl : null;
            _stripe.BackgroundColor = mapLabel != null ? WvwStaticData.GetFactionColor(mapLabel) : Color.DarkGray;
            _icon.Tint = factionColor;
            _icon.Texture = WvwIconProvider.ForObjective(_staticInfo.Type, state?.HasWaypoint ?? false);

            string timeText;
            if (state == null || state.LastFlipped == DateTime.MinValue || owner.Equals("Neutral", StringComparison.OrdinalIgnoreCase))
            {
                timeText = owner.Equals("Neutral", StringComparison.OrdinalIgnoreCase) ? "Neutral" : "--";
            }
            else
            {
                TimeSpan held = DateTime.UtcNow - state.LastFlipped;
                timeText = held.TotalHours >= 1 ? $"{(int)held.TotalHours}h {held.Minutes}m" : $"{held.Minutes}m";
            }

            string distanceText = _distance.HasValue ? $" | {_distance.Value:N0}" : "";
            _nameLabel.Text = $"{TrimName(_staticInfo.Name)} | {timeText}{distanceText}";
        }
    }

    /// <summary>
    /// One row per guild currently holding at least one claim across the four
    /// WvW maps. Four fixed slots in map order (EBG, Red, Green, Blue) -- a
    /// guild with no current claim on a given map leaves that slot blank, same
    /// "blank space, not compacted" rule QuickTravel's static tray uses, so
    /// columns line up card to card. Each slot's timer is red for its first 5
    /// minutes -- the guild's claim-cooldown window, during which it can't
    /// claim anywhere else. Guild tag trails at the right; gold if it's one of
    /// the player's own guilds.
    /// </summary>
    public class WvwCardGuildActivity : Panel
    {
        public const int CardWidth = WvwCardStandard.CardWidth; // 250
        public const int CardHeight = 44;
        private const int SlotWidth = 44; 
        private const float CooldownWindowMinutes = 5f;

        public WvwCardGuildActivity(string guildTag, bool isMyGuild, (WvwObjectiveInfo obj, LiveObjectiveState state)?[] slots, string[] slotMapLabels, int highlightSlot, ChatLinkRouter router = null)
        {
            Size = new Point(CardWidth, CardHeight);
            BackgroundColor = new Color(20, 20, 20) * 0.9f;

            int x = 0;
            for (int i = 0; i < 4; i++)
            {
                // Faction color stripe
                new Panel { Parent = this, Location = new Point(x, 0), Size = new Point(4, CardHeight), BackgroundColor = WvwStaticData.GetFactionColor(slotMapLabels[i]) };

                int iconSize = 36;
                var icon = new Image { Parent = this, Location = new Point(x + 8, 2), Size = new Point(iconSize, iconSize) };
                var timerBg = new Panel { Parent = this, Location = new Point(x + 8, 2 + iconSize - 14), Size = new Point(iconSize, 14), BackgroundColor = Color.Black * 0.55f };
                var timer = new Label { Parent = this, Location = new Point(x + 10, 2 + iconSize - 14), Size = new Point(iconSize - 4, 14), Font = GameService.Content.DefaultFont14, TextColor = Color.Gray };

                if (slots[i].HasValue)
                {
                    var (obj, state) = slots[i].Value;
                    icon.Texture = WvwIconProvider.ForObjective(obj.Type, state.HasWaypoint);
                    icon.Tint = WvwStaticData.GetFactionColor(state.Owner);

                    TimeSpan held = DateTime.UtcNow - state.LastFlipped;
                    timer.Text = held.TotalHours >= 1 ? $"{(int)held.TotalHours}h{held.Minutes}m" : $"{held.Minutes}m";
                    timer.TextColor = held.TotalMinutes < CooldownWindowMinutes
                        ? Color.Red
                        : (i == highlightSlot ? Color.White : Color.LightGray);

                    // Each slot is its own objective, so each gets its own
                    // click target (added last so it sits on top): left-click
                    // routes the chat link, right-click gives the same
                    // Send to... menu as every other card.
                    var hit = new Panel { Parent = this, Location = new Point(x, 0), Size = new Point(SlotWidth, CardHeight), BasicTooltipText = obj.Name };
                    if (router != null && !string.IsNullOrEmpty(obj.ChatLink))
                    {
                        string link = obj.ChatLink;
                        hit.LeftMouseButtonPressed += (s, e) => _ = router.RouteAsync(link);
                        CardContextMenu.Attach(hit, router, () => link);
                    }
                }
                else
                {
                    timerBg.Visible = false; // no claim in this slot -- don't show a dark strip with nothing on it
                }
                x += SlotWidth;
            }

            new Label
            {
                Parent = this,
                Location = new Point(x + 8, (CardHeight - 16) / 2),
                Size = new Point(CardWidth - x - 12, 16),
                Font = GameService.Content.DefaultFont14,
                Text = string.IsNullOrEmpty(guildTag) ? "?" : guildTag,
                TextColor = isMyGuild ? Color.Gold : Color.White
            };
        }
    }

    /// <summary>CardClaimed: guild-claim log entry with a map-color side stripe. Width is responsive (set via object initializer), not fixed.</summary>
    public class WvwCardClaimed : Panel
    {
        public const int CardHeight = 36;

        private readonly Label _titleLabel;
        private new readonly Image _icon;
        private readonly Panel _stripe;

        public WvwCardClaimed(WvwLiveDataService dataService, WvwObjectiveInfo obj, LiveObjectiveState state, ChatLinkRouter router = null)
        {
            Height = CardHeight;
            Width = 220;
            BackgroundColor = new Color(20, 20, 20) * 0.95f;

            string mapLabel = WvwStaticData.MapLabels.TryGetValue(obj.MapId, out var lbl) ? lbl : "WvW";
            Color stripeColor = WvwStaticData.GetFactionColor(mapLabel);

            _stripe = new Panel { Parent = this, Location = new Point(0, 0), Size = new Point(4, CardHeight) };
            _stripe.BackgroundColor = stripeColor;

            _icon = new Image
            {
                Parent = this,
                Location = new Point(8, 6),
                Size = new Point(24, 24),
                Tint = WvwStaticData.GetFactionColor(state.Owner),
                Texture = WvwIconProvider.ForObjective(obj.Type, state.HasWaypoint)
            };

            _titleLabel = new Label
            {
                Parent = this,
                Location = new Point(38, 10),
                Size = new Point(Width - 46, 16),
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.White,
                Text = $"{obj.Name} (T{state.Tier})"
            };

            Resized += (s, e) =>
            {
                _stripe.Height = Height;
                _titleLabel.Width = Math.Max(20, Width - 46);
            };

            if (router != null && !string.IsNullOrEmpty(obj.ChatLink))
            {
                LeftMouseButtonPressed += (s, e) => _ = router.RouteAsync(obj.ChatLink);
                CardContextMenu.Attach(this, router, () => obj.ChatLink);
            }
        }
    }
}