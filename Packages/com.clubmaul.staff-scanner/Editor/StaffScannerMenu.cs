// Staff Scanner V2 — menu items for spawning the prefab.
// Because this ships as a VPM package (not under Assets/), users can't drag the
// prefab from the Project window, so these menu entries do it for them.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ClubMaul.StaffScanner.Editor
{
    public static class StaffScannerMenu
    {
        // GUID of "Staff Scanner V2.prefab" at the package root.
        private const string PrefabGuid = "591181ae0b8e2d64c832091b668133fa";

        [MenuItem("Tools/Club Maul/Initialize Staff Scanner", false, 0)]
        private static void InitializeFromToolsMenu()
        {
            SpawnStaffScanner(null);
        }

        // Adding the "GameObject/" prefix makes this show up in the hierarchy
        // right-click context menu (and the top GameObject menu).
        [MenuItem("GameObject/Club Maul/Staff Scanner", false, 10)]
        private static void InitializeFromHierarchy(MenuCommand command)
        {
            SpawnStaffScanner(command.context as GameObject);
        }

        private static void SpawnStaffScanner(GameObject parent)
        {
            var path = AssetDatabase.GUIDToAssetPath(PrefabGuid);
            var prefab = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                EditorUtility.DisplayDialog(
                    "Staff Scanner V2",
                    "Could not find the Staff Scanner V2 prefab. Make sure the " +
                    "Club Maul Staff Scanner package is installed correctly.",
                    "OK");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null)
            {
                return;
            }

            // Always place it at world origin, as the setup instructions expect.
            if (parent != null)
            {
                Undo.SetTransformParent(instance.transform, parent.transform, "Spawn Staff Scanner V2");
            }
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            // Keep it in the scene the user is looking at.
            var targetScene = parent != null
                ? parent.scene
                : SceneManager.GetActiveScene();
            if (targetScene.IsValid() && instance.scene != targetScene)
            {
                SceneManager.MoveGameObjectToScene(instance, targetScene);
            }

            Undo.RegisterCreatedObjectUndo(instance, "Spawn Staff Scanner V2");
            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            EditorSceneManager.MarkSceneDirty(instance.scene);
        }
    }
}
