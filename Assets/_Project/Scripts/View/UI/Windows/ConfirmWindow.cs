using System;
using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>"Are you sure?" with Yes / No.</summary>
    public sealed class ConfirmWindow : UiWindow
    {
        private readonly string _message;
        private readonly string _yes;
        private readonly Action _onYes;
        private bool _done;

        public ConfirmWindow(string title, string message, string yes, Action onYes) : base("confirm", title, 240)
        {
            _message = message;
            _yes = yes;
            _onYes = onYes;
            Scheme = UiTheme.Red;
            Pinnable = false;
            Shadeable = false;
        }

        public override bool StillValid() => !_done;

        public override void DrawContent(UiContext ui)
        {
            ui.Paragraph(_message, ui.Scheme.Text);
            ui.Space(4);
            var row = ui.Row(UiContext.ButtonHeight);
            if (ui.Button(UiContext.Cell(row, 0, 2, 6), _yes))
            {
                _done = true;
                _onYes?.Invoke();
            }
            if (ui.Button(UiContext.Cell(row, 1, 2, 6), "Cancel")) _done = true;
        }
    }
}
