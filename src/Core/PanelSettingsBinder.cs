using Blish_HUD.Settings;
using Microsoft.Xna.Framework;

namespace WvWarlord.Core
{
    /// <summary>
    /// Persists a FeaturePanelBase's four state flags (plus roaming window
    /// position) as module settings so they survive restarts, and keeps
    /// state in sync afterward. Call once per panel, right after the panel
    /// is fully constructed.
    /// </summary>
    public static class PanelSettingsBinder
    {
        public static void Bind(SettingCollection settings, PanelStateController controller)
        {
            var enabledSetting = settings.DefineSetting($"{controller.PanelId}_Enabled", true);
            var minimizedSetting = settings.DefineSetting($"{controller.PanelId}_Minimized", false);
            var roamingSetting = settings.DefineSetting($"{controller.PanelId}_Roaming", false);
            var battleSetting = settings.DefineSetting($"{controller.PanelId}_Battle", false);

            // Roaming position: two plain ints with a -1 "unset" sentinel,
            // rather than SettingEntry<Point> -- Point round-tripping through
            // Blish HUD's settings serializer isn't something this codebase
            // has verified (KeyBinding is the only confirmed-working
            // non-primitive SettingEntry type here), so this avoids
            // introducing a new unverified one.
            var roamingXSetting = settings.DefineSetting($"{controller.PanelId}_RoamingX", -1);
            var roamingYSetting = settings.DefineSetting($"{controller.PanelId}_RoamingY", -1);

            // Apply saved values first. Location before Roaming: setting
            // Roaming synchronously creates the roaming window via
            // PanelStateController's factory, which reads LastRoamingLocation
            // at that exact moment.
            controller.Enabled = enabledSetting.Value;
            controller.Minimized = minimizedSetting.Value;
            controller.Battle = battleSetting.Value;
            if (roamingXSetting.Value >= 0 && roamingYSetting.Value >= 0)
            {
                controller.LastRoamingLocation = new Point(roamingXSetting.Value, roamingYSetting.Value);
            }
            controller.Roaming = roamingSetting.Value;

            // Keep settings in sync with further in-session toggles --
            // except live-only ones (inline title-bar Minimize/Roaming),
            // which per SillyHuman should change current on-screen state
            // only, not the persisted "load on reboot" default. Changed
            // still fires either way, so AddPanelToggleColumn's checkboxes
            // stay visually in sync with a live-only toggle; this is just
            // the one place that decides whether to write it to disk.
            controller.Changed += (s, e) =>
            {
                if (!e.Persist) return;
                switch (e.PropertyName)
                {
                    case nameof(controller.Enabled): enabledSetting.Value = e.NewValue; break;
                    case nameof(controller.Minimized): minimizedSetting.Value = e.NewValue; break;
                    case nameof(controller.Roaming):
                        roamingSetting.Value = e.NewValue;
                        // Roaming just turned false -> LastRoamingLocation was
                        // just captured (see PanelStateController.Roaming) --
                        // persist it now so it survives to next restart.
                        if (!e.NewValue && controller.LastRoamingLocation.HasValue)
                        {
                            roamingXSetting.Value = controller.LastRoamingLocation.Value.X;
                            roamingYSetting.Value = controller.LastRoamingLocation.Value.Y;
                        }
                        break;
                    case nameof(controller.Battle): battleSetting.Value = e.NewValue; break;
                }
            };
        }
    }
}