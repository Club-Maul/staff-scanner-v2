// Staff Scanner V2 — Build-time generator
// by Loveseal | v1.0.0

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
using VRC.Core;
using Object = UnityEngine.Object;

namespace ClubMaul.StaffScanner.Editor
{
    public class StaffScannerBuilder : IVRCSDKPreprocessAvatarCallback
    {
        private const string OneParam = "Constant/One";
        
        private const string ShowParam  = "ClubMaul/Scanner/Show";
        private const string LocalParam = "IsLocal";      // VRChat built-in; true only on the wearer's own client.
        private const string SphereParam = "Internal/Sphere Mode";    // Non-synced (per-viewer); see BuildSphereReceiver.
        private const float  SphereSize  = 0.3f; // sphere diameter in world meters (armature scale divided out)

        private const string OpacityControlParam = "Control/Opacity";
        private const string OpacityReceiveParam = "Internal/Opacity";

        private const string XrayControlParam = "Control/Xray";
        private const string XrayReceiveParam = "Internal/Xray";
        
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
            var fc = FuryComponents.CreateFullController(avatarRoot);
            
            // World features are independent of the scanner mesh, so apply them first —
            // before any early-out below can skip the rest of the build.
            ApplyWorldFeatures(comp, fc, avatarRoot.transform);

            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(MaterialGuid));
            material = Object.Instantiate(material);
            
            // probably unnecessary, but I always like to tell Unity before I
            // modify the C# representation of any unity object
            Undo.RecordObject(material, "Set Scanner Color");

            if (!StaffColorMap.TryGetValue(comp.Role, out var color))
            {
                Debug.LogWarning("Invalid role: " + comp.Role);
                color = Color.blue;
            }

            material.color = color;
            
