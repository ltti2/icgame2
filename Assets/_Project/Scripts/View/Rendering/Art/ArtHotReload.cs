using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Watches StreamingAssets/Art while the game runs: save a sprite sheet in Aseprite (or any editor) and the
    /// game picks it up within a second, without restarting. Pixel edits are redrawn in place; if a sprite
    /// changed size, renderers with cached meshes rebuild them (<see cref="WorldAtlas.LayoutChanged"/>).
    /// </summary>
    public sealed class ArtHotReload : MonoBehaviour
    {
        [Tooltip("Seconds between checks of the art folder.")]
        [SerializeField] private float checkInterval = 1f;

        private long _stamp;
        private float _nextCheck;

        private void Start()
        {
            _stamp = ArtLibrary.FilesStamp();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + Mathf.Max(0.25f, checkInterval);

            long stamp = ArtLibrary.FilesStamp();
            if (stamp == _stamp) return;
            _stamp = stamp;

            var art = ArtLibrary.ReloadCurrent();
            Debug.Log($"Ice Coast: reloaded art from {ArtLibrary.RootPath} ({art.FilesLoaded} files).");
        }
    }
}
