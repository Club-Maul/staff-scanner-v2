// Staff Scanner V2 — Build-time generator
// by Loveseal | v1.0.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using com.vrcfury.api.Components;

namespace ClubMaul.StaffScanner.Editor
{
    public class StaffScannerBuilder : IVRCSDKPreprocessAvatarCallback
    {
        private const string ShowParam  = "ClubMaulShow";
        // VRChat built-in, local-only param — true only on the wearer's own client. Hides the mesh from its wearer.
        private const string LocalParam = "IsLocal";
        // Non-synced (per-viewer) param driven by the SphereView receiver — see BuildSphereReceiver.
        private const string SphereParam = "ClubMaulSphere";
        private const float  SphereSize  = 0.3f; // sphere diameter in world meters (armature scale divided out)

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

            // Alternate hips-centered sphere shown in place of the mesh under Sphere View.
            var sphere = BuildSphere(avatarRoot, material);
            var sphereTargets = sphere != null ? new List<GameObject> { sphere } : new List<GameObject>();

            // Default-off avoids a one-frame flash on load before the FX layer settles.
            foreach (var go in generated) go.SetActive(false);

            var controller = BuildAnimatorController(avatarRoot, generated, sphereTargets);

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

            // Un-prefix the receiver-driven param; left out of expParams so it stays local (per-viewer).
            if (sphere != null) fc.AddGlobalParam(SphereParam);
        }