            // the asset gets lost during upload if we don't do this!
            PersistAsset(ref material, "Scanner Material");

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
                    networkSynced = false
                }
            };

            List<GameObject> visualTargets = new();

            visualTargets.AddRange(generated);
            visualTargets.AddRange(sphereTargets);

            InstallVisualControls(avatarRoot.transform, fc, visualTargets);

            fc.AddController(controller, VRCAvatarDescriptor.AnimLayerType.FX);
            fc.AddParams(expParams);

            // This parameter is exposed to other systems. For example, a hunter avatar could
            // react to it by turning off its post-processing effects.
            fc.AddGlobalParam(ShowParam);
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
        private const string ContactTag     = "ClubMaul/Scanner/Show";
        // Old V1 scanner's tag; the presence beacon also answers it so V1 users see V2 wearers (one-way —
        // V2 meshes stay staff-only).
        private const string LegacyContactTag = "ClubMaulShow";
        private const string SphereTag      = "ClubMaul/Scanner/SphereView";
        private const string WorldAnchorGuid = "c08f73a7f7ed6e240a00a92532499325"; // Misc/World.prefab
        private const string IconGuid       = "373ff8c870ce9d34e8b2c82ceaf2d385"; // Misc/Club_Maul_Flames.png

        private const string MaterialGuid = "36ab3d734792f4b5fb9779f55b34e942";

        private static readonly Dictionary<StaffRole, Color> StaffColorMap = new()
        {
            { StaffRole.Beast, Color.red },
            { StaffRole.Security, Color.blue },
            { StaffRole.Photography, Color.yellow },
            { StaffRole.Host, Color.magenta }
        };

        private static void ApplyWorldFeatures(StaffScannerComponent comp, FuryFullController fc, Transform avatarRoot)
        {
            var controller = new AnimatorController
            {
                name = "World Features Controller"
            };

            controller.AddParameter(OneParam, AnimatorControllerParameterType.Float);
    
            {
                var parameters = controller.parameters;
                parameters[^1].defaultFloat = 1f;
                controller.parameters = parameters;
            }
            
            var machine = new AnimatorStateMachine
            {
                name = "Controls Machine"
            };

            var layer = new AnimatorControllerLayer
            {
                name = "Controls",
                stateMachine = machine
            };

            var dbt = new BlendTree
            {
                name = "Root Tree",
                blendType = BlendTreeType.Direct
            };

            var dbtState = machine.AddState("Blend");
            dbtState.motion = dbt;

            controller.AddLayer(layer);

            var paramz = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            paramz.name = "World Features Parameters";
            paramz.parameters = new VRCExpressionParameters.Parameter[0];

            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();

            menu.name = "World Features Menu";
            menu.Parameters = paramz;
            
            fc.AddController(controller, VRCAvatarDescriptor.AnimLayerType.FX);
            fc.AddParams(paramz);
            fc.AddMenu(menu, comp.GetMenuPath());
            
            var contacts = EnsureContactsGroup(comp.transform);
            
            // To avoid overlapping too many contact sender/receivers, we'll stagger them spatially.

            var mainHolder = new GameObject("Main Contacts");
            mainHolder.transform.SetParent(contacts, false);

            var opacityHolder = new GameObject("Opacity Contacts");
            opacityHolder.transform.SetParent(contacts, false);
            opacityHolder.transform.localPosition = new Vector3(0, 2, 0);

            var xrayHolder = new GameObject("Xray Contacts");
            xrayHolder.transform.SetParent(contacts, false);
            xrayHolder.transform.localPosition = new Vector3(0, 4, 0);
            
            // Toggles must live on an always-active object; only the component is stripped, not its GameObject.
            var menuHost = comp.gameObject;
            var menuPath = comp.GetMenuPath();

            // Core contacts — always built.
            var receiver = BuildReceiver(contacts);
            receiver.GetComponent<VRCContactReceiver>().collisionTags.Add(LegacyContactTag);
            AddMenuToggle(menuHost, menuPath, "Broadcast Self", receiver, saved: true, defaultOn: true);

            var presence = BuildSender(contacts, "Sender", ContactTag, localOnly: true);
            presence.GetComponent<VRCContactSender>().collisionTags.Add(LegacyContactTag);
            AddMenuToggle(menuHost, menuPath, "See Others", presence, saved: true, defaultOn: true);

            // Sphere View — viewer-side: a local-only sender + always-on receiver (non-synced param) so
            // enabling it shows other scanners as spheres to you only.
            var sphereSender = BuildSender(contacts, "SphereViewSender", SphereTag, localOnly: true);
            AddMenuToggle(menuHost, menuPath + "/Visuals", "Sphere View", sphereSender, saved: true);
            BuildSphereReceiver(contacts);

            var opacityPair = BuildFloatPair(avatarRoot, opacityHolder.transform, "Opacity", "Internal/Opacity",
                "ClubMaul/Scanner/Opacity");

            {
                var (opacityTree, opacityMenu, opacityParams) = opacityPair.Generate(OpacityControlParam);

                dbt.AddChild(opacityTree);
                
                controller.AddParameter(OpacityControlParam, AnimatorControllerParameterType.Float);
                controller.AddParameter(OpacityReceiveParam, AnimatorControllerParameterType.Float);

                fc.AddMenu(opacityMenu, menuPath + "/Visuals");
                fc.AddParams(opacityParams);
            }

            var xrayPair = BuildFloatPair(avatarRoot, xrayHolder.transform, "Xray", "Internal/Xray",
                "ClubMaul/Scanner/Xray");

            {
                var (xrayTree, xrayMenu, xrayParams) = xrayPair.Generate(XrayControlParam);

                dbt.AddChild(xrayTree);
                
                controller.AddParameter(XrayControlParam, AnimatorControllerParameterType.Float);
                controller.AddParameter(XrayReceiveParam, AnimatorControllerParameterType.Float);

                fc.AddMenu(xrayMenu, menuPath + "/Visuals");
                fc.AddParams(xrayParams);
            }

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

            {
                var children = dbt.children;

                for (int idx = 0; idx < children.Length; ++idx)
                {
                    children[idx].directBlendParameter = OneParam;
                }

                dbt.children = children;
            }
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

        // Drives non-synced ClubMaulSphere from any local "Sphere View" sender; always active (per-viewer).
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

        private class FloatPair
        {
            public string name;
            public VRCContactSender sender;
            public VRCContactReceiver receiver;
            public AnimationClip zeroClip;
            public AnimationClip oneClip;

            public (BlendTree, VRCExpressionsMenu, VRCExpressionParameters) Generate(string controlParam)
            {
                var tree = new BlendTree
                {
                    name = name,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = controlParam,
                    useAutomaticThresholds = true
                };

                tree.AddChild(zeroClip);
                tree.AddChild(oneClip);

                var paramz = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                paramz.name = "Float Control Param - " + name;

                var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                menu.name = "Float Control Menu - " + name;
                menu.Parameters = paramz;

                paramz.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter
                    {
                        name = controlParam,
                        defaultValue = 0.5f,
                        networkSynced = false,
                        saved = true,
                        valueType = VRCExpressionParameters.ValueType.Float
                    }
                };

                menu.controls = new List<VRCExpressionsMenu.Control>
                {
                    new VRCExpressionsMenu.Control
                    {
                        name = name,
                        type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                        subParameters = new[]
                        {
                            new VRCExpressionsMenu.Control.Parameter
                            {
                                name = controlParam
                            }
                        }
                    }
                };

                return (tree, menu, paramz);
            }
        }
        
        /// <summary>
        /// Creates a contact pair that can convey a float value.
        /// </summary>
        /// <param name="root">Where to record the animations from. A VRCF Full Controller should be placed here.</param>
        /// <param name="parent">Where to place the sender and receiver.</param>
        /// <param name="name">A name to use for the objects and animations. This has no effect on functionality.</param>
        /// <param name="paramName">The parameter to set. Note that the value will actually range from 0.5 to 1. A value of 0 means that no sender exists.</param>
        /// <param name="tag">The collision tag to use. This should be unique.</param>
        /// <returns></returns>
        private static FloatPair BuildFloatPair(Transform root, Transform parent, string name,
            string paramName, string tag)
        {
            var senderHolder = new GameObject(name + " Sender");
            var receiverHolder = new GameObject(name + "Receiver");

            senderHolder.transform.SetParent(parent, false);
            receiverHolder.transform.SetParent(parent, false);

            var sender = senderHolder.AddComponent<VRCContactSender>();
            var receiver = receiverHolder.AddComponent<VRCContactReceiver>();

            sender.shapeType = receiver.shapeType       = ContactBase.ShapeType.Sphere;
            sender.radius = receiver.radius             = SenderRadius;
            sender.position = receiver.position         = Vector3.zero;
            sender.rotation = receiver.rotation         = Quaternion.identity;

            sender.localOnly = true;
            receiver.localOnly = false;

            sender.collisionTags = receiver.collisionTags = new List<string> { tag };

            receiver.allowSelf = false;
            receiver.allowOthers = true;
            receiver.receiverType = ContactReceiver.ReceiverType.Proximity;
            receiver.parameter = paramName;

            var zeroClip = new AnimationClip
            {
                name = name + " - Zero"
            };
            var oneClip = new AnimationClip
            {
                name = name + " - One"
            };

            zeroClip.SetCurve(sender.transform.GetHierarchyPath(root), typeof(Transform), "m_LocalPosition.x",
                AnimationCurve.Constant(0, 1, SenderRadius * 1.5f));

            oneClip.SetCurve(sender.transform.GetHierarchyPath(root), typeof(Transform), "m_LocalPosition.x",
                AnimationCurve.Constant(0, 1, SenderRadius));

            FloatPair result = new()
            {
                name = name,
                sender = sender,
                receiver = receiver,
                zeroClip = zeroClip,
                oneClip = oneClip
            };

            return result;
        }

        // VRCFury menu Toggle that turns 'target' on while the item is on. holdButton = momentary Button.
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
            PersistAsset(ref decimated, "Decimated_" + source.name);

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

        private static void PersistAsset<T>(ref T asset, string sourceName) where T : Object
        {
            EnsureTempFolder();
            string safe = SanitizeFileName(sourceName);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{TempFolder}/{safe}.asset");
            AssetDatabase.CreateAsset(asset, path);
            // Flush to disk before VRChat snapshots build dependencies, else the bundle ships an
            // empty mesh (visible in-editor, invisible in-game).
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var persisted = AssetDatabase.LoadAssetAtPath<T>(path);

            if (!persisted)
            {
                Debug.LogWarning("Failed to persist a " + typeof(T).Name + ": " + sourceName);
            }
            else
            {
                asset = persisted;
            }
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

        private static void InstallVisualControls(Transform avatarRoot, FuryFullController fc, List<GameObject> targets)
        {
            var controller = new AnimatorController
            {
                name = "Visual Controller"
            };
            
            fc.AddController(controller, VRCAvatarDescriptor.AnimLayerType.FX);

            controller.AddParameter(OneParam, AnimatorControllerParameterType.Float);

            {
                var parameters = controller.parameters;
                parameters[^1].defaultFloat = 1f;
                controller.parameters = parameters;
            }

            controller.AddParameter(OpacityReceiveParam, AnimatorControllerParameterType.Float);
            controller.AddParameter(XrayReceiveParam, AnimatorControllerParameterType.Float);

            var machine = new AnimatorStateMachine
            {
                name = "Visual Machine"
            };

            var layer = new AnimatorControllerLayer
            {
                name = "Visuals",
                stateMachine = machine
            };

            controller.AddLayer(layer);

            var state = machine.AddState("Blend");

            var root = new BlendTree
            {
                name = "Root Tree",
                blendType = BlendTreeType.Direct
            };

            state.motion = root;

            var opacityTree = new BlendTree
            {
                name = "Opacity Control",
                blendParameter = OpacityReceiveParam
            };

            root.AddChild(opacityTree);
            
            var opacityDefaultClip = new AnimationClip
            {
                name = "Opacity - Default"
            };

            var opacityMinClip = new AnimationClip
            {
                name = "Opacity - Min"
            };

            var opacityMaxClip = new AnimationClip
            {
                name = "Opacity - Max"
            };

            opacityTree.AddChild(opacityDefaultClip);
            opacityTree.AddChild(opacityMinClip);
            opacityTree.AddChild(opacityMaxClip);

            {
                var type = typeof(Renderer);

                foreach (var target in targets)
                {
                    var path = target.transform.GetHierarchyPath(avatarRoot);
                    opacityDefaultClip.SetCurve(path, type, "material._AlphaMod", AnimationCurve.Constant(0, 1, -0.7f));
                    opacityMinClip.SetCurve(path, type, "material._AlphaMod", AnimationCurve.Constant(0, 1, -1f));
                    opacityMaxClip.SetCurve(path, type, "material._AlphaMod", AnimationCurve.Constant(0, 1, 0f));
                }
            }

            var xrayTree = new BlendTree
            {
                name = "Xray Control",
                blendParameter = XrayReceiveParam
            };

            root.AddChild(xrayTree);
            
            var xrayDefaultClip = new AnimationClip
            {
                name = "Xray - Default"
            };

            var xrayMinClip = new AnimationClip
            {
                name = "Xray - Min"
            };

            var xrayMaxClip = new AnimationClip
            {
                name = "Xray - Max"
            };

            xrayTree.AddChild(xrayDefaultClip);
            xrayTree.AddChild(xrayMinClip);
            xrayTree.AddChild(xrayMaxClip);

            {
                var type = typeof(Renderer);

                foreach (var target in targets)
                {
                    var path = target.transform.GetHierarchyPath(avatarRoot);
                    xrayDefaultClip.SetCurve(path, type, "material._DepthAlphaMaxValue", AnimationCurve.Constant(0, 1, 0.25f));
                    xrayMinClip.SetCurve(path, type, "material._DepthAlphaMaxValue", AnimationCurve.Constant(0, 1, 0f));
                    xrayMaxClip.SetCurve(path, type, "material._DepthAlphaMaxValue", AnimationCurve.Constant(0, 1, 1f));
                }
            }

            {
                var children = root.children;

                for (int idx = 0; idx < children.Length; ++idx)
                {
                    children[idx].directBlendParameter = OneParam;
                }

                root.children = children;
            }
        }
    }
}
