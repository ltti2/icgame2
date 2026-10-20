using UnityEngine;

namespace IceCoast.View.UI.Toolkit
{
    /// <summary>
    /// A floating window in the OpenTTD style: caption bar with a close box on the left and shade / pin
    /// (sticky) boxes on the right, a bevelled body and an optional resize grip. Drag the caption to move it.
    ///
    /// Subclasses draw their contents in <see cref="DrawContent"/> using the current layout area. By default a
    /// window sizes its height to fit its contents; once the player resizes it, it keeps their size and
    /// scrolls instead.
    /// </summary>
    public abstract class UiWindow
    {
        public const float CaptionHeight = 14f;
        public const float Padding = 4f;
        public const float GripSize = 9f;

        /// <summary>Identity: opening a window whose key is already open brings that one forward instead.</summary>
        public string Key { get; protected set; }
        public string Title = "";
        public UiScheme Scheme = UiTheme.Brown;
        public Rect Rect;

        public bool Closable = true;
        public bool Resizable;
        public bool Shadeable = true;
        public bool Pinnable = true;
        /// <summary>Sticky windows survive "close all windows" (Delete key), as in OpenTTD.</summary>
        public bool Pinned;
        /// <summary>Rolled up to just the caption bar.</summary>
        public bool Shaded;
        /// <summary>Height follows the contents until the player resizes the window.</summary>
        public bool AutoHeight = true;
        public Vector2 MinSize = new Vector2(100f, 40f);
        /// <summary>The player dragged this window, so it is no longer a "dropdown" attached to its button.</summary>
        public bool UserMoved;

        /// <summary>For dropdowns: the toolbar button that opened it. Clicking it is not an "outside click".</summary>
        public Rect AnchorRect;
        /// <summary>Close when the player clicks anywhere else (menus). Moving the window cancels this.</summary>
        public bool CloseOnOutsideClick;

        /// <summary>Move to free space once the window knows its real size (after its first draw).</summary>
        internal bool PendingPlacement;

        /// <summary>Height of the visible content area this frame. Resizable lists size themselves to it.</summary>
        public float ViewHeight { get; internal set; }

        /// <summary>
        /// Height for a scrolling list inside this window: <paramref name="natural"/> until the player resizes the
        /// window, then whatever fits above <paramref name="footer"/> pixels of other content.
        /// </summary>
        public float ListHeight(float natural, float footer = 0f)
            => AutoHeight ? natural : Mathf.Max(40f, ViewHeight - footer);

        public int LayerId { get; internal set; }
        public bool IsOpen { get; internal set; }
        internal readonly ScrollState BodyScroll = new ScrollState();

        protected UiWindow(string key, string title, float width)
        {
            Key = key;
            Title = title;
            Rect = new Rect(0, 0, width, 80);
        }

        /// <summary>Draws the window's contents into the current layout area.</summary>
        public abstract void DrawContent(UiContext ui);

        /// <summary>Return false to close the window (e.g. the lift it shows was demolished).</summary>
        public virtual bool StillValid() => true;

        /// <summary>Caption text; may change every frame.</summary>
        public virtual string Caption => Title;

        /// <summary>An optional icon button in the caption bar (e.g. "centre view on this"). Null for none.</summary>
        public virtual string CaptionAction => null;
        public virtual string CaptionActionTooltip => null;
        public virtual void OnCaptionAction() { }

        public virtual void OnClosed() { }
        public virtual void OnShown() { }
    }
}
