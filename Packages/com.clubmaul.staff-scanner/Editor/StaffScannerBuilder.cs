// Staff Scanner V2 — Build-time generator
// by Loveseal | v1.0.0

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using com.vrcfury.api;

namespace ClubMaul.StaffScanner.Editor
{
    public class StaffScannerBuilder : IVRCSDKPreprocessAvatarCallback
    {
        private const string ShowParam  = "ClubMaulShow";

        // Resolved from Misc/World.prefab's GUID so it follows the package if it's moved/renamed.
        private static string TempFolder
        {
            get
            {
                var anchor = AssetDatabase.GUIDToAssetPath(WorldAnchorGuid);
                if (string.IsNullOrEmpty(anchor)) return "Assets/Club Maul/Staff Scanner V2/Misc/_TempBuild";
                return System.IO.Path.GetDirectoryName(anchor).Replace('\\', '/') + "/_TempBuild";
            }
        }

        // Must run before VRCFury's main pass (-10000); otherwise the
        // FullController we attach below is never picked up.
        public int callbackOrder => -11000;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var components = avatarRoot.GetComponentsInChildren<StaffScannerComponent>(true);
            if (components.Length == 0) return true;

            Debug.Log($"[StaffScanner] Preprocessing avatar: {avatarRoot.name} ({components.Length} component(s))");
            CleanTempFolder();

