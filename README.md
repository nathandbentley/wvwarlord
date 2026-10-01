# wvwarlord
A Blish HUD module that tracks World vs World objectives, waypoints and guild claims in one window.

# WvWarlord User Guide

A Blish HUD module that tracks World vs World objectives, waypoints and guild claims in one window. This guide follows the order you will meet things in: the Blish HUD page, the in-window Settings tab, each panel, then quick travel from the Blish icon.

[1. Blish HUD settings](#blish) [2. Module settings](#settings) [3. The panels](#panels) [4. Quick travel from the icon](#travel)

## 1. Blish HUD settings

Blish HUD › Settings › Manage Modules › WvWarlord

This is the only page Blish HUD itself shows for the module. It holds the few things the module needs before it can run.

| Setting | What it does |
| --- | --- |
| Open WvWarlord Settings | Opens the WvWarlord window straight on its Settings tab (section 2). |
| GW2 API Access Key | Required. The module uses it to find your WvW team and match. Create the key at account.arena.net with at least the **account** and **wvw** permissions; add **guilds** if you want your guild tags and claims to show. |
| Reset to Defaults | Resets every setting except your API key. |
| Window Hotkey | Opens and closes the main window. Default Shift + B. |
| Enable Dev Debug Window | Turns on a diagnostics window (API call counters, raw log). Leave off unless you are troubleshooting. |

Until the key is accepted, the window shows *“Waiting on preload”* and the panels do not appear. Once the key works they build themselves automatically.

The **WvWarlord icon** on Blish HUD's top bar does two things. Left-click opens or closes the main window, which stays closed when Blish HUD starts. Right-click opens the quick-travel menu (section 4).

## 2. Module settings

WvWarlord window › Settings tab (gear icon, bottom of the tab strip)

Everything else is set here. Each choice saves as you make it and is what the module loads with next time. Use **Close** at the bottom to go back.

### Tac Overview

| Option | Choices |
| --- | --- |
| View | Single Map, All Map Grid, Detail Grid, Guild |
| Filter | All, My Color, Not My Color |
| Sort | Building Type, Player Distance |

### Quick Travel

**Filter** controls which waypoint cards are listed: *My Map & Color* (current map *or* your color), *My Color*, *My Map*, or *All*.

### Guild Claims

No filters, only the panel toggles below.

### Panel toggles

Tac Overview, Quick Travel and Guild Claims each have the same four checkboxes.

| Toggle | Effect |
| --- | --- |
| Enabled | Shows or hides the panel entirely. |
| Minimized | Collapses the panel to its title bar. |
| Popped Out | Detaches the panel into its own floating window. |
| Stay in Battle | Keeps the panel fully visible in combat. Without it the panel fades to 15% opacity while you fight. |

Changes made directly on a panel (clicking its title bar to minimize, the pop-out icon, or the docked View and Filter dropdowns) apply to the current session only. Only the Settings tab changes what loads next time.

### Core Settings

| Option | What it does |
| --- | --- |
| ChatLink Handling | What happens when you click a card or a quick-travel entry. **Clipboard Only** (default) copies the link. **Manual** opens chat and pastes it. **/d (Squad)**, **Shift+Enter /d**, **/p (Party)** and **/s (Say)** open chat and paste it with that prefix. **/w \[username\]** whispers it to your own account. **ScarMap** sends it to the ScarMap module. The chat options only work while the game window is focused. |
| Auto-Send | Presses Enter after pasting, so the message goes out without a second key press. |
| ScarMap Filter | Which objectives Auto-Destination may pick: All Structures, Only Enemy (enemy-held or neutral), Only Friendly. |
| Auto-Destination | On by default. While you are on a WvW map and ScarMap is installed, WvWarlord sends the closest eligible objective to ScarMap and updates it as you move. |

ScarMap is not part of this module. It is a separate Blish HUD module that must be installed on its own. WvWarlord only sends objectives to it.

## 3. The panels

WvWarlord window

The left column holds Tac Overview controls, Quick Travel and Guild Claims, with a Status block pinned underneath. The right side shows the card grid for whichever tab is selected. Tabs, top to bottom: None / Hide TAC Single Map All Maps Grid Detail Grid Guild Settings.

Every panel has a title bar with a **minimize** icon and a **pop-out** icon; clicking the title also minimizes. A popped-out panel is a draggable window that remembers where you left it and closes back into the main window with its X. Popped-out panels hide when you are off a WvW map and the main window is closed.

### Tac Overview

Two dropdowns (View, Filter) and four map buttons: **EBG, Red, Green, Blue**. The map buttons choose which map the grid shows. When you are not on a WvW map it defaults to your own team's borderland.

- **Single Map** lists one map's objectives. **All Maps Grid** shows all four side by side, current map first. **Detail Grid** uses larger cards that add guild tactic slots, eight per column.
- **Guild** shows one row per guild that holds a claim, with four slots (EBG, Red, Green, Blue). This gives an idea of which guilds have been active recently.

**Reading a card.** The side stripe indicates which map the objective is on. The icon is tinted by who owns it now. The second line reads T2 | Yaks 4 | 12m (tier, yaks delivered, time held), or Locked 03:12 in red for the first 5 minutes after a flip, or Neutral. A guild tag appears after the name when the objective is claimed. With **Player Distance** sorting, a distance is appended and updates about twice a second while the list re-sorts every 15 seconds. Distances need Blish HUD's Mumble link to be connected.

**Clicking cards.** Left-click sends the objective's chat link using your ChatLink Handling choice. Right-click gives a fixed menu that ignores that setting: Send to Chat (/s), Send to Broadcast (/d), Send to Clipboard, Send to ScarMap.

### Quick Travel

The top strip is a tray of waypoint icons for each map. Hover for the name, click to send the link to the chat.

Below the tray is a list of every objective with a working waypoint right now, filtered by your Quick Travel Filter. Emergency waypoints are not shown here. Each card shows the name and how long it has been held.

### Guild Claims

For each guild on your account that currently holds claims, a gold TAG Guild Name header is followed by a card per claimed objective with its tier, e.g. *Name (T3)*.

### Status

Pinned at the bottom of the left column: your account, team and color, match ID, and the time of the last update with seconds since the match data last changed. Data refreshes about every 15 seconds. The block's background turns redder the longer Guild Wars 2 is not providing updated data.

## 4. Quick travel from the icon

Right-click the WvWarlord icon on Blish HUD's top bar

You can travel (send link to chat) without opening the window. The menu lists waypoints grouped by map, **your home map first**, then EBG, then the other two borderlands. Group headers are labels and cannot be clicked.

-- BLUE (HOME) --

Home Citadel

Garrison Keep

────────────

-- EBG --

Blue Spawn

Valley Keep

────────────

-- RED MAP --

Red Border

────────────

-- GREEN MAP --

Green Border

- **Always shown:** one waypoint per map. That is your Home Citadel on your own map, your spawn on EBG, and the border waypoint on the other two.
- **Shown only while you hold it:** your home Garrison keep on your own map and your team's EBG keep.
- **Added automatically:** any other keep on that map that your team owns at Tier 3 with a working waypoint, listed by name.

Clicking an entry **does not teleport you**. It sends that waypoint's chat link using your ChatLink Handling setting. With the default, *Clipboard Only*, the link is copied and you paste it into chat, then click it to travel. Set it to */s*, */p* or */d* and the link is typed into chat for you, or to *Manual* to have chat opened with the link ready.

Written from the WvWarlord source as uploaded. Behavior in a later build may differ.
