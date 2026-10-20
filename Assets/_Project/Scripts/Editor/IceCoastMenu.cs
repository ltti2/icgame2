using IceCoast.View.Bootstrap;
using IceCoast.View.Config;
using IceCoast.View.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IceCoast.EditorTools
{
    /// <summary>Editor menu: one-click scene setup and handy shortcuts.</summary>
    public static class IceCoastMenu
    {
        private const string ConfigPath = "Assets/_Project/Config/DefaultSimConfig.asset";
        private const string SandboxConfigPath = "Assets/_Project/Config/SandboxSimConfig.asset";

        [MenuItem("Ice Coast/Set Up Game Scene", priority = 0)]
        public static void SetUpScene()
        {
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap == null)
            {
                var go = new GameObject("Game");
                bootstrap = go.AddComponent<GameBootstrap>();
                Undo.RegisterCreatedObjectUndo(go, "Create Ice Coast Game");
            }

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                Undo.RegisterCreatedObjectUndo(camGo, "Create Camera");
            }
            cam.orthographic = true;
            cam.transform.SetPositionAndRotation(new Vector3(0, 0, -100), Quaternion.identity);

            var so = new SerializedObject(bootstrap);
            so.FindProperty("config").objectReferenceValue = GetOrCreateConfig(ConfigPath, sandbox: false);
            so.FindProperty("unlitShader").objectReferenceValue = Shader.Find("IceCoast/UnlitVertexColor");
            so.FindProperty("gameCamera").objectReferenceValue = cam;
            so.ApplyModifiedProperties();

            GetOrCreateConfig(SandboxConfigPath, sandbox: true);

            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
            Selection.activeGameObject = bootstrap.gameObject;
            Debug.Log("Ice Coast scene ready. Save the scene (Ctrl+S) and press Play.");
        }

        [MenuItem("Ice Coast/Create Default Config Assets", priority = 20)]
        public static void CreateConfigs()
        {
            Selection.activeObject = GetOrCreateConfig(ConfigPath, sandbox: false);
            GetOrCreateConfig(SandboxConfigPath, sandbox: true);
        }

        [MenuItem("Ice Coast/Open Save Folder", priority = 40)]
        public static void OpenSaveFolder()
        {
            string dir = System.IO.Path.Combine(Application.persistentDataPath, "saves");
            System.IO.Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        // ---------- Art ----------

        [MenuItem("Ice Coast/Art/Export Missing Sprites to PNG", priority = 60)]
        public static void ExportMissingArt() => ExportArt(overwrite: false);

        [MenuItem("Ice Coast/Art/Reset All Sprites to Built-in Art", priority = 61)]
        public static void ResetArt()
        {
            if (!EditorUtility.DisplayDialog("Reset all sprites",
                    "Overwrite every PNG and JSON in StreamingAssets/Art with the built-in art? Your edits will be lost.",
                    "Overwrite", "Cancel"))
                return;
            ExportArt(overwrite: true);
        }

        [MenuItem("Ice Coast/Art/Open Art Folder", priority = 62)]
        public static void OpenArtFolder()
        {
            System.IO.Directory.CreateDirectory(ArtLibrary.RootPath);
            EditorUtility.RevealInFinder(ArtLibrary.RootPath);
        }

        [MenuItem("Ice Coast/Art/Reload Art (Play Mode)", priority = 63)]
        public static void ReloadArt()
        {
            if (!Application.isPlaying) { Debug.Log("Art reloads by itself when you press Play."); return; }
            ArtLibrary.ReloadCurrent();
        }

        private static void ExportArt(bool overwrite)
        {
            int written = ArtLibrary.ExportDefaults(overwrite);
            AssetDatabase.Refresh();
            Debug.Log($"Ice Coast: wrote {written} art files to {ArtLibrary.RootPath}. Edit the PNGs (e.g. in Aseprite); " +
                      "the game loads them on Play and reloads them while playing.");
        }

        private static SimConfigAsset GetOrCreateConfig(string path, bool sandbox)
        {
            var asset = AssetDatabase.LoadAssetAtPath<SimConfigAsset>(path);
            if (asset != null) return asset;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            asset = ScriptableObject.CreateInstance<SimConfigAsset>();
            asset.Config.SandboxMode = sandbox;
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
