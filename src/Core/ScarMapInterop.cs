using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Blish_HUD;
using Blish_HUD.Controls;
using WvWarlord.Api;

namespace WvWarlord.Core
{
    /// <summary>
    /// Hand-off point to the standalone ScarMap module now that ScarMapPanel
    /// has moved out of this project entirely. WvWarlord must NOT take a
    /// compile-time reference to ScarMap's assembly (that would defeat the
    /// point of splitting it out), so this looks up ScarMap's public inbox
    /// type by name via reflection, once, and caches the result.
    ///
    /// If ScarMap isn't installed or isn't loaded, resolution just fails and
    /// Send() no-ops (with a notification) instead of throwing -- WvWarlord
    /// has to keep working whether or not ScarMap happens to be present.
    ///
    /// NOTE: the exact type/method names below are a CONTRACT PROPOSAL, not
    /// something verified against real cross-module behavior in this Blish
    /// HUD version -- confirm ScarMap.Interop.ScarMapInbox.Receive(string)
    /// (see that project) is actually reachable this way once both modules
    /// are loaded side by side; if reflection across module AppDomains/load
    /// contexts doesn't work the way assumed here, this is the one spot that
    /// needs to change.
    /// </summary>
    public static class ScarMapInterop
    {
        private const string InboxTypeFullName = "ScarMap.Interop.ScarMapInbox";
        private const string InboxMethodName = "Receive";

        private static MethodInfo _cachedMethod;
        private static bool _lookupAttempted;

        /// <summary>Whether ScarMap's inbox was found via reflection. Unlike Send(), this never shows a notification -- use it to gate work that shouldn't even attempt a hand-off (e.g. ScarMapHandoffService.Tick's auto-destination) when ScarMap simply isn't installed.</summary>
        public static bool IsAvailable => ResolveInboxMethod() != null;

        /// <summary>Everything ScarMap needs to display and navigate to a destination, so it never needs its own copy of WvWarlord's catalog/live-data services. Mirrors what WvwCardPro shows.</summary>
        public class ScarMapHandoff
        {
            public string ChatLink { get; set; }
            public string ObjectiveId { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public int MapId { get; set; }
            public string MapLabel { get; set; }
            public float CoordX { get; set; }
            public float CoordY { get; set; }
            public string Owner { get; set; } = "Neutral";
            public int Tier { get; set; }
            public int YaksDelivered { get; set; }
            public bool IsContested { get; set; }
            public string ClaimedByGuildTag { get; set; }
            public List<int> ActiveGuildTacticIds { get; set; } = new List<int>();
            public bool HasWaypoint { get; set; }
            public DateTime? LastFlippedUtc { get; set; }
        }

        /// <summary>Serializes and forwards a destination to ScarMap, if it's present. Returns false (and shows a notification) if ScarMap couldn't be found.
        /// NOTE: ScreenNotification.ShowNotification(string, NotificationType, Texture2D, float) is assumed to match this Blish HUD version's real signature -- not independently re-verified here, just carried over from the original ScarMapPanel's usage.</summary>
        public static bool Send(ScarMapHandoff handoff)
        {
            var method = ResolveInboxMethod();
            if (method == null)
            {
                ScreenNotification.ShowNotification("ScarMap module not found -- install/enable it to receive destinations.", ScreenNotification.NotificationType.Error, null, 4);
                return false;
            }

            try
            {
                string json = JsonSerializer.Serialize(handoff);
                method.Invoke(null, new object[] { json });
                return true;
            }
            catch (Exception ex)
            {
                ScreenNotification.ShowNotification("Failed to hand off destination to ScarMap.", ScreenNotification.NotificationType.Error, null, 4);
                ApiCallTracker.Log($"ScarMapInterop.Send failed: {ex.Message}");
                return false;
            }
        }

        private static MethodInfo ResolveInboxMethod()
        {
            if (_lookupAttempted) return _cachedMethod;
            _lookupAttempted = true;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type inboxType;
                try { inboxType = asm.GetType(InboxTypeFullName, throwOnError: false); }
                catch { continue; } // a handful of assemblies throw on GetType lookups for unrelated reasons -- just skip them

                if (inboxType == null) continue;

                _cachedMethod = inboxType.GetMethod(InboxMethodName, BindingFlags.Public | BindingFlags.Static);
                if (_cachedMethod != null) break;
            }

            return _cachedMethod;
        }
    }
}