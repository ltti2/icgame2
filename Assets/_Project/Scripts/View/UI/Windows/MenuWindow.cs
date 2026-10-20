using System;
using System.Collections.Generic;
using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>
    /// A toolbar dropdown: a small window of choices that opens right under its button. Picking an item or
    /// clicking elsewhere closes it, like an OpenTTD dropdown. Drag it by the title and it becomes a normal
    /// window that stays open.
    /// </summary>
    public sealed class MenuWindow : UiWindow
    {
        public sealed class Item
        {
            public string Label;
            public string Right;
            public Func<bool> Enabled;
            public Func<bool> Checked;
            public Action Action;
            public bool Separator;
        }

        private readonly List<Item> _items = new List<Item>();

        public MenuWindow(string key, string title, float width) : base("menu:" + key, title, width)
        {
            Scheme = UiTheme.Grey;
            CloseOnOutsideClick = true;
            Shadeable = false;
        }

        public MenuWindow Add(string label, Action action, string right = null, Func<bool> enabled = null, Func<bool> isChecked = null)
        {
            _items.Add(new Item { Label = label, Action = action, Right = right, Enabled = enabled, Checked = isChecked });
            return this;
        }

        public MenuWindow AddSeparator()
        {
            _items.Add(new Item { Separator = true });
            return this;
        }

        /// <summary>Set by the HUD to close the window after a pick.</summary>
        public Action<MenuWindow> Dismiss;

        public override void DrawContent(UiContext ui)
        {
            foreach (var item in _items)
            {
                if (item.Separator) { ui.Separator(); continue; }
                bool enabled = item.Enabled == null || item.Enabled();
                bool on = item.Checked != null && item.Checked();
                string label = (on ? "✓ " : "   ") + item.Label;
                var color = enabled ? ui.Scheme.Text : UiTheme.TextDisabled;
                if (ui.ListRow(label, false, color, item.Right) && enabled)
                {
                    item.Action?.Invoke();
                    if (!UserMoved && !Pinned) Dismiss?.Invoke(this);
                }
            }
        }
    }
}
