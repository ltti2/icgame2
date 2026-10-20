using IceCoast.View.Tools;
using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>
    /// The dropdown under a construction button (Build Lift, Paint Run, Terraform, Clear Trees): the tool's
    /// options plus a live line saying what a click will do and what it costs. Open while the tool is
    /// active; closing it puts the tool away.
    /// </summary>
    public sealed class ToolOptionsWindow : UiWindow
    {
        private readonly GameUi _game;
        public ToolBase Tool { get; }

        public static string KeyFor(ToolBase tool) => "tool:" + tool.Name;

        public ToolOptionsWindow(GameUi game, ToolBase tool) : base(KeyFor(tool), tool.Name, 230)
        {
            _game = game;
            Tool = tool;
            Scheme = UiTheme.DarkGreen;
        }

        public override bool StillValid() => _game.Tools.Active == Tool;

        public override void OnClosed()
        {
            if (_game.Tools.Active == Tool) _game.Tools.Activate(_game.Tools.Inspect);
        }

        public override void DrawContent(UiContext ui)
        {
            var ctx = _game.Tools.Context;
            Tool.OnOptionsUi(ui, ctx);
            if (!string.IsNullOrEmpty(ctx.Status) && ctx.Status != Tool.Help)
            {
                ui.Separator();
                ui.Paragraph(ctx.Status, ctx.StatusIsError ? UiTheme.TextRed : UiTheme.TextYellow);
            }
        }
    }
}
