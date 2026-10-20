using IceCoast.View.UI.Toolkit;
using IceCoast.View.UI.Windows;
using UnityEngine;

namespace IceCoast.View.UI
{
    /// <summary>
    /// The bar along the bottom, OpenTTD style: date and time on the left, the latest message in the middle
    /// (click to jump to where it happened, or to open the message log), and money on the right.
    /// </summary>
    public sealed class StatusBar
    {
        public const int LayerId = 2;
        public const float Height = 14f;
        private const float DateWidth = 132f;
        private const float MoneyWidth = 112f;

        private readonly GameUi _game;
        public Rect Bounds { get; private set; }

        public StatusBar(GameUi game) { _game = game; }

        public void Layout(UiContext ui)
        {
            Bounds = new Rect(0, ui.Height - Height, ui.Width, Height);
        }

        public void Draw(UiContext ui)
        {
            var sim = _game.Sim;
            if (sim == null) return;
            ui.BeginLayer(LayerId, UiTheme.Grey);
            var R = ui.R;
            var b = Bounds;
            R.Fill(new Rect(0, b.y - 1, b.width, 1), UiTheme.Outline);

            var datePanel = new Rect(b.x, b.y, DateWidth, b.height);
            var moneyPanel = new Rect(b.xMax - MoneyWidth, b.y, MoneyWidth, b.height);
            var messagePanel = new Rect(datePanel.xMax + 1, b.y, moneyPanel.x - datePanel.xMax - 2, b.height);

            // Date and time
            var time = sim.World.Time;
            R.Bevel(datePanel, UiTheme.Grey, false);
            R.TextCentered(datePanel, $"{time.FormatDate()}  {time.FormatClock()}", UiTheme.TextBlack);

            // Message ticker
            R.Bevel(messagePanel, UiTheme.Grey, true);
            var inner = new Rect(messagePanel.x + 1, messagePanel.y + 1, messagePanel.width - 2, messagePanel.height - 2);
            R.Fill(inner, UiTheme.Black);
            var latest = _game.Messages.Latest;
            float age = latest.HasValue ? Time.unscaledTime - latest.Value.ShownAt : float.MaxValue;
            bool fresh = age < _game.Settings.MessageSeconds;
            string text = null;
            Color32 color = UiTheme.TextWhite;
            if (_game.Runner.Paused && !fresh)
            {
                text = "*  *  PAUSED  *  *";
                color = UiTheme.TextRed;
            }
            else if (latest.HasValue)
            {
                var e = latest.Value.Event;
                text = e.Message;
                color = fresh ? GameUi.SeverityOnDark(e.Severity) : UiTheme.TextGrey;
            }
            if (text != null)
            {
                R.PushClip(inner);
                R.TextCentered(inner, R.Ellipsize(text, inner.width - 8), color);
                R.PopClip();
            }
            bool hasLocation = latest.HasValue && latest.Value.Event.HasLocation;
            ui.SetTooltip(messagePanel, hasLocation ? "Click to go there. Right-click for the message log." : "Click to open the message log.");
            if (ui.Hover(messagePanel) && ui.RightDown) _game.ToggleSingleton(new MessageLogWindow(_game));
            if (ui.Clicked(messagePanel))
            {
                if (hasLocation) _game.Focus(latest.Value.Event.Location);
                else _game.ToggleSingleton(new MessageLogWindow(_game));
            }

            // Money
            R.Bevel(moneyPanel, UiTheme.Grey, false);
            var econ = sim.World.Economy;
            var moneyColor = !sim.Config.SandboxMode && econ.Cash < 0 ? new Color32(176, 0, 0, 255) : UiTheme.TextBlack;
            R.TextCentered(moneyPanel, _game.CashText, moneyColor);
            ui.SetTooltip(moneyPanel, $"Profit today: {_game.Money(econ.Today.Profit)}. Click for finances.");
            if (ui.Clicked(moneyPanel)) _game.ToggleSingleton(new FinanceWindow(_game));
        }
    }
}
