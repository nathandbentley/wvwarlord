using System;
using Blish_HUD;
using Blish_HUD.Content;
using Gw2Sharp.WebApi.V2.Models;

namespace WvWarlord.UI
{
    /// <summary>Loads the real GW2 render-service icons once and hands out cached AsyncTexture2D references.</summary>
    public static class WvwIconProvider
    {
        private static AsyncTexture2D _ruins;
        private static AsyncTexture2D _camp;
        private static AsyncTexture2D _tower;
        private static AsyncTexture2D _keep;
        private static AsyncTexture2D _castle;
        private static AsyncTexture2D _waypoint;
        private static AsyncTexture2D _waypointContested;
        private static AsyncTexture2D _dolyak;
        private static bool _loaded = false;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _ruins = GameService.Content.GetRenderServiceTexture("52B43242E55961770D78B80ED77BC764F0E57BF2/1635237");
            _camp = GameService.Content.GetRenderServiceTexture("015D365A08AAE105287A100AAE04529FDAE14155/102532");
            _tower = GameService.Content.GetRenderServiceTexture("ABEC80C79576A103EA33EC66FCB99B77291A2F0D/102531");
            _keep = GameService.Content.GetRenderServiceTexture("DB580419C8AD9449309A96C8E7C3D61631020EBB/102535");
            _castle = GameService.Content.GetRenderServiceTexture("F0F1DA1C807444F4DF53090343F43BED02E50523/102608");
            _waypoint = GameService.Content.GetRenderServiceTexture("32633AF8ADEA696A1EF56D3AE32D617B10D3AC57/157353");
            _waypointContested = GameService.Content.GetRenderServiceTexture("5EF051273B40CFAC4AEA6C1F1D0DA612C1B0776C/102349");
            _dolyak = GameService.Content.GetRenderServiceTexture("0A90B5007662D701123EA8BD22422CD93F0B0C6B/358416");
        }

        public static AsyncTexture2D ForObjectiveType(WvwObjectiveType type)
        {
            EnsureLoaded();
            switch (type)
            {
                case WvwObjectiveType.Ruins: return _ruins;
                case WvwObjectiveType.Mercenary: return _ruins; // Mercenary camps share the Ruins icon.
                case WvwObjectiveType.Resource: return _camp;
                case WvwObjectiveType.Camp: return _camp;
                case WvwObjectiveType.Tower: return _tower;
                case WvwObjectiveType.Keep: return _keep;
                case WvwObjectiveType.Castle: return _castle;
                default: return _ruins;
            }
        }

        /// <summary>
        /// Waypoint Icon Modifier: swaps in the official waypoint texture when
        /// an objective currently has an active waypoint, in place of its
        /// default structure-type icon.
        /// </summary>
        public static AsyncTexture2D ForObjective(WvwObjectiveType type, bool hasActiveWaypoint, bool contested = false)
        {
            EnsureLoaded();
            if (hasActiveWaypoint) return contested ? _waypointContested : _waypoint;
            return ForObjectiveType(type);
        }

        public static AsyncTexture2D Waypoint(bool contested = false)
        {
            EnsureLoaded();
            return contested ? _waypointContested : _waypoint;
        }

        public static AsyncTexture2D Dolyak()
        {
            EnsureLoaded();
            return _dolyak;
        }

        // FATAL CRASH FIX: this previously matched the two path segments
        // right after the domain -- for a real URL like
        // https://render.guildwars2.com/file/59E8C3B.../102478.png that's
        // "file" and the signature hash, NOT the signature/fileId pair
        // GetRenderServiceTexture actually needs. It would then hand
        // "file/59E8C3B..." to GetRenderServiceTexture, which throws
        // ArgumentException on anything that isn't a real signature/fileId
        // pair -- uncaught inside PaintBeforeChildren, that's a fatal
        // crash, not a recoverable exception. Now explicitly matches the
        // literal "file/" segment first, then captures the two segments
        // that actually follow it.
        private static readonly System.Text.RegularExpressions.Regex RenderUrlPattern =
            new System.Text.RegularExpressions.Regex(@"render\.guildwars2\.com/file/([^/]+)/([^/.\s]+)", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// /v2/wvw/upgrades and /v2/guild/upgrades return icons as full
        /// "https://render.guildwars2.com/{signature}/{fileId}.png" URLs, but
        /// GameService.Content.GetRenderServiceTexture expects the bare
        /// "{signature}/{fileId}" pair (the same format used for the hardcoded
        /// icons above). Passing the raw URL straight through silently fails
        /// to resolve a texture -- this parses it into the expected form.
        /// </summary>
        public static AsyncTexture2D FromApiIconUrl(string fullIconUrl)
        {
            if (string.IsNullOrEmpty(fullIconUrl)) return null;

            var match = RenderUrlPattern.Match(fullIconUrl);
            if (!match.Success) return null;

            string signatureAndId = $"{match.Groups[1].Value}/{match.Groups[2].Value}";
            try
            {
                return GameService.Content.GetRenderServiceTexture(signatureAndId);
            }
            catch (ArgumentException)
            {
                // GetRenderServiceTexture throws on anything it doesn't
                // recognize as a real signature/fileId pair. This method is
                // called from WvwCardPro.PaintBeforeChildren -- an uncaught
                // exception there is fatal to the whole overlay, not just
                // this card, so any bad/unexpected URL format needs to fail
                // quietly here instead of propagating.
                return null;
            }
        }
    }
}
