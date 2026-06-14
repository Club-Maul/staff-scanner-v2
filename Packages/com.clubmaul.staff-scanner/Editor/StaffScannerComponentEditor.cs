// Staff Scanner V2 — custom inspector
// Hides the World Feature toggles unless the Beast role is selected.

using UnityEditor;
using UnityEngine;

namespace ClubMaul.StaffScanner.Editor
{
    [CustomEditor(typeof(StaffScannerComponent))]
    public class StaffScannerComponentEditor : UnityEditor.Editor
    {
        // These are drawn manually at the end, gated on Role == Beast.
        private static readonly string[] WorldFeatureFields = { "Slow", "Rumble", "Unique" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var roleProp = serializedObject.FindProperty("Role");

            // Draw every serialized field except the world-feature toggles (and the script ref).
            var prop = serializedObject.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.name == "m_Script") continue;
                if (System.Array.IndexOf(WorldFeatureFields, prop.name) >= 0) continue;
                EditorGUILayout.PropertyField(prop, true);
            }

            // World features are Beast-only — their "World Features" header rides along on the first field.
            if (roleProp.enumValueIndex == (int)StaffRole.Beast)
            {
                foreach (var name in WorldFeatureFields)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
