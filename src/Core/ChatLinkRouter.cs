using System;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework.Input;

namespace WvWarlord.Core
{
    /// <summary>All routing strategies exposed by the Main Control Panel dropdown.</summary>
    public enum ChatLinkRoute
    {
        ClipboardOnly,
        Manual,
        SquadD,
        ShiftEnterSquadD,
        PartyP,
        SayS,
        WhisperUsername,
        ScarMap
    }

    /// <summary>
    /// Central pipe that turns a raw chatlink string into the correct in-game
    /// chat action based on the currently selected <see cref="ChatLinkRoute"/>.
    /// Owns the AutoSend modifier and the low-level key-stroke helpers.
    /// </summary>
    public class ChatLinkRouter
    {
        /// <summary>Currently selected routing strategy, bound to the dropdown in the Main Control Panel.</summary>
        public ChatLinkRoute CurrentRoute { get; set; } = ChatLinkRoute.ClipboardOnly;

        /// <summary>When true, a final Enter keystroke is appended after staging the text.</summary>
        public bool AutoSend { get; set; } = false;

        /// <summary>Account name used to build the "/w username " whisper prefix. Set by the module after preload.</summary>
        public string AccountNameForWhisper { get; set; } = "";

        /// <summary>Callback wired by ScarMapHandoffService (see WvWarlord.Core) so ScarMap-routed links -- along with a CardPro-equivalent info snapshot -- get forwarded to the standalone ScarMap module. No longer a panel's own input box now that ScarMap has moved out of this project.</summary>
        public Action<string> ScarMapTarget { get; set; }

        /// <summary>
        /// Explicit routing used by the card context menu's "Send to Chat" /
        /// "Send to Broadcast" / "Send to Clipboard" / "Send to ScarMap"
        /// entries -- bypasses <see cref="CurrentRoute"/> entirely since the
        /// person picked a specific destination for this one chatlink.
        /// </summary>
        public async Task SendExplicitAsync(string chatLink, ChatLinkRoute explicitRoute)
        {
            var previous = CurrentRoute;
            CurrentRoute = explicitRoute;
            try { await RouteAsync(chatLink); }
            finally { CurrentRoute = previous; }
        }

        public async Task RouteAsync(string chatLink)
        {
            if (string.IsNullOrEmpty(chatLink)) return;

            switch (CurrentRoute)
            {
                case ChatLinkRoute.ClipboardOnly:
                    await Blish_HUD.ClipboardUtil.WindowsClipboardService.SetTextAsync(chatLink);
                    return;

                case ChatLinkRoute.ScarMap:
                    ScarMapTarget?.Invoke(chatLink);
                    return;

                case ChatLinkRoute.Manual:
                    await StageAndFocus(chatLink, prefix: null, useShiftEnter: false);
                    return;

                case ChatLinkRoute.SquadD:
                    await StageAndFocus(chatLink, prefix: "/d ", useShiftEnter: false);
                    return;

                case ChatLinkRoute.ShiftEnterSquadD:
                    await StageAndFocus(chatLink, prefix: "/d ", useShiftEnter: true);
                    return;

                case ChatLinkRoute.PartyP:
                    await StageAndFocus(chatLink, prefix: "/p ", useShiftEnter: false);
                    return;

                case ChatLinkRoute.SayS:
                    await StageAndFocus(chatLink, prefix: "/s ", useShiftEnter: false);
                    return;

                case ChatLinkRoute.WhisperUsername:
                    await StageWhisperAndFocus(chatLink, AccountNameForWhisper);
                    return;
            }
        }

        private async Task StageAndFocus(string chatLink, string prefix, bool useShiftEnter)
        {
            if (!GameService.Gw2Mumble.Info.IsGameFocused) return;

            string payload = string.IsNullOrEmpty(prefix) ? chatLink : prefix + chatLink;

            ReleaseModifierKeys();
            await Task.Delay(10);

            if (useShiftEnter)
            {
                Blish_HUD.Controls.Intern.Keyboard.Press(Blish_HUD.Controls.Extern.VirtualKeyShort.LSHIFT, true);
                await Task.Delay(10);
                Blish_HUD.Controls.Intern.Keyboard.Stroke(Blish_HUD.Controls.Extern.VirtualKeyShort.RETURN, true);
                await Task.Delay(10);
                Blish_HUD.Controls.Intern.Keyboard.Release(Blish_HUD.Controls.Extern.VirtualKeyShort.LSHIFT, true);
            }
            else
            {
                Blish_HUD.Controls.Intern.Keyboard.Stroke(Blish_HUD.Controls.Extern.VirtualKeyShort.RETURN, true);
            }

            bool focusActive = false;
            for (int i = 0; i < 200; i++)
            {
                if (GameService.Gw2Mumble.UI.IsTextInputFocused) { focusActive = true; break; }
                await Task.Delay(5);
            }
            if (!focusActive) return;

            bool staged = await Blish_HUD.ClipboardUtil.WindowsClipboardService.SetTextAsync(payload);
            if (!staged) return;

            await Task.Delay(40);
            await SendKeyCombo(new[] { Keys.LeftControl }, Keys.V, delay: 35);
            await Task.Delay(60);

            if (AutoSend)
            {
                Blish_HUD.Controls.Intern.Keyboard.Stroke(Blish_HUD.Controls.Extern.VirtualKeyShort.RETURN, true);
            }
        }