            foreach (var comp in components)
            {
                try { Process(avatarRoot, comp); }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Debug.LogError($"[StaffScanner] Build failed for component on {comp.gameObject.name}.");
                }
                UnityEngine.Object.DestroyImmediate(comp);
            }
            return true;
        }

        private static void Process(GameObject avatarRoot, StaffScannerComponent comp)
        {
            // World features are independent of the scanner mesh, so apply them first —
            // before any early-out below can skip the rest of the build.
            ApplyWorldFeatures(comp);

            var material = comp.GetMaterialForRole();
            if (material == null)
            {
                Debug.LogWarning($"[StaffScanner] No material assigned for role {comp.Role} on {comp.gameObject.name}. Skipping.");
                return;
            }

            var sources = (comp.SourceRenderers ?? new List<SkinnedMeshRenderer>())
                .Where(s => s != null)
                .Distinct()
                .ToList();
            if (sources.Count == 0)
            {
                var auto = AutoDetectBody(avatarRoot);
                if (auto != null) sources.Add(auto);
            }
            if (sources.Count == 0)
            {
                Debug.LogWarning($"[StaffScanner] No source SkinnedMeshRenderer found on {avatarRoot.name}. Skipping.");
                return;
            }

            bool multi = sources.Count > 1;
            var generated = new List<GameObject>();
            foreach (var source in sources)
            {
                var go = BuildOne(comp, source, material, multi);
                if (go != null) generated.Add(go);
            }
            if (generated.Count == 0) return;

            // Default-off avoids a one-frame flash on load before the FX layer settles.
            foreach (var go in generated) go.SetActive(false);

            var controller = BuildAnimatorController(avatarRoot, generated);

            var expParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name          = ShowParam,
                    valueType     = VRCExpressionParameters.ValueType.Bool,
                    saved         = false,
                    defaultValue  = 0f,
                    networkSynced = true
                }
            };

            var fc = FuryComponents.CreateFullController(avatarRoot);
            fc.AddController(controller, VRCAvatarDescriptor.AnimLayerType.FX);
            fc.AddParams(expParams);
            // Global so VRCFury doesn't rename it — keeps other Club Maul tools in sync.
            fc.AddGlobalParam(ShowParam);
        }

        // Everything contact-related is regenerated at build time under a world-locked "Contacts" group:
        //  - "Receiver" + "Sender" + "Contacts" are always built (the scanner's core).
        //  - each checked World Feature (Slow/Rumble/Unique) adds its own sender.
        // Every sender/receiver gets a VRCFury menu Toggle under the Staff Scanner V2 menu that turns it
        // on/off. The contacts sit at world origin (the VRCParentConstraint trick) so all scanner users'
        // contacts coincide there.
        private const float  SenderRadius   = 0.5f;
        private const string MenuPrefix     = "Staff Scanner V2";
        private const string ContactTag     = "ClubMaul/Contact";
        private const string WorldAnchorGuid = "c08f73a7f7ed6e240a00a92532499325"; // Misc/World.prefab

        private static void ApplyWorldFeatures(StaffScannerComponent comp)
        {
            var contacts = EnsureContactsGroup(comp.transform);
            // VRCFury toggles must live on an always-active object; the prefab root qualifies and
            // outlives the StaffScannerComponent (only the component is stripped, not its GameObject).
            var menuHost = comp.gameObject;

            // Core contacts — always built, regardless of the feature checkboxes.
            var receiver = BuildReceiver(contacts);
            AddMenuToggle(menuHost, "Broadcast Self", receiver, saved: true, defaultOn: true);

            var sender = BuildSender(contacts, "Sender", ContactTag, localOnly: true);
            AddMenuToggle(menuHost, "See Others", sender, saved: true, defaultOn: true);

            // Optional world features — Beast role only.
            bool isBeast = comp.Role == StaffRole.Beast;
            foreach (var feature in comp.GetWorldFeatures())
            {
                // Drop any stale leftover so re-builds / old prefabs don't duplicate it.
                var existing = FindChildByName(contacts, feature.ObjectName);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

                if (!isBeast || !feature.Enabled) continue;

                var go = BuildSender(contacts, feature.ObjectName, feature.CollisionTag, localOnly: true);
                AddMenuToggle(menuHost, feature.ObjectName, go);
                Debug.Log($"[StaffScanner] Created world-feature '{feature.ObjectName}' (tag '{feature.CollisionTag}').");
            }
        }

        // Creates a default-off VRCContactSender under the world-locked group (replacing any same-named
        // leftover). Default-off so its menu toggle is what enables the contact.
        private static GameObject BuildSender(Transform parent, string name, string tag, bool localOnly)
        {
            var existing = FindChildByName(parent, name);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var sender = go.AddComponent<VRCContactSender>();
            sender.shapeType     = ContactBase.ShapeType.Sphere;
            sender.radius        = SenderRadius;
            sender.position      = Vector3.zero;
            sender.rotation      = Quaternion.identity;
            sender.localOnly     = localOnly;
            sender.collisionTags = new List<string> { tag };

            go.SetActive(false);
            return go;
        }

        // Creates the "Receiver" that drives the scanner-mesh param (ShowParam) when it detects another
        // scanner user's sender. Default-off; enabled by the "Broadcast Self" toggle.
        private static GameObject BuildReceiver(Transform parent)
        {
            var existing = FindChildByName(parent, "Receiver");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var go = new GameObject("Receiver");
            go.transform.SetParent(parent, false);

            var receiver = go.AddComponent<VRCContactReceiver>();
            receiver.shapeType     = ContactBase.ShapeType.Sphere;
            receiver.radius        = SenderRadius;
            receiver.position      = Vector3.zero;
            receiver.rotation      = Quaternion.identity;
            receiver.localOnly     = false;
            receiver.collisionTags = new List<string> { ContactTag };
            receiver.allowSelf     = false;
            receiver.allowOthers   = true;
            receiver.receiverType  = ContactReceiver.ReceiverType.Constant;
            receiver.parameter     = ShowParam;

            go.SetActive(false);
            return go;
        }

        // VRCFury menu Toggle (synced param) that turns 'target' on while the menu item is on.
        private static void AddMenuToggle(GameObject host, string itemName, GameObject target,
                                          bool saved = false, bool defaultOn = false)
        {
            var toggle = FuryComponents.CreateToggle(host);
            toggle.SetMenuPath($"{MenuPrefix}/{itemName}");
            if (saved)     toggle.SetSaved();
            if (defaultOn) toggle.SetDefaultOn();
            toggle.GetActions().AddTurnOn(target);
        }

        // Returns the world-locked "Contacts" group, creating it (with a world-origin VRCParentConstraint)
        // if the prefab no longer contains one.
        private static Transform EnsureContactsGroup(Transform root)
        {
            var existing = FindChildByName(root, "Contacts");
            if (existing != null) return existing;

            var go = new GameObject("Contacts");
            go.transform.SetParent(root, false);

            var anchorPath   = AssetDatabase.GUIDToAssetPath(WorldAnchorGuid);
            var anchorPrefab = string.IsNullOrEmpty(anchorPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(anchorPath);
            if (anchorPrefab == null)
            {
                Debug.LogWarning("[StaffScanner] World anchor (Misc/World.prefab) not found — generated " +
                                 "Contacts will follow the avatar instead of being pinned to world origin.");
            }
            else
            {
                var constraint = go.AddComponent<VRCParentConstraint>();
                constraint.IsActive = true;
                constraint.Locked   = true;
                constraint.Sources.Add(new VRCConstraintSource(anchorPrefab.transform, 1f, Vector3.zero, Vector3.zero));
            }

            return go.transform;
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && t.name == name) return t;
            return null;
        }

        private static GameObject BuildOne(StaffScannerComponent comp, SkinnedMeshRenderer source, Material material, bool appendSourceName)
        {
            if (source.sharedMesh == null)
            {
                Debug.LogWarning($"[StaffScanner] Source renderer '{source.name}' has no mesh. Skipping.");
                return null;
            }

            var decimated  = MeshDecimator.Decimate(source.sharedMesh, comp.DecimationAmount);
            Debug.Log($"[StaffScanner] Decimated '{source.sharedMesh.name}' " +
                      $"from {source.sharedMesh.vertexCount} verts → {decimated.vertexCount} verts " +
                      $"(slider {comp.DecimationAmount:F2}).");

            // Must persist as a real asset — VRChat's upload prefab-save can null
            // in-memory mesh references, leaving the scanner invisible in-game.
            decimated = PersistMesh(decimated, source.name);

            string baseName = string.IsNullOrEmpty(comp.GeneratedObjectName)
                ? "StaffScannerMesh"
                : comp.GeneratedObjectName;
            string goName = appendSourceName ? $"{baseName}_{source.name}" : baseName;

            // Match source's local transform — bindposes are already in source-mesh space.
            // Fallback when source is on the avatar root itself so we stay inside the avatar tree.
            var parent = source.transform.parent != null ? source.transform.parent : source.transform;
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            if (source.transform.parent != null)
            {
                go.transform.localPosition = source.transform.localPosition;
                go.transform.localRotation = source.transform.localRotation;
                go.transform.localScale    = source.transform.localScale;
            }

            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh              = decimated;
            // Sharing source.bones lets the scanner follow every rig motion automatically.
            smr.bones                   = source.bones;
            smr.rootBone                = source.rootBone;
            smr.localBounds             = source.localBounds;
            smr.quality                 = source.quality;
            smr.updateWhenOffscreen     = source.updateWhenOffscreen;
            smr.skinnedMotionVectors    = source.skinnedMotionVectors;
            smr.shadowCastingMode       = source.shadowCastingMode;
            smr.receiveShadows          = source.receiveShadows;
            smr.lightProbeUsage         = source.lightProbeUsage;
            smr.reflectionProbeUsage    = source.reflectionProbeUsage;
            smr.probeAnchor             = source.probeAnchor;

            smr.sharedMaterials = new[] { material };
            return go;
        }

        private static AnimatorController BuildAnimatorController(GameObject avatarRoot, List<GameObject> targets)
        {
            var controller = new AnimatorController { name = "StaffScanner_FX" };
            controller.AddParameter(ShowParam, AnimatorControllerParameterType.Bool);

            controller.AddLayer("StaffScanner");
            var layers = controller.layers;
            layers[0].defaultWeight = 1f;
            controller.layers = layers;

            var sm = controller.layers[0].stateMachine;

            var offClip  = BuildToggleClip("StaffScanner_Off", avatarRoot, targets, false);
            var offState = sm.AddState("Off");
            offState.motion             = offClip;
            offState.writeDefaultValues = false;
            sm.defaultState             = offState;

            var onClip  = BuildToggleClip("StaffScanner_On", avatarRoot, targets, true);
            var onState = sm.AddState("On");
            onState.motion             = onClip;
            onState.writeDefaultValues = false;

            var toOn = offState.AddTransition(onState);
            toOn.hasExitTime = false;
            toOn.duration    = 0f;
            toOn.AddCondition(AnimatorConditionMode.If, 0, ShowParam);

            var toOff = onState.AddTransition(offState);
            toOff.hasExitTime = false;
            toOff.duration    = 0f;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0, ShowParam);

            return controller;
        }

        private static AnimationClip BuildToggleClip(string name, GameObject avatarRoot, List<GameObject> targets, bool active)
        {
            var clip  = new AnimationClip { name = name };
            float val = active ? 1f : 0f;
            foreach (var go in targets)
            {
                string path = GetRelativePath(avatarRoot.transform, go.transform);
                clip.SetCurve(path, typeof(GameObject), "m_IsActive",
                    AnimationCurve.Constant(0f, 0f, val));
            }
            return clip;
        }

        private static SkinnedMeshRenderer AutoDetectBody(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null &&
                descriptor.lipSync == VRCAvatarDescriptor.LipSyncStyle.VisemeBlendShape &&
                descriptor.VisemeSkinnedMesh != null)
            {
                return descriptor.VisemeSkinnedMesh;
            }

            SkinnedMeshRenderer best = null;
            int bestCount = -1;
            foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                int vc = smr.sharedMesh.vertexCount;
                if (vc > bestCount) { bestCount = vc; best = smr; }
            }
            return best;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            var path    = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                path.Add(current.name);
                current = current.parent;
            }
            path.Reverse();
            return string.Join("/", path);
        }

        private static Mesh PersistMesh(Mesh mesh, string sourceName)
        {
            EnsureTempFolder();
            string safe = SanitizeFileName(sourceName);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{TempFolder}/Decimated_{safe}.asset");
            AssetDatabase.CreateAsset(mesh, path);
            // Flush to disk before VRChat snapshots build dependencies, else the bundle ships an
            // empty mesh (visible in-editor, invisible in-game).
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path) ?? mesh;
        }

        // Creates each missing path segment (CreateFolder only makes one level at a time).
        private static void EnsureTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolder)) return;

            var segments = TempFolder.Split('/');
            string current = segments[0]; // "Assets"
            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        private static void CleanTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder)) return;
            AssetDatabase.DeleteAsset(TempFolder);
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Source";
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
