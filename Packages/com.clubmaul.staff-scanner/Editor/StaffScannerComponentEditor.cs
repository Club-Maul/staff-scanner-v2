// Staff Scanner V2 — custom inspector
// Adds an "Add Plugin" dropdown (so package plugins don't have to be hunted down in the
// Project window) and hides the Universal toggles unless the Beast role is selected.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ClubMaul.StaffScanner.Editor
{
    [CustomEditor(typeof(StaffScannerComponent))]
    public class StaffScannerComponentEditor : UnityEditor.Editor
    {
        // These are drawn manually at the end, gated on Role == Beast.
        private static readonly string[] WorldFeatureFields = { "Slow", "Rumble" };

        // Cached decimation preview — only re-run the (heavy) decimation when sources or amount change.
        private string _previewKey;
        private int _previewBefore, _previewAfter;

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

                if (prop.name == "SourceRenderers") DrawSourceDefaultInfo();
                if (prop.name == "DecimationAmount") DrawDecimationPreview();
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

        // The explicit source list, or the auto-detected body mesh (same pick the build uses) if empty.
        private List<SkinnedMeshRenderer> GetEffectiveSources(out bool autoDetected)
        {
            autoDetected = false;
            var list = new List<SkinnedMeshRenderer>();
            var arr = serializedObject.FindProperty("SourceRenderers");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var smr = arr.GetArrayElementAtIndex(i).objectReferenceValue as SkinnedMeshRenderer;
                if (smr != null && !list.Contains(smr)) list.Add(smr);
            }
            if (list.Count == 0)
            {
                var root = FindAvatarRoot();
                var auto = root != null ? StaffScannerBuilder.AutoDetectBody(root) : null;
                if (auto != null) { list.Add(auto); autoDetected = true; }
            }
            return list;
        }

        private GameObject FindAvatarRoot()
        {
            var comp = (StaffScannerComponent)target;
            var desc = comp.GetComponentInParent<VRCAvatarDescriptor>();
            return desc != null ? desc.gameObject : comp.transform.root.gameObject;
        }

        // When no source is set, tell the user which mesh the build will auto-pick.
        private void DrawSourceDefaultInfo()
        {
            if (serializedObject.isEditingMultipleObjects) return;
            var sources = GetEffectiveSources(out bool autoDetected);
            if (!autoDetected) return; // explicit sources set — nothing to clarify

            if (sources.Count == 0)
                EditorGUILayout.HelpBox("No Source Renderers set, and no body mesh could be auto-detected on this avatar.", MessageType.Warning);
            else
                EditorGUILayout.HelpBox($"No Source Renderers set — will auto-detect \"{sources[0].name}\".", MessageType.Info);
        }

        // Post-decimation triangle count for the current slider value.
        private void DrawDecimationPreview()
        {
            if (serializedObject.isEditingMultipleObjects) return;
            var sources = GetEffectiveSources(out _);
            if (sources.Count == 0) return;

            float amount = serializedObject.FindProperty("DecimationAmount").floatValue;
            UpdatePreview(sources, amount);
            EditorGUILayout.HelpBox($"After decimation: ~{_previewAfter:N0} tris (from {_previewBefore:N0}).", MessageType.None);
        }

        // Runs the real decimator (exact count), cached so idle repaints don't recompute.
        private void UpdatePreview(List<SkinnedMeshRenderer> sources, float amount)
        {
            var key = amount.ToString("F2");
            foreach (var s in sources) key += "|" + (s.sharedMesh != null ? s.sharedMesh.GetInstanceID() : 0);
            if (key == _previewKey) return;

            int before = 0, after = 0;
            foreach (var smr in sources)
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                for (int s = 0; s < mesh.subMeshCount; s++) before += (int)(mesh.GetIndexCount(s) / 3);
                var dec = MeshDecimator.Decimate(mesh, amount);
                after += (int)(dec.GetIndexCount(0) / 3);
                DestroyImmediate(dec);
            }
            _previewKey = key;
            _previewBefore = before;
            _previewAfter = after;
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
