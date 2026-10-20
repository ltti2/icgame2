using IceCoast.View.Bootstrap;
using IceCoast.View.Config;
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
