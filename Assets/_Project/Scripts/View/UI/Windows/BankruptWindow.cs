using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>Game over. Can't be closed; the only way out is a new game.</summary>
    public sealed class BankruptWindow : UiWindow
    {
        public const string WindowKey = "bankrupt";
        private readonly GameUi _game;

        public BankruptWindow(GameUi game) : base(WindowKey, "Bankrupt!", 260)
        {
            _game = game;
            Scheme = UiTheme.Red;
            Closable = false;
            Pinnable = false;
            Shadeable = false;
            Pinned = true;
        }

        public override bool StillValid() => _game.Sim != null && _game.Sim.World.Economy.Bankrupt;

        public override void DrawContent(UiContext ui)
        {
            ui.Paragraph("The bank has taken the mountain back. Better luck next season.", ui.Scheme.Text);
            ui.Space(4);
            var row = ui.Row(UiContext.ButtonHeight);
            if (ui.Button(UiContext.Cell(row, 0, 2, 6), "New game")) _game.NewGame?.Invoke(false);
            if (ui.Button(UiContext.Cell(row, 1, 2, 6), "Load game")) _game.QuickLoad();
        }
    }
}
