using System;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using WvWarlord.Api;
using WvWarlord.Core;
using WvWarlord.UI;

namespace WvWarlord.UI.Features
{
    public class UserDebugContent
    {
        private readonly WvwModuleContext _ctx;
        private readonly Func<Task> _retryPreload;

        private Label _summaryLabel;
        private StandardButton _statusButton;
        private Panel _parentPanel;

        public int Height { get; }

        public UserDebugContent(WvwModuleContext ctx, Func<Task> retryPreload, Panel parent, int width, int y)
        {
            _ctx = ctx;
            _retryPreload = retryPreload;
            _parentPanel = parent;
            Height = 108;

            new Label
            {
                Parent = parent,
                Location = new Point(4, y),
                Size = new Point(width - 8, 16),
                Text = "STATUS",
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.LightGreen
            };

            _summaryLabel = new Label
            {
                Parent = parent,
                Location = new Point(4, y + 18),
                Size = new Point(width - 8, 68),
                Font = GameService.Content.DefaultFont14,
                TextColor = Color.LightGray
            };

            _statusButton = new StandardButton
            {
                Parent = parent,
                Location = new Point(4, y + 82),
                Size = new Point(width - 8, 24),
                Text = "Awaiting preload...",
                Visible = false
            };
            _statusButton.Click += async (s, e) =>
            {
                _statusButton.Text = "Retrying...";
                _statusButton.Enabled = false;
                if (_retryPreload != null) await _retryPreload();
                _statusButton.Enabled = true;
            };

            _ctx.DataService.DataUpdated += (s, e) => RefreshSummary();
            _ctx.DataService.PreloadCompleted += (s, e) => RefreshSummary();
            _ctx.DataService.PreloadFailed += (s, msg) => ShowFailure(msg);

            RefreshSummary();
        }

        private void RefreshSummary()
        {
            try
            {
                _statusButton.Visible = false;

                DateTime lastUpdate = _ctx.DataService.LastUpdateTime.ToLocalTime();

                DateTime lastMatchChange = _ctx.DataService.LastMatchDataChangeTime != DateTime.MinValue
                    ? _ctx.DataService.LastMatchDataChangeTime.ToLocalTime()
                    : lastUpdate;

                TimeSpan elapsedSinceChange = DateTime.Now - lastMatchChange;
                int secondsStale = (int)Math.Max(0, elapsedSinceChange.TotalSeconds);

                _summaryLabel.Text =
                    $"Account: {_ctx.DataService.MyAccountName}\n" +
                    $"Team: {_ctx.DataService.MyTeamId} ({_ctx.DataService.MyTeamColor})\n" +
                    $"Match: {_ctx.DataService.ActiveMatchId}\n" +
                    $"Last Update: {lastUpdate:HH:mm:ss} Delta {secondsStale}s";

                byte red = (byte)Math.Min(255, secondsStale);
                if (_parentPanel != null)
                {
                    _parentPanel.BackgroundColor = new Color(red, 10, 12) * 0.9f;
                }
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"RefreshSummary UI update failed: {ex.Message}");
            }
        }

        private void ShowFailure(string message)
        {
            try
            {
                _summaryLabel.Text = message;
                _statusButton.Text = "Click to Retry";
                _statusButton.Visible = true;
                if (_parentPanel != null)
                {
                    _parentPanel.BackgroundColor = new Color(10, 10, 12) * 0.9f;
                }
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"ShowFailure UI update failed: {ex.Message}");
            }
        }
    }
}