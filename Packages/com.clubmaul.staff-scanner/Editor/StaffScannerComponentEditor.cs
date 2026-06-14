// Staff Scanner V2 — custom inspector
// Adds an "Add Plugin" dropdown (so package plugins don't have to be hunted down in the
// Project window) and hides the Universal toggles unless the Beast role is selected.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ClubMaul.StaffScanner.Editor
{
    [CustomEditor(typeof(StaffScannerComponent))]
    public class StaffScannerComponentEditor : UnityEditor.Editor
    {
        // These are drawn manually at the end, gated on Role == Beast.
        private static readonly string[] WorldFeatureFields = { "Slow", "Rumble" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var roleProp = serializedObject.FindProperty("Role");
            bool isBeast = roleProp.enumValueIndex == (int)StaffRole.Beast;

            // Draw every serialized field except the world-feature toggles (and the script ref).
            // Plugins are Beast-only, like the Universal features.
            var prop = serializedObject.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.name == "m_Script") continue;
                if (System.Array.IndexOf(WorldFeatureFields, prop.name) >= 0) continue;
                if (prop.name == "Plugins" && !isBeast) continue;
                EditorGUILayout.PropertyField(prop, true);

                // The add-from-anywhere dropdown rides directly under the Plugins list.
                if (prop.name == "Plugins") DrawAddPluginButton();
            }

            // Universal features are Beast-only — their "Universal" header rides along on the first field.
            if (isBeast)
            {
                foreach (var name in WorldFeatureFields)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAddPluginButton()
        {
            var rect = EditorGUILayout.GetControlRect();
            if (EditorGUI.DropdownButton(rect, new GUIContent("Add Plugin"), FocusType.Keyboard))
                ShowAddPluginMenu();
        }

        // Lists every StaffScannerPlugin asset in the project (packages included) that isn't
        // already in the list, plus an option to create a fresh one.
        private void ShowAddPluginMenu()
        {
            var plugins = serializedObject.FindProperty("Plugins");

            var already = new HashSet<Object>();
            for (int i = 0; i < plugins.arraySize; i++)
                already.Add(plugins.GetArrayElementAtIndex(i).objectReferenceValue);

            var menu = new GenericMenu();
            bool any = false;
            foreach (var guid in AssetDatabase.FindAssets("t:StaffScannerPlugin"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var plugin = AssetDatabase.LoadAssetAtPath<StaffScannerPlugin>(path);
                if (plugin == null || already.Contains(plugin)) continue;
                any = true;
                menu.AddItem(new GUIContent(plugin.name), false, () => AddPlugin(plugin));
            }
            if (!any) menu.AddDisabledItem(new GUIContent("No unused plugins found"));

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Create New Plugin..."), false, CreateAndAddPlugin);
            menu.ShowAsContext();
        }

        private void AddPlugin(StaffScannerPlugin plugin)
        {
            var plugins = serializedObject.FindProperty("Plugins");
            int i = plugins.arraySize;
            plugins.InsertArrayElementAtIndex(i);
            plugins.GetArrayElementAtIndex(i).objectReferenceValue = plugin;
            serializedObject.ApplyModifiedProperties();
        }

        private void CreateAndAddPlugin()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Staff Scanner Plugin", "New Plugin", "asset", "Choose where to save the plugin.");
            if (string.IsNullOrEmpty(path)) return;

            var plugin = CreateInstance<StaffScannerPlugin>();
            AssetDatabase.CreateAsset(plugin, path);
            AssetDatabase.SaveAssets();
            AddPlugin(plugin);
        }
    }
}
