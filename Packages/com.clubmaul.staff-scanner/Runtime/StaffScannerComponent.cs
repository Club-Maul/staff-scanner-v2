// Staff Scanner V2 — by Loveseal | v1.0.0

using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace ClubMaul.StaffScanner
{
    public enum StaffRole
    {
        Beast,
        Security,
        Photography
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Club Maul/Staff Scanner V2")]
    public class StaffScannerComponent : MonoBehaviour, IEditorOnly
    {
        public const string Version = "1.0.0";

        [Tooltip("Meshes to duplicate. If empty, auto-detects the avatar's body mesh.")]
        public List<SkinnedMeshRenderer> SourceRenderers = new List<SkinnedMeshRenderer>();

        [Tooltip("Which role's material is applied to the scanner mesh.")]
        public StaffRole Role = StaffRole.Beast;

        [Tooltip("Material for the Beast role.")]
        public Material BeastMaterial;

        [Tooltip("Material for the Security role.")]
        public Material SecurityMaterial;

        [Tooltip("Material for the Photography role.")]
        public Material PhotographyMaterial;

        [Tooltip("Mesh reduction. 0 = lightest, 1 = heaviest.")]
        [Range(0f, 1f)]
        public float DecimationAmount = 0.5f;

        [Tooltip("Name of the generated mesh object (source name appended when multiple).")]
        public string GeneratedObjectName = "StaffScannerMesh";

        [Tooltip("Menu path the toggles are written under. Use slashes for sub-folders.")]
        public string MenuPath = "Staff Scanner V2";

        [Header("Plugins")]
        [Tooltip("Plugin assets (e.g. Suburbia). Each adds world contacts like the World Feature checkboxes.")]
        public List<StaffScannerPlugin> Plugins = new List<StaffScannerPlugin>();

        [Header("Universal")]
        [Tooltip("Add the 'Slow' contact sender.")]
        public bool Slow = false;

        [Tooltip("Add the 'Rumble' contact sender.")]
        public bool Rumble = false;

        /// <summary>
        /// A world feature: object name, collision tag, and whether it's checked. When enabled, the
        /// builder creates a world-locked VRCContactSender with this tag. Add a checkbox + entry to register one.
        /// </summary>
        public readonly struct WorldFeature
        {
            public readonly string ObjectName;
            public readonly string CollisionTag;
            public readonly bool Enabled;
            public WorldFeature(string objectName, string collisionTag, bool enabled)
            {
                ObjectName   = objectName;
                CollisionTag = collisionTag;
                Enabled      = enabled;
            }
        }

        public List<WorldFeature> GetWorldFeatures() => new List<WorldFeature>
        {
            new WorldFeature("Slow",   "ClubMaul/Slow",   Slow),
            new WorldFeature("Rumble", "ClubMaul/Rumble", Rumble),
        };

        /// <summary>Menu path the toggles are written under, falling back to the default if blank.</summary>
        public string GetMenuPath() =>
            string.IsNullOrWhiteSpace(MenuPath) ? "Staff Scanner V2" : MenuPath.Trim().Trim('/');

        public Material GetMaterialForRole()
        {
            switch (Role)
            {
                case StaffRole.Security:    return SecurityMaterial;
                case StaffRole.Photography: return PhotographyMaterial;
                default:                    return BeastMaterial;
            }
        }
    }
}
