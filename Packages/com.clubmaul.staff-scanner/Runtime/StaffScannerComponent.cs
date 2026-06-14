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

        [Tooltip("SkinnedMeshRenderers to duplicate. If left empty, the builder auto-detects the avatar's body mesh (via VisemeSkinnedMesh, then by vertex count).")]
        public List<SkinnedMeshRenderer> SourceRenderers = new List<SkinnedMeshRenderer>();

        [Tooltip("Which role's material gets applied to the generated scanner mesh(es).")]
        public StaffRole Role = StaffRole.Beast;

        [Tooltip("Material used when Role = Beast.")]
        public Material BeastMaterial;

        [Tooltip("Material used when Role = Security.")]
        public Material SecurityMaterial;

        [Tooltip("Material used when Role = Photography.")]
        public Material PhotographyMaterial;

        [Tooltip("How heavily to reduce the mesh. 0 = lightest decimation (highest poly), 1 = heaviest. Maps internally to a vertex-cluster cell size of 0.01–0.1 source-mesh units.")]
        [Range(0f, 1f)]
        public float DecimationAmount = 0.5f;

        [Tooltip("Base name of the generated GameObject(s) (parented next to each source mesh). When multiple sources are processed, the source mesh's name is appended.")]
        public string GeneratedObjectName = "StaffScannerMesh";

        [Header("World Features")]
        [Tooltip("Apply the 'Slow' contact sender to your avatar so the Club Maul world can react to it. (Placeholder feature.)")]
        public bool Slow = false;

        [Tooltip("Apply the 'Rumble' contact sender to your avatar so the Club Maul world can react to it. (Placeholder feature.)")]
        public bool Rumble = false;

        [Tooltip("Apply the 'Unique' contact sender to your avatar so the Club Maul world can react to it.")]
        public bool Unique = false;

        /// <summary>
        /// Defines a world feature: the GameObject name to create, the contact-sender collision tag
        /// the Club Maul world listens for, and whether the user checked it. When Enabled, the builder
        /// generates a world-locked VRCContactSender with this tag at build time. Add a checkbox above
        /// + an entry here to register a new feature.
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
            new WorldFeature("Unique", "ClubMaul/Unique", Unique),
        };

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