        private async Task StageWhisperAndFocus(string chatLink, string accountName)
        {
            if (!GameService.Gw2Mumble.Info.IsGameFocused) return;
            if (string.IsNullOrEmpty(accountName)) { await StageAndFocus(chatLink, prefix: "/w ", useShiftEnter: false); return; }

            ReleaseModifierKeys();
            await Task.Delay(10);

            Blish_HUD.Controls.Intern.Keyboard.Stroke(Blish_HUD.Controls.Extern.VirtualKeyShort.RETURN, true); // open chat

            bool focusActive = false;
            for (int i = 0; i < 200; i++)
            {
                if (GameService.Gw2Mumble.UI.IsTextInputFocused) { focusActive = true; break; }
                await Task.Delay(5);
            }
            if (!focusActive) return;

            // Example configuration flag or parameter
            bool messageFirst = true; // Set to true for: /w message, username, tab, enter

            if (messageFirst)
            {
                // --- FLOW A: /w message, username, tab, enter ---
                if (!await Blish_HUD.ClipboardUtil.WindowsClipboardService.SetTextAsync($"/w {chatLink}")) return;
                await Task.Delay(40);
                await SendKeyCombo(new[] { Keys.LeftControl }, Keys.V, delay: 35);
                await Task.Delay(60);

                if (!await Blish_HUD.ClipboardUtil.WindowsClipboardService.SetTextAsync(accountName)) return;
                await Task.Delay(40);
                await SendKeyCombo(new[] { Keys.LeftControl }, Keys.V, delay: 35);
                await Task.Delay(60);
                Blish_HUD.Controls.Intern.Keyboard.Stroke(Blish_HUD.Controls.Extern.VirtualKeyShort.TAB, true);
                await Task.Delay(500);

            }
            else
            {
                // --- FLOW B: /w, username, tab, message, enter ---
                if (!await Blish_HUD.ClipboardUtil.WindowsClipboardService.SetTextAsync($"/w {accountName}")) return;
                await Task.Delay(40);
                await SendKeyCombo(new[] { Keys.LeftControl }, Keys.V, delay: 35);
                await Task.Delay(60);

                Blish_HUD.Controls.Intern.Keyboard.Stroke(Blish_HUD.Controls.Extern.VirtualKeyShort.TAB, true);
                await Task.Delay(60);

                if (!await Blish_HUD.ClipboardUtil.WindowsClipboardService.SetTextAsync(chatLink)) return;
                await Task.Delay(40);
                await SendKeyCombo(new[] { Keys.LeftControl }, Keys.V, delay: 35);
                await Task.Delay(600);
            }

            if (AutoSend)
            {
                Blish_HUD.Controls.Intern.Keyboard.Stroke(Blish_HUD.Controls.Extern.VirtualKeyShort.RETURN, true);
            }
        }

        private static void ReleaseModifierKeys()
        {
            var clearKeys = new[]
            {
                Keys.RightShift, Keys.LeftShift,
                Keys.RightAlt, Keys.LeftAlt,
                Keys.RightControl, Keys.LeftControl
            };
            foreach (var k in clearKeys)
            {
                Blish_HUD.Controls.Intern.Keyboard.Release((Blish_HUD.Controls.Extern.VirtualKeyShort)k, true);
            }
        }

        private static async Task SendKeyCombo(Keys[] modifiers, Keys key, int delay = 25)
        {
            foreach (var mod in modifiers)
                Blish_HUD.Controls.Intern.Keyboard.Press((Blish_HUD.Controls.Extern.VirtualKeyShort)mod, true);
            await Task.Delay(delay);
            Blish_HUD.Controls.Intern.Keyboard.Stroke((Blish_HUD.Controls.Extern.VirtualKeyShort)key, true);
            await Task.Delay(delay);
            foreach (var mod in modifiers)
                Blish_HUD.Controls.Intern.Keyboard.Release((Blish_HUD.Controls.Extern.VirtualKeyShort)mod, true);
        }
    }
}