        // Non-skinned sphere on the humanoid Hips with the scanner material. Default-off; null if non-humanoid.
        private static GameObject BuildSphere(GameObject avatarRoot, Material material)
        {
            var hips = FindHips(avatarRoot);
            if (hips == null)
            {
                Debug.LogWarning("[StaffScanner] No humanoid Hips bone found — skipping the Sphere View option.");
                return null;
            }

            // Borrow Unity's built-in sphere mesh (bundled, so it uploads), then drop the primitive.
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var sphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(temp);

            var go = new GameObject("StaffScannerSphere");
            go.transform.SetParent(hips, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            // Divide out the Hips' world scale so the sphere is a fixed world size on every rig.
            var ls = hips.lossyScale;
            go.transform.localScale = new Vector3(
                ls.x != 0f ? SphereSize / ls.x : SphereSize,
                ls.y != 0f ? SphereSize / ls.y : SphereSize,
                ls.z != 0f ? SphereSize / ls.z : SphereSize);

            go.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            go.SetActive(false);
            return go;
        }

        private static Transform FindHips(GameObject avatarRoot)
        {
            var animator = avatarRoot.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
                return animator.GetBoneTransform(HumanBodyBones.Hips);
            return null;
        }

        // Contacts are rebuilt under a world-locked "Contacts" group: core Receiver + Sender always,
        // plus a sender per checked World Feature and per plugin contact. Each gets a VRCFury menu toggle.
        // The group sits at world origin (VRCParentConstraint) so all scanner users' contacts coincide.
        private const float  SenderRadius   = 0.5f;
        private const string ContactTag     = "ClubMaul/Contact";
        private const string SphereTag      = "ClubMaul/SphereView";
        private const string WorldAnchorGuid = "c08f73a7f7ed6e240a00a92532499325"; // Misc/World.prefab
        private const string IconGuid       = "373ff8c870ce9d34e8b2c82ceaf2d385"; // Misc/Club_Maul_Flames.png

        private static void ApplyWorldFeatures(StaffScannerComponent comp)
        {
            var contacts = EnsureContactsGroup(comp.transform);
            // Toggles must live on an always-active object; only the component is stripped, not its GameObject.
            var menuHost = comp.gameObject;
            var menuPath = comp.GetMenuPath();

            // Core contacts — always built.
            var receiver = BuildReceiver(contacts);
            AddMenuToggle(menuHost, menuPath, "Broadcast Self", receiver, saved: true, defaultOn: true);

            // Networked (not local-only) so other players' receivers can detect it cross-client.
            var sender = BuildSender(contacts, "Sender", ContactTag, localOnly: false);
            AddMenuToggle(menuHost, menuPath, "See Others", sender, saved: true, defaultOn: true);

            // Sphere View — viewer-side: a local-only sender + always-on receiver (non-synced param) so
            // enabling it shows other scanners as spheres to you only.
            var sphereSender = BuildSender(contacts, "SphereViewSender", SphereTag, localOnly: true);
            AddMenuToggle(menuHost, menuPath, "Sphere View", sphereSender, saved: true);
            BuildSphereReceiver(contacts);

            // Optional world features — Beast role only.
            bool isBeast = comp.Role == StaffRole.Beast;
            foreach (var feature in comp.GetWorldFeatures())
            {
                // Drop any stale leftover so re-builds don't duplicate it.
                var existing = FindChildByName(contacts, feature.ObjectName);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

                if (!isBeast || !feature.Enabled) continue;

                var go = BuildSender(contacts, feature.ObjectName, feature.CollisionTag, localOnly: true);
                AddMenuToggle(menuHost, menuPath, feature.ObjectName, go);
                Debug.Log($"[StaffScanner] Created world-feature '{feature.ObjectName}' (tag '{feature.CollisionTag}').");
            }

            if (isBeast) ApplyPlugins(comp, contacts, menuHost, menuPath);
            SetMenuIcon(menuHost, menuPath);
        }

        // Each plugin adds its contacts under a per-plugin sub-folder of the menu.
        private static void ApplyPlugins(StaffScannerComponent comp, Transform contacts, GameObject menuHost, string menuPath)
        {
            if (comp.Plugins == null) return;
            foreach (var plugin in comp.Plugins)
            {
                if (plugin == null || plugin.Contacts == null) continue;
                string pluginName = string.IsNullOrWhiteSpace(plugin.DisplayName) ? plugin.name : plugin.DisplayName;

                foreach (var contact in plugin.Contacts)
                {
                    if (contact == null ||
                        string.IsNullOrWhiteSpace(contact.Name) ||
                        string.IsNullOrWhiteSpace(contact.CollisionTag))
                        continue;

                    // Namespace the object by plugin so two plugins can't collide.
                    string objName = $"{pluginName}_{contact.Name}";
                    var existing = FindChildByName(contacts, objName);
                    if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

                    var go = BuildSender(contacts, objName, contact.CollisionTag, localOnly: true);
                    bool isButton = contact.ControlType == PluginControlType.Button;
                    AddMenuToggle(menuHost, $"{menuPath}/{pluginName}", contact.Name, go, holdButton: isButton);
                    Debug.Log($"[StaffScanner] Plugin '{pluginName}' created contact '{contact.Name}' " +
                              $"(tag '{contact.CollisionTag}', {contact.ControlType}).");
                }
            }
        }

        // Applies the flames icon to the menu folder via VRCFury's "Override Menu Icon" feature.
        // That feature (VF.Model.Feature.SetIcon) isn't in VRCFury's public API, so we build it
        // through reflection — the same VRCFury component the API's toggles use.
        private static void SetMenuIcon(GameObject host, string menuPath)
        {
            var iconPath = AssetDatabase.GUIDToAssetPath(IconGuid);
            var icon = string.IsNullOrEmpty(iconPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (icon == null)
            {
                Debug.LogWarning("[StaffScanner] Menu icon (Misc/Club_Maul_Flames.png) not found — skipping.");
                return;
            }

            try
            {
                var vrcFuryType = Type.GetType("VF.Model.VRCFury, VRCFury");
                var setIconType = Type.GetType("VF.Model.Feature.SetIcon, VRCFury");
                if (vrcFuryType == null || setIconType == null)
                {
                    Debug.LogWarning("[StaffScanner] VRCFury SetIcon types not found — skipping menu icon.");
                    return;
                }

                var setIcon = Activator.CreateInstance(setIconType);
                setIconType.GetField("path").SetValue(setIcon, menuPath);

                var iconField   = setIconType.GetField("icon");
                var guidWrapper = Activator.CreateInstance(iconField.FieldType);
                iconField.FieldType.GetField("objRef").SetValue(guidWrapper, icon);
                iconField.SetValue(setIcon, guidWrapper);

                var vrcFury = host.AddComponent(vrcFuryType);
                vrcFuryType.GetField("content").SetValue(vrcFury, setIcon);
                Debug.Log($"[StaffScanner] Set menu icon for '{menuPath}'.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StaffScanner] Failed to set menu icon: {ex.Message}");
            }
        }

        // Default-off VRCContactSender (its menu toggle enables it); replaces any same-named leftover.
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

        // Drives ShowParam when it detects another scanner user's sender. Enabled by "Broadcast Self".
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

        // Drives the non-synced ClubMaulSphere from any local "Sphere View" sender. Always active so it
        // evaluates per-viewer — that's what makes others render as spheres only for whoever enabled it.
        private static GameObject BuildSphereReceiver(Transform parent)
        {
            var existing = FindChildByName(parent, "SphereViewReceiver");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var go = new GameObject("SphereViewReceiver");
            go.transform.SetParent(parent, false);

            var receiver = go.AddComponent<VRCContactReceiver>();
            receiver.shapeType     = ContactBase.ShapeType.Sphere;
            receiver.radius        = SenderRadius;
            receiver.position      = Vector3.zero;
            receiver.rotation      = Quaternion.identity;
            receiver.localOnly     = false;
            receiver.collisionTags = new List<string> { SphereTag };
            receiver.allowSelf     = false;
            receiver.allowOthers   = true;
            receiver.receiverType  = ContactReceiver.ReceiverType.Constant;
            receiver.parameter     = SphereParam;

            return go;
        }

        // VRCFury menu Toggle (synced param) that turns 'target' on while the menu item is on.
        // 'menuPath' is the folder the item lives under. When holdButton is set, the menu control
        // is a momentary Button (on only while held) instead of a sticky toggle.
        private static void AddMenuToggle(GameObject host, string menuPath, string itemName, GameObject target,
                                          bool saved = false, bool defaultOn = false, bool holdButton = false)
        {
            var toggle = FuryComponents.CreateToggle(host);
            toggle.SetMenuPath($"{menuPath}/{itemName}");
            if (saved)      toggle.SetSaved();
            if (defaultOn)  toggle.SetDefaultOn();
            if (holdButton) SetHoldButton(toggle);
            toggle.GetActions().AddTurnOn(target);
        }

        // 'holdButton' (Button mode) isn't in VRCFury's public API, so set it on the underlying
        // Toggle model that FuryToggle wraps.
        private static void SetHoldButton(FuryToggle toggle)
        {
            try
            {
                var modelField = typeof(FuryToggle).GetField("c", BindingFlags.NonPublic | BindingFlags.Instance);
                var model = modelField?.GetValue(toggle);
                model?.GetType().GetField("holdButton")?.SetValue(model, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StaffScanner] Couldn't set Button mode: {ex.Message}");
            }
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

        private static AnimatorController BuildAnimatorController(GameObject avatarRoot, List<GameObject> meshTargets, List<GameObject> sphereTargets)
        {
            bool hasSphere = sphereTargets != null && sphereTargets.Count > 0;

            var controller = new AnimatorController { name = "StaffScanner_FX" };
            controller.AddParameter(ShowParam, AnimatorControllerParameterType.Bool);
            controller.AddParameter(LocalParam, AnimatorControllerParameterType.Bool);
            if (hasSphere) controller.AddParameter(SphereParam, AnimatorControllerParameterType.Bool);

            // Full mesh: shown when scanning and not the wearer. Suppressed in sphere mode (if available).
            BuildShowLayer(controller, "StaffScannerMesh", avatarRoot, meshTargets,
                           sphereMode: hasSphere ? (bool?)false : null);

            // Sphere: same gate, but only in sphere mode.
            if (hasSphere)
                BuildShowLayer(controller, "StaffScannerSphere", avatarRoot, sphereTargets, sphereMode: true);

            return controller;
        }

        // A weight-1 layer that activates 'targets' when ShowParam is set and LocalParam is not. When
        // 'sphereMode' has a value, SphereParam must also match it (false = mesh layer, true = sphere layer).
        private static void BuildShowLayer(AnimatorController controller, string layerName,
                                           GameObject avatarRoot, List<GameObject> targets, bool? sphereMode)
        {
            controller.AddLayer(layerName);
            var layers = controller.layers;
            int idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;
            controller.layers = layers;

            var sm = controller.layers[idx].stateMachine;

            var offState = sm.AddState("Off");
            offState.motion             = BuildToggleClip(layerName + "_Off", avatarRoot, targets, false);
            offState.writeDefaultValues = false;
            sm.defaultState             = offState;

            var onState = sm.AddState("On");
            onState.motion             = BuildToggleClip(layerName + "_On", avatarRoot, targets, true);
            onState.writeDefaultValues = false;

            var toOn = offState.AddTransition(onState);
            toOn.hasExitTime = false;
            toOn.duration    = 0f;
            toOn.AddCondition(AnimatorConditionMode.If,    0, ShowParam);
            toOn.AddCondition(AnimatorConditionMode.IfNot, 0, LocalParam);
            if (sphereMode.HasValue)
                toOn.AddCondition(sphereMode.Value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, SphereParam);

            // Leave "On" if any single gate fails (separate transitions == OR).
            AddOffTransition(onState, offState, AnimatorConditionMode.IfNot, ShowParam);
            AddOffTransition(onState, offState, AnimatorConditionMode.If,    LocalParam);
            if (sphereMode.HasValue)
                AddOffTransition(onState, offState, sphereMode.Value ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, SphereParam);
        }

        private static void AddOffTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, string param)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration    = 0f;
            t.AddCondition(mode, 0, param);
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

        internal static SkinnedMeshRenderer AutoDetectBody(GameObject avatarRoot)
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
