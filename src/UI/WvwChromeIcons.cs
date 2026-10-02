using Blish_HUD;
using Blish_HUD.Content;

namespace WvWarlord.UI
{
    /// <summary>
    /// Native window-chrome icons, loaded once and shared by every FeaturePanelBase title bar.
    /// All load through GameService.Content.DatAssetCache.GetTextureFromAssetId.
    ///
    /// IDs below are as visually verified against the actual GW2 asset
    /// registry (not just "the API call didn't error"):
    ///   - Close (156012): confirmed correct.
    ///   - Minimize/Restore: previously 156011/156004, both wrong -- 156011
    ///     turned out to be a GOLD CLOSE button (an X, not a minimize glyph;
    ///     this is exactly why the old minimize button rendered as a yellow
    ///     X), and 156004 didn't resolve to anything. Replaced with the
    ///     bank-window convention: a down arrow (155929) for Minimize and a
    ///     right arrow (155909) for Restore.
    ///   - Gear/Settings (155052): confirmed correct; a brighter variant
    ///     (157110) exists too if a hover/active state is ever added.
    ///   - Maximize (156003) and Battle (156133): both confirmed WRONG
    ///     (156003 isn't a maximize icon; 156133 is an unrelated plain X) --
    ///     left as-is below since neither is currently rendered anywhere
    ///     (their title-bar buttons were removed already), but don't wire
    ///     either of these up without picking a real verified ID first.
    ///   - Popout (156017): actually a shield crest, not a real popout icon,
    ///     but kept for now by choice, not because it's confirmed correct.
    /// </summary>
    public static class WvwChromeIcons
    {
        private static AsyncTexture2D _close;
        private static AsyncTexture2D _minimize;
        private static AsyncTexture2D _maximize;
        private static AsyncTexture2D _restore;
        private static AsyncTexture2D _battle;
        private static AsyncTexture2D _popout;
        private static AsyncTexture2D _gear;
        private static bool _loaded;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            _close = GameService.Content.DatAssetCache.GetTextureFromAssetId(156012);
            _minimize = GameService.Content.DatAssetCache.GetTextureFromAssetId(155929); // down arrow
            _maximize = GameService.Content.DatAssetCache.GetTextureFromAssetId(156003); // confirmed wrong, unused -- don't wire up as-is
            _restore = GameService.Content.DatAssetCache.GetTextureFromAssetId(155909); // right arrow
            _battle = GameService.Content.DatAssetCache.GetTextureFromAssetId(156133); // confirmed wrong (plain X), unused -- don't wire up as-is
            _popout = GameService.Content.DatAssetCache.GetTextureFromAssetId(156017); // shield crest, kept by choice for now
            _gear = GameService.Content.DatAssetCache.GetTextureFromAssetId(155052);
        }

        public static AsyncTexture2D Close { get { EnsureLoaded(); return _close; } }
        public static AsyncTexture2D Minimize { get { EnsureLoaded(); return _minimize; } }
        public static AsyncTexture2D Maximize { get { EnsureLoaded(); return _maximize; } }
        public static AsyncTexture2D Restore { get { EnsureLoaded(); return _restore; } }
        public static AsyncTexture2D Battle { get { EnsureLoaded(); return _battle; } }
        public static AsyncTexture2D Popout { get { EnsureLoaded(); return _popout; } }
        public static AsyncTexture2D Gear { get { EnsureLoaded(); return _gear; } }
    }
}