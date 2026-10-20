using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>Every message sent to the player, newest first. Click one to jump to where it happened.</summary>
    public sealed class MessageLogWindow : UiWindow
    {
        public const string WindowKey = "messages";
        private readonly GameUi _game;
        private readonly ScrollState _scroll = new ScrollState();

        public MessageLogWindow(GameUi game) : base(WindowKey, "Messages", 380)
        {
            _game = game;
            Scheme = UiTheme.Grey;
            Resizable = true;
            MinSize = new UnityEngine.Vector2(200f, 90f);
        }

        public override void DrawContent(UiContext ui)
        {
            var history = _game.Messages.History;
            if (history.Count == 0)
            {
                ui.Label("No messages yet.");
                return;
            }

            ui.BeginScroll(_scroll, ListHeight(200f, ui.R.LineHeight + UiContext.RowSpacing));
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var e = history[i];
                string text = string.IsNullOrEmpty(e.When) ? e.Event.Message : $"{e.When}  {e.Event.Message}";
                if (ui.ListRow(text, false, GameUi.SeverityOnLight(e.Event.Severity)) && e.Event.HasLocation)
                    _game.Focus(e.Event.Location);
            }
            ui.EndScroll(_scroll);
            ui.Label("Click a message to jump to it.", UiTheme.TextGrey);
        }
    }
}
