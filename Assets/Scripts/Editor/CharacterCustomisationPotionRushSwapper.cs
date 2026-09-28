using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CharacterCustomisationPotionRush
{
    public enum MaterialMode
    {
        UseModelMaterials,
        KeepExistingMaterials
    }

    /// <summary>
    /// How to reconcile a model whose skeleton has the same bone names as the character but a different
    /// rest pose. Rigs that match exactly need no reconciliation at all.
    /// </summary>
    public enum RigFitMode
    {
        Auto,
        Direct,
        AdoptModelRestPose,
        RebakeBindPoses
    }

    [Serializable]
    public class CharacterSwapOptions
    {
        public MaterialMode Materials = MaterialMode.UseModelMaterials;
        public RigFitMode RigFit = RigFitMode.Auto;
        public string GeneratedMeshFolder = "Assets/Art/Characters/Generated";
        public bool GraftMissingBones = true;
        public bool RemoveUnusedRenderers = true;
        public bool CopyBlendShapeWeights = true;
        public bool IgnoreBoneNamespace = true;
        public bool SwapAttachedProps = true;
        public bool RemoveUnselectedProps = true;

        /// <summary>
        /// Names of the props the user ticked in the window. Nothing is attached unless it is listed here,
        /// so a model's accessories are never brought across on their own.
        /// </summary>
        public List<string> SelectedProps = new List<string>();

        /// <summary>
        /// Props the user explicitly unticked. Needed because accessories inherited from the character
        /// itself start out selected, so "not listed as selected" cannot mean "drop it".
        /// </summary>
        public List<string> DeselectedProps = new List<string>();

        public bool IsPropSelected(string propName)
        {
            return IsPropSelected(propName, false);
        }

        public bool IsPropSelected(string propName, bool selectedByDefault)
        {
            if (SelectedProps != null && SelectedProps.Contains(propName))
            {
                return true;
            }

            return selectedByDefault && (DeselectedProps == null || !DeselectedProps.Contains(propName));
        }

        public void SetPropSelected(string propName, bool selected)
        {
            if (SelectedProps == null)
            {
                SelectedProps = new List<string>();
            }

            if (DeselectedProps == null)
            {
                DeselectedProps = new List<string>();
            }

            if (selected)
            {
                if (!SelectedProps.Contains(propName))
                {
                    SelectedProps.Add(propName);
                }

                DeselectedProps.Remove(propName);
            }
            else
            {
                SelectedProps.Remove(propName);

                if (!DeselectedProps.Contains(propName))
                {
                    DeselectedProps.Add(propName);
                }
            }
        }
    }

    /// <summary>
    /// Name based lookup over a skeleton. Mixamo and other exporters prefix bones with a namespace
    /// ("mixamorig:Hips", "mixamorig1:Hips"), so an optional normalised lookup ignores that prefix.
    /// </summary>
    public class BoneMap
    {
        private readonly Dictionary<string, Transform> _exact = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Transform> _normalised = new Dictionary<string, Transform>();
        private readonly bool _ignoreNamespace;

        public List<string> DuplicateNames { get; } = new List<string>();

        public BoneMap(Transform root, bool ignoreNamespace, IEnumerable<SkinnedMeshRenderer> preferredBones = null)
        {
            _ignoreNamespace = ignoreNamespace;

            // Bones actually referenced by the character's renderers win over leftover armatures
            // that share Mixamo names (this project has Armature, Armature.001 and Run under Visual).
            if (preferredBones != null)
            {
                foreach (SkinnedMeshRenderer renderer in preferredBones)
                {
                    if (renderer == null)
                    {
                        continue;
                    }

                    if (renderer.bones != null)
                    {
                        foreach (Transform bone in renderer.bones)
                        {
                            Add(bone);
                        }
                    }

                    Add(renderer.rootBone);
                }
            }

            if (root == null)
            {
                return;
            }

            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
            {
                Add(bone);
            }
        }

        public void Add(Transform bone)
        {
            if (bone == null)
            {
                return;
            }

            Transform existing;
            if (_exact.TryGetValue(bone.name, out existing))
            {
                if (existing != bone && !DuplicateNames.Contains(bone.name))
                {
                    DuplicateNames.Add(bone.name);
                }

                return;
            }

            _exact.Add(bone.name, bone);

            string normalised = Normalise(bone.name);
            if (!_normalised.ContainsKey(normalised))
            {
                _normalised.Add(normalised, bone);
            }
        }

        public Transform Find(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return null;
            }

            Transform bone;
            if (_exact.TryGetValue(boneName, out bone))
            {
                return bone;
            }

            if (_ignoreNamespace && _normalised.TryGetValue(Normalise(boneName), out bone))
            {
                return bone;
            }

            foreach (string alias in BuildAliases(boneName))
            {
                if (_normalised.TryGetValue(Normalise(alias), out bone))
                {
                    return bone;
                }
            }

            return null;
        }

        public static string Normalise(string boneName)
        {
            int separator = boneName.LastIndexOf(':');
            string trimmed = separator >= 0 ? boneName.Substring(separator + 1) : boneName;
            return trimmed.Replace(" ", string.Empty).ToLowerInvariant();
        }

        private static IEnumerable<string> BuildAliases(string boneName)
        {
            string name = Normalise(boneName);
            if (name.EndsWith("_m", StringComparison.Ordinal))
            {
                switch (name)
                {
                    case "root_m":
                        yield return "Hips";
                        break;
                    case "spine1_m":
                        yield return "Spine";
                        break;
                    case "spine2_m":
                        yield return "Spine1";
                        break;
                    case "chest_m":
                        yield return "Spine2";
                        break;
                    case "neck_m":
                        yield return "Neck";
                        break;
                    case "head_m":
                        yield return "Head";
                        break;
                    case "headend_m":
                        yield return "HeadEnd";
                        break;
                }
            }

            string side = name.EndsWith("_l", StringComparison.Ordinal) ? "Left" :
                          name.EndsWith("_r", StringComparison.Ordinal) ? "Right" : null;
            if (side == null)
            {
                yield break;
            }

            string part = name.Substring(0, name.Length - 2);
            string targetPart = null;
            switch (part)
            {
                case "scapula":
                    targetPart = "Shoulder";
                    break;
                case "shoulder":
                case "shoulderpart1":
                    targetPart = "Arm";
                    break;
                case "elbow":
                case "elbowpart1":
                    targetPart = "ForeArm";
                    break;
                case "wrist":
                    targetPart = "Hand";
                    break;
                case "hip":
                    targetPart = "UpLeg";
                    break;
                case "hippart1":
                    targetPart = "UpLeg";
                    break;
                case "knee":
                case "kneepart1":
                    targetPart = "Leg";
                    break;
                case "ankle":
                    targetPart = "Foot";
                    break;
                case "toes":
                    targetPart = "ToeBase";
                    break;
                case "toesend":
                    targetPart = "Toe_End";
                    break;
                case "thumbfinger1":
                    targetPart = "HandThumb1";
                    break;
                case "thumbfinger2":
                    targetPart = "HandThumb2";
                    break;
                case "thumbfinger3":
                    targetPart = "HandThumb3";
                    break;
                case "thumbfinger4":
                    targetPart = "HandThumb4";
                    break;
                case "middlefinger1":
                    targetPart = "HandMiddle1";
                    break;
                case "middlefinger2":
                    targetPart = "HandMiddle2";
                    break;
                case "middlefinger3":
                    targetPart = "HandMiddle3";
                    break;
                case "middlefinger4":
                    targetPart = "HandMiddle4";
                    break;
                case "indexfinger1":
                    targetPart = "HandIndex1";
                    break;
                case "indexfinger2":
                    targetPart = "HandIndex2";
                    break;
                case "indexfinger3":
                    targetPart = "HandIndex3";
                    break;
                case "indexfinger4":
                    targetPart = "HandIndex4";
                    break;
                case "ringfinger1":
                    targetPart = "HandRing1";
                    break;
                case "ringfinger2":
                    targetPart = "HandRing2";
                    break;
                case "ringfinger3":
                    targetPart = "HandRing3";
                    break;
                case "ringfinger4":
                    targetPart = "HandRing4";
                    break;
                case "pinkyfinger1":
                    targetPart = "HandPinky1";
                    break;
                case "pinkyfinger2":
                    targetPart = "HandPinky2";
                    break;
                case "pinkyfinger3":
                    targetPart = "HandPinky3";
                    break;
                case "pinkyfinger4":
                    targetPart = "HandPinky4";
                    break;
            }

            if (targetPart != null)
            {
                yield return side + targetPart;
            }
        }
    }

    public class MeshSwapPlan
    {
        public SkinnedMeshRenderer Source;
        public SkinnedMeshRenderer Existing;
        public string Name;
        public int BoneCount;
        public readonly List<string> MissingBones = new List<string>();
        public readonly List<string> UngraftableBones = new List<string>();

        public bool IsNew
        {
            get { return Existing == null; }
        }

        public int MatchedBones
        {
            get { return BoneCount - MissingBones.Count; }
        }
    }

    /// <summary>
    /// A rigid accessory parented straight to a bone rather than skinned to the skeleton
    /// (character_animations_01 attaches Knot, LeatherBag, RopeBelt and the hats/hair this way).
    /// Each one is offered to the user as its own opt-in entry.
    /// </summary>
    public class PropSwapPlan
    {
        public Transform Source;
        public string Name;
        public string AttachBoneName;
        public Matrix4x4 LocalOffset = Matrix4x4.identity;
        public bool AttachBoneMissing;
        public bool Selected;
        public GameObject Existing;
        public CharacterPropLibraryEntry LibraryEntry;
        public readonly List<string> MissingBones = new List<string>();

        /// <summary>
        /// True when the prop comes from the character itself rather than the incoming model, so it is
        /// carried over and re-anchored instead of copied in.
        /// </summary>
        public bool Inherited;

        public bool IsNew
        {
            get { return Existing == null; }
        }
    }

    public class CharacterSwapPlan
    {
        public GameObject Target;
        public GameObject Model;
        public Transform SkeletonRoot;
        public Transform ModelSkeletonRoot;
        public Transform MeshParent;
        public SkinnedMeshRenderer Template;
        public BoneMap Bones;

        public readonly List<MeshSwapPlan> Meshes = new List<MeshSwapPlan>();
        public readonly List<PropSwapPlan> Props = new List<PropSwapPlan>();
        public readonly List<SkinnedMeshRenderer> UnusedRenderers = new List<SkinnedMeshRenderer>();
        public readonly List<GameObject> UnselectedProps = new List<GameObject>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public int SharedBoneCount;
        public int MismatchedBoneCount;
        public float MaxRestPoseDelta;
        public bool ClipsAnimateBonePositions;
        public RigFitMode RecommendedFit = RigFitMode.Direct;
        public RigFitMode EffectiveFit = RigFitMode.Direct;
        public string FitAdvice = string.Empty;

        public bool CanApply
        {
            get { return Errors.Count == 0 && Meshes.Count > 0; }
        }

        public bool RestPoseMatches
        {
            get { return MismatchedBoneCount == 0; }
        }
    }

    public class CharacterSwapResult
    {
        public int RenderersUpdated;
        public int RenderersCreated;
        public int RenderersRemoved;
        public int BonesGrafted;
        public int BonesRepositioned;
        public int PropsAttached;
        public int PropsRemoved;
        public int MeshesRebaked;
        public readonly List<string> Messages = new List<string>();
    }

    /// <summary>
    /// Swaps the skinned meshes of a character over to a different model while keeping the character's
    /// existing skeleton GameObjects. Bones are matched by name, so animation clip paths, the Animator
    /// and anything else holding a bone reference stay valid across a swap.
    /// </summary>
    public static class CharacterCustomisationPotionRushSwapper
    {
        private const float PositionTolerance = 0.001f;
        private const float AngleTolerance = 0.01f;

        public static CharacterSwapPlan BuildPlan(GameObject target, GameObject model, CharacterSwapOptions options)
        {
            return BuildPlan(target, model, options, null);
        }

        public static CharacterSwapPlan BuildPlan(
            GameObject target,
            GameObject model,
            CharacterSwapOptions options,
            CharacterPropLibrary propLibrary)
        {
            var plan = new CharacterSwapPlan { Target = target, Model = model };

            if (target == null)
            {
                plan.Errors.Add("Pick a character in the scene to customise.");
                return plan;
            }

            if (model == null)
            {
                plan.Errors.Add("Pick a model to swap in.");
                return plan;
            }

            if (target == model || model.transform.IsChildOf(target.transform))
            {
                plan.Errors.Add("The model must be a different object to the target character.");
                return plan;
            }

            List<SkinnedMeshRenderer> sourceRenderers = model
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null)
                .ToList();

            if (sourceRenderers.Count == 0)
            {
                plan.Errors.Add(string.Format("'{0}' has no Skinned Mesh Renderers with a mesh assigned.", model.name));
                return plan;
            }

            List<SkinnedMeshRenderer> targetRenderers = target.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList();

            plan.Template = targetRenderers.FirstOrDefault();
            plan.SkeletonRoot = ResolveSkeletonRoot(target.transform, targetRenderers);
            plan.ModelSkeletonRoot = ResolveModelSkeletonRoot(model.transform, sourceRenderers);
            plan.MeshParent = plan.Template != null && plan.Template.transform.parent != null
                ? plan.Template.transform.parent
                : target.transform;

            if (plan.SkeletonRoot == null)
            {
                plan.Errors.Add(string.Format(
                    "Could not find a skeleton under '{0}'. The target needs at least one Skinned Mesh Renderer with bones before it can be customised.",
                    target.name));
                return plan;
            }

            plan.Bones = new BoneMap(plan.SkeletonRoot, options.IgnoreBoneNamespace, targetRenderers);
            if (plan.Bones.DuplicateNames.Count > 0)
            {
                plan.Warnings.Add(string.Format(
                    "{0} bone name(s) appear more than once under '{1}'. Bones already used by the character's skinned meshes are preferred.",
                    plan.Bones.DuplicateNames.Count, plan.SkeletonRoot.name));
            }

            var modelBones = new BoneMap(plan.ModelSkeletonRoot, options.IgnoreBoneNamespace);

            foreach (SkinnedMeshRenderer source in sourceRenderers)
            {
                var mesh = new MeshSwapPlan
                {
                    Source = source,
                    Name = source.name,
                    BoneCount = source.bones.Length,
                    Existing = targetRenderers.FirstOrDefault(r => r.name == source.name)
                };

                foreach (Transform bone in source.bones)
                {
                    if (bone == null || plan.Bones.Find(bone.name) != null || mesh.MissingBones.Contains(bone.name))
                    {
                        continue;
                    }

                    mesh.MissingBones.Add(bone.name);
                    if (modelBones.Find(bone.name) == null)
                    {
                        mesh.UngraftableBones.Add(bone.name);
                    }
                }

                if (source.rootBone != null && plan.Bones.Find(source.rootBone.name) == null &&
                    !mesh.MissingBones.Contains(source.rootBone.name))
                {
                    mesh.MissingBones.Add(source.rootBone.name);
                }

                plan.Meshes.Add(mesh);
            }

            var sourceNames = new HashSet<string>(sourceRenderers.Select(r => r.name));
            plan.UnusedRenderers.AddRange(targetRenderers.Where(r => !sourceNames.Contains(r.name)));

            if (options.SwapAttachedProps)
            {
                CollectProps(plan, options, propLibrary);
            }

            MeasureRestPose(plan);
            Advise(plan, options);

            plan.EffectiveFit = options.RigFit == RigFitMode.Auto ? plan.RecommendedFit : options.RigFit;

            AddPlanWarnings(plan, options);

            return plan;
        }

        public static CharacterSwapResult Apply(CharacterSwapPlan plan, CharacterSwapOptions options)
        {
            var result = new CharacterSwapResult();
            if (plan == null || !plan.CanApply)
            {
                return result;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(string.Format("Swap character to {0}", plan.Model.name));

            var generated = new List<KeyValuePair<Mesh, string>>();

            try
            {
                if (options.GraftMissingBones)
                {
                    result.BonesGrafted = GraftMissingBones(plan);
                }

                if (plan.EffectiveFit == RigFitMode.AdoptModelRestPose)
                {
                    result.BonesRepositioned = AdoptRestPose(plan);
                }

                foreach (MeshSwapPlan mesh in plan.Meshes)
                {
                    if (mesh.IsNew)
                    {
                        result.RenderersCreated++;
                    }
                    else
                    {
                        result.RenderersUpdated++;
                    }

                    ApplyMesh(plan, mesh, options, result, generated);
                }

                if (options.RemoveUnusedRenderers)
                {
                    foreach (SkinnedMeshRenderer unused in plan.UnusedRenderers.Where(r => r != null))
                    {
                        result.RenderersRemoved++;
                        Undo.DestroyObjectImmediate(unused.gameObject);
                    }
                }

                if (options.SwapAttachedProps)
                {
                    ApplyProps(plan, options, result, generated);
                }

                RefreshBounds(plan);
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            // Asset database work is kept outside the undo group so that one undo reverts the whole swap.
            foreach (KeyValuePair<Mesh, string> entry in generated)
            {
                SaveGeneratedMesh(entry.Key, entry.Value);
            }

            if (generated.Count > 0)
            {
                AssetDatabase.SaveAssets();
            }

            result.Messages.Add(string.Format(
                "{0} renderer(s) updated, {1} created, {2} removed, {3} bone(s) grafted, {4} bone(s) repositioned, {5} mesh(es) rebaked, {6} prop(s) attached, {7} prop(s) removed.",
                result.RenderersUpdated, result.RenderersCreated, result.RenderersRemoved,
                result.BonesGrafted, result.BonesRepositioned, result.MeshesRebaked,
                result.PropsAttached, result.PropsRemoved));

            return result;
        }

        /// <summary>
        /// Walks up from a renderer's root bone to that renderer's armature, stopping before a sibling
        /// mesh container or a second leftover Mixamo armature.
        /// </summary>
        public static Transform ResolveSkeletonRoot(Transform characterRoot, IEnumerable<SkinnedMeshRenderer> renderers)
        {
            if (characterRoot == null)
            {
                return null;
            }

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Transform bone = renderer.rootBone;
                if (bone == null && renderer.bones != null && renderer.bones.Length > 0)
                {
                    bone = renderer.bones[0];
                }

                if (bone == null || !bone.IsChildOf(characterRoot))
                {
                    continue;
                }

                while (bone.parent != null && bone.parent != characterRoot && bone.parent.IsChildOf(characterRoot))
                {
                    Transform parent = bone.parent;
                    if (parent.GetComponent<Animator>() != null || HasDirectSkinnedMesh(parent))
                    {
                        break;
                    }

                    bone = parent;
                }

                return bone;
            }

            return null;
        }

        private static Transform ResolveModelSkeletonRoot(Transform modelRoot, IEnumerable<SkinnedMeshRenderer> renderers)
        {
            if (modelRoot == null)
            {
                return null;
            }

            Transform baseCharacter = FindHierarchyNode(modelRoot, "base_character");
            return baseCharacter != null ? baseCharacter : ResolveSkeletonRoot(modelRoot, renderers);
        }

        private static Transform FindHierarchyNode(Transform root, string nodeName)
        {
            if (root == null)
            {
                return null;
            }

            if (string.Equals(root.name, nodeName, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root && string.Equals(child.name, nodeName, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }

        private static bool HasDirectSkinnedMesh(Transform parent)
        {
            if (parent.GetComponent<SkinnedMeshRenderer>() != null)
            {
                return true;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).GetComponent<SkinnedMeshRenderer>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Finds the rigid accessories the model parents straight to a bone (hats, hair, Knot, LeatherBag,
        /// RopeBelt...). They carry no skin deformer, so the Skinned Mesh Renderer pass never sees them.
        /// Each one is listed for the user to pick; none are attached unless explicitly selected.
        /// </summary>
        private static void CollectProps(CharacterSwapPlan plan, CharacterSwapOptions options)
        {
            CollectProps(plan, options, null);
        }

        private static void CollectProps(
            CharacterSwapPlan plan,
            CharacterSwapOptions options,
            CharacterPropLibrary propLibrary)
        {
            Dictionary<string, GameObject> attached = FindAttachedProps(plan.SkeletonRoot);

            // Search the complete model hierarchy, including wrappers such as base_character, because
            // rigid props may be parented to bones beneath those nodes.
            foreach (Transform source in FindRigidPropRoots(plan.Model))
            {
                var prop = new PropSwapPlan
                {
                    Source = source,
                    Name = source.name,
                    LocalOffset = LocalMatrix(source),
                    Selected = options.IsPropSelected(source.name)
                };

                // Walk up until a bone the character actually has, folding the transforms of any
                // intermediate node into the offset so the accessory keeps its place on the body.
                Transform anchor = source.parent;
                while (anchor != null && anchor != plan.Model.transform && plan.Bones.Find(anchor.name) == null)
                {
                    prop.LocalOffset = LocalMatrix(anchor) * prop.LocalOffset;
                    anchor = anchor.parent;
                }

                Transform bone = anchor != null ? plan.Bones.Find(anchor.name) : null;
                prop.AttachBoneName = anchor != null ? anchor.name : null;
                prop.AttachBoneMissing = bone == null;

                GameObject match;
                if (attached.TryGetValue(prop.Name, out match))
                {
                    prop.Existing = match;
                    attached.Remove(prop.Name);
                }

                plan.Props.Add(prop);
            }

            if (propLibrary != null && propLibrary.Props != null)
            {
                foreach (CharacterPropLibraryEntry entry in propLibrary.Props)
                {
                    if (entry == null || (entry.IsSkinnedMesh ? entry.SkinnedMesh == null : entry.Prefab == null) ||
                        plan.Props.Any(existing => string.Equals(existing.Name, entry.Name, StringComparison.Ordinal)) ||
                        plan.Meshes.Any(mesh => string.Equals(mesh.Name, entry.Name, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    GameObject existingProp;
                    attached.TryGetValue(entry.Name, out existingProp);
                    if (existingProp != null)
                    {
                        attached.Remove(entry.Name);
                    }

                    var prop = new PropSwapPlan
                    {
                        Source = entry.Prefab != null ? entry.Prefab.transform : null,
                        Name = entry.Name,
                        AttachBoneName = entry.AttachBoneName,
                        LocalOffset = Matrix4x4.TRS(entry.LocalPosition, entry.LocalRotation, entry.LocalScale),
                        AttachBoneMissing = string.IsNullOrEmpty(entry.AttachBoneName) ||
                                            plan.Bones.Find(entry.AttachBoneName) == null,
                        Selected = options.IsPropSelected(entry.Name),
                        Existing = existingProp,
                        LibraryEntry = entry
                    };

                    if (entry.IsSkinnedMesh && entry.BoneNames != null)
                    {
                        foreach (string boneName in entry.BoneNames.Where(name => !string.IsNullOrEmpty(name)))
                        {
                            if (plan.Bones.Find(boneName) == null && !prop.MissingBones.Contains(boneName))
                            {
                                prop.MissingBones.Add(boneName);
                            }
                        }
                    }

                    plan.Props.Add(prop);
                }
            }

            // The incoming model may carry no accessories at all (character_001 has none). Everything the
            // previous character already wears is inherited so the same props survive the swap, re-anchored
            // to whatever bone the new hierarchy offers.
            foreach (GameObject inherited in attached.Values)
            {
                plan.Props.Add(BuildInheritedProp(plan, options, inherited.transform));
            }

            // Anything the user has unticked is queued for removal rather than silently kept.
            plan.UnselectedProps.AddRange(plan.Props
                .Where(p => !p.Selected && p.Existing != null)
                .Select(p => p.Existing));
        }

        internal static List<Transform> FindRigidPropRoots(GameObject modelRoot)
        {
            if (modelRoot == null)
            {
                return new List<Transform>();
            }

            return modelRoot.transform
                .GetComponentsInChildren<Transform>(true)
                .Where(transform => IsPropRoot(transform, modelRoot.transform))
                .ToList();
        }

        /// <summary>
        /// Turns a prop already attached to the character into a plan entry. It keeps its own transform and
        /// is selected by default, since the user configured it on the previous character.
        /// </summary>
        private static PropSwapPlan BuildInheritedProp(CharacterSwapPlan plan, CharacterSwapOptions options, Transform source)
        {
            var prop = new PropSwapPlan
            {
                Source = source,
                Existing = source.gameObject,
                Name = source.name,
                Inherited = true,
                LocalOffset = LocalMatrix(source),
                Selected = options.IsPropSelected(source.name, true)
            };

            Transform anchor = source.parent;
            while (anchor != null && anchor != plan.SkeletonRoot && plan.Bones.Find(anchor.name) == null)
            {
                prop.LocalOffset = LocalMatrix(anchor) * prop.LocalOffset;
                anchor = anchor.parent;
            }

            prop.AttachBoneName = anchor != null ? anchor.name : null;
            prop.AttachBoneMissing = anchor == null || plan.Bones.Find(anchor.name) == null;

            return prop;
        }

        private static bool IsPropRoot(Transform candidate, Transform modelRoot)
        {
            if (candidate == modelRoot || !HasRigidMesh(candidate))
            {
                return false;
            }

            // A prop is copied with its own children, so only the topmost mesh node is a root, and
            // anything skinned belongs to the Skinned Mesh Renderer pass instead.
            if (candidate.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
            {
                return false;
            }

            for (Transform parent = candidate.parent; parent != null && parent != modelRoot; parent = parent.parent)
            {
                if (HasRigidMesh(parent))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasRigidMesh(Transform candidate)
        {
            if (candidate.GetComponent<SkinnedMeshRenderer>() != null)
            {
                return false;
            }

            MeshFilter filter = candidate.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null && candidate.GetComponent<MeshRenderer>() != null;
        }

        /// <summary>
        /// Accessories already hanging off the character's bones from a previous swap, keyed by name.
        /// Only bone children count, so unrelated scene objects under the character are left alone.
        /// </summary>
        private static Dictionary<string, GameObject> FindAttachedProps(Transform skeletonRoot)
        {
            var props = new Dictionary<string, GameObject>();
            if (skeletonRoot == null)
            {
                return props;
            }

            foreach (Transform candidate in skeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!IsPropRoot(candidate, skeletonRoot) || props.ContainsKey(candidate.name))
                {
                    continue;
                }

                props.Add(candidate.name, candidate.gameObject);
            }

            return props;
        }

        private static Matrix4x4 LocalMatrix(Transform transform)
        {
            return Matrix4x4.TRS(transform.localPosition, transform.localRotation, transform.localScale);
        }

        private static void ApplyProps(
            CharacterSwapPlan plan,
            CharacterSwapOptions options,
            CharacterSwapResult result,
            List<KeyValuePair<Mesh, string>> generated)
        {
            foreach (PropSwapPlan prop in plan.Props)
            {
                bool isLibrarySkinnedProp = prop.LibraryEntry != null && prop.LibraryEntry.IsSkinnedMesh;
                if ((prop.Source == null && !isLibrarySkinnedProp) || !prop.Selected)
                {
                    continue;
                }

                Transform parent = prop.AttachBoneName != null ? plan.Bones.Find(prop.AttachBoneName) : null;
                if (parent == null)
                {
                    parent = plan.SkeletonRoot;
                }

                // A prop inherited from the character is kept as it is and only re-anchored, so its mesh,
                // materials and transform survive the swap untouched.
                if (prop.Inherited && prop.Existing != null)
                {
                    Transform existing = prop.Existing.transform;
                    Undo.RecordObject(existing, "Attach character prop");

                    if (existing.parent != parent)
                    {
                        Undo.SetTransformParent(existing, parent, "Attach character prop");
                    }

                    existing.localPosition = prop.LocalOffset.GetColumn(3);
                    existing.localRotation = prop.LocalOffset.rotation;
                    existing.localScale = prop.LocalOffset.lossyScale;
                    SetLayerRecursively(prop.Existing, parent.gameObject.layer);

                    result.PropsAttached++;
                    continue;
                }

                // The incoming prop may differ in mesh, materials or child objects, so the old copy is
                // replaced wholesale rather than patched.
                if (prop.Existing != null)
                {
                    Undo.DestroyObjectImmediate(prop.Existing);
                    prop.Existing = null;
                }

                GameObject instance = isLibrarySkinnedProp
                    ? new GameObject(prop.Name)
                    : (GameObject)UnityEngine.Object.Instantiate(prop.Source.gameObject);
                instance.name = prop.Name;
                Undo.RegisterCreatedObjectUndo(instance, "Attach character prop");

                instance.transform.SetParent(parent, false);
                instance.transform.localPosition = prop.LocalOffset.GetColumn(3);
                instance.transform.localRotation = prop.LocalOffset.rotation;
                instance.transform.localScale = prop.LocalOffset.lossyScale;

                if (prop.LibraryEntry != null && prop.LibraryEntry.IsSkinnedMesh)
                {
                    ApplyLibrarySkinnedProp(plan, prop, instance, parent, options, generated);
                }

                SetLayerRecursively(instance, parent.gameObject.layer);

                prop.Existing = instance;
                result.PropsAttached++;
            }

            if (!options.RemoveUnselectedProps)
            {
                return;
            }

            foreach (GameObject unselected in plan.UnselectedProps.Where(p => p != null).Distinct())
            {
                result.PropsRemoved++;
                Undo.DestroyObjectImmediate(unselected);
            }
        }

        private static void ApplyLibrarySkinnedProp(
            CharacterSwapPlan plan,
            PropSwapPlan prop,
            GameObject instance,
            Transform parent,
            CharacterSwapOptions options,
            List<KeyValuePair<Mesh, string>> generated)
        {
            CharacterPropLibraryEntry entry = prop.LibraryEntry;
            if (entry.SkinnedMesh == null)
            {
                return;
            }

            SkinnedMeshRenderer renderer = instance.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null)
            {
                renderer = instance.AddComponent<SkinnedMeshRenderer>();
            }

            Transform[] bones = new Transform[entry.BoneNames != null ? entry.BoneNames.Length : 0];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = plan.Bones.Find(entry.BoneNames[i]);
            }

            Transform rootBone = !string.IsNullOrEmpty(entry.RootBoneName)
                ? plan.Bones.Find(entry.RootBoneName)
                : parent;

            Mesh mesh = UnityEngine.Object.Instantiate(entry.SkinnedMesh);
            mesh.name = string.Format("{0}__on_{1}", entry.SkinnedMesh.name, plan.Target.name);
            Matrix4x4 meshToWorld = parent.localToWorldMatrix * Matrix4x4.TRS(
                prop.LocalOffset.GetColumn(3),
                prop.LocalOffset.rotation,
                prop.LocalOffset.lossyScale);
            Matrix4x4[] bindposes = new Matrix4x4[mesh.bindposes.Length];
            for (int i = 0; i < bindposes.Length; i++)
            {
                bindposes[i] = i < bones.Length && bones[i] != null
                    ? bones[i].worldToLocalMatrix * meshToWorld
                    : mesh.bindposes[i];
            }

            mesh.bindposes = bindposes;
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = rootBone;
            renderer.sharedMaterials = entry.Materials ?? Array.Empty<Material>();
            renderer.localBounds = entry.LocalBounds;
            renderer.quality = entry.Quality;
            renderer.updateWhenOffscreen = entry.UpdateWhenOffscreen;
            renderer.skinnedMotionVectors = entry.SkinnedMotionVectors;

            generated.Add(new KeyValuePair<Mesh, string>(
                mesh,
                string.Format("{0}/{1}.asset", options.GeneratedMeshFolder.TrimEnd('/'), Sanitise(mesh.name))));
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
            }
        }

        private static void MeasureRestPose(CharacterSwapPlan plan)
        {
            if (plan.ModelSkeletonRoot == null)
            {
                return;
            }

            foreach (Transform modelBone in plan.ModelSkeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                Transform targetBone = plan.Bones.Find(modelBone.name);
                if (targetBone == null)
                {
                    continue;
                }

                plan.SharedBoneCount++;

                float delta = Vector3.Distance(targetBone.localPosition, modelBone.localPosition);
                if (delta > PositionTolerance)
                {
                    plan.MismatchedBoneCount++;
                }

                plan.MaxRestPoseDelta = Mathf.Max(plan.MaxRestPoseDelta, delta);
            }
        }

        private static void Advise(CharacterSwapPlan plan, CharacterSwapOptions options)
        {
            if (plan.RestPoseMatches)
            {
                plan.RecommendedFit = RigFitMode.Direct;
                plan.FitAdvice = "Both rigs share the same rest pose, so the meshes can be swapped as they are.";
                return;
            }

            plan.ClipsAnimateBonePositions = ClipsAnimateBonePositions(plan);

            if (plan.ClipsAnimateBonePositions)
            {
                plan.RecommendedFit = RigFitMode.RebakeBindPoses;
                plan.FitAdvice = string.Format(
                    "The rest poses differ and this character's clips animate bone positions, so they would undo any change to the skeleton. " +
                    "Rebake bind poses to fit '{0}' onto the existing skeleton.",
                    plan.Model.name);
            }
            else
            {
                plan.RecommendedFit = RigFitMode.AdoptModelRestPose;
                plan.FitAdvice = string.Format(
                    "The rest poses differ. Move the skeleton to '{0}' rest pose so the mesh is not stretched.",
                    plan.Model.name);
            }
        }

        /// <summary>
        /// Clips that carry position curves for individual bones re-impose the original rig's proportions
        /// every frame, which makes moving the skeleton's rest pose pointless at runtime.
        /// </summary>
        private static bool ClipsAnimateBonePositions(CharacterSwapPlan plan)
        {
            Animator animator = plan.Target.GetComponent<Animator>();
            if (animator == null)
            {
                animator = plan.Target.GetComponentInChildren<Animator>(true);
            }

            if (animator == null)
            {
                animator = plan.Target.GetComponentInParent<Animator>();
            }

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return false;
            }

            var boneNames = new HashSet<string>(plan.SkeletonRoot
                .GetComponentsInChildren<Transform>(true)
                .Where(t => t != plan.SkeletonRoot)
                .Select(t => t.name));

            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip == null)
                {
                    continue;
                }

                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!binding.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int slash = binding.path.LastIndexOf('/');
                    string leaf = slash >= 0 ? binding.path.Substring(slash + 1) : binding.path;
                    if (boneNames.Contains(leaf))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void AddPlanWarnings(CharacterSwapPlan plan, CharacterSwapOptions options)
        {
            foreach (MeshSwapPlan mesh in plan.Meshes.Where(m => m.MissingBones.Count > 0))
            {
                if (mesh.UngraftableBones.Count > 0)
                {
                    plan.Errors.Add(string.Format(
                        "'{0}' needs bone(s) that exist on neither skeleton: {1}.",
                        mesh.Name, string.Join(", ", mesh.UngraftableBones.ToArray())));
                }
                else if (!options.GraftMissingBones)
                {
                    plan.Warnings.Add(string.Format(
                        "'{0}' needs {1} bone(s) the target skeleton does not have. Turn on 'Graft missing bones' or that part will not deform.",
                        mesh.Name, mesh.MissingBones.Count));
                }
            }

            foreach (PropSwapPlan prop in plan.Props.Where(p => p.Selected && p.AttachBoneMissing))
            {
                plan.Warnings.Add(string.Format(
                    "'{0}' hangs off '{1}', which the character's skeleton does not have. It will be attached to '{2}' instead.",
                    prop.Name, prop.AttachBoneName ?? "the model root", plan.SkeletonRoot.name));
            }

            foreach (PropSwapPlan prop in plan.Props.Where(p => p.Selected && p.MissingBones.Count > 0))
            {
                plan.Warnings.Add(string.Format(
                    "Skinned prop '{0}' has {1} bone(s) that could not be matched on the target: {2}.",
                    prop.Name,
                    prop.MissingBones.Count,
                    string.Join(", ", prop.MissingBones.ToArray())));
            }

            if (options.SwapAttachedProps && options.RemoveUnselectedProps && plan.UnselectedProps.Count > 0)
            {
                plan.Warnings.Add(string.Format(
                    "{0} prop(s) on the target are not selected and will be removed: {1}.",
                    plan.UnselectedProps.Count,
                    string.Join(", ", plan.UnselectedProps.Where(p => p != null).Select(p => p.name).Distinct().ToArray())));
            }

            if (options.RemoveUnusedRenderers && plan.UnusedRenderers.Count > 0)
            {
                plan.Warnings.Add(string.Format(
                    "{0} renderer(s) on the target have no counterpart in '{1}' and will be deleted: {2}.",
                    plan.UnusedRenderers.Count, plan.Model.name,
                    string.Join(", ", plan.UnusedRenderers.Select(r => r.name).ToArray())));
            }
        }

        private static int GraftMissingBones(CharacterSwapPlan plan)
        {
            if (plan.ModelSkeletonRoot == null)
            {
                return 0;
            }

            var modelBones = new Dictionary<string, Transform>();
            foreach (Transform bone in plan.ModelSkeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!modelBones.ContainsKey(bone.name))
                {
                    modelBones.Add(bone.name, bone);
                }
            }

            int grafted = 0;
            foreach (string boneName in plan.Meshes.SelectMany(m => m.MissingBones).Distinct())
            {
                grafted += Graft(boneName, modelBones, plan);
            }

            return grafted;
        }

        private static int Graft(string boneName, Dictionary<string, Transform> modelBones, CharacterSwapPlan plan)
        {
            if (plan.Bones.Find(boneName) != null)
            {
                return 0;
            }

            Transform modelBone;
            if (!modelBones.TryGetValue(boneName, out modelBone) || modelBone.parent == null)
            {
                return 0;
            }

            int grafted = Graft(modelBone.parent.name, modelBones, plan);

            Transform parent = plan.Bones.Find(modelBone.parent.name);
            if (parent == null)
            {
                return grafted;
            }

            var bone = new GameObject(modelBone.name);
            Undo.RegisterCreatedObjectUndo(bone, "Graft bone");
            bone.transform.SetParent(parent, false);
            bone.transform.localPosition = modelBone.localPosition;
            bone.transform.localRotation = modelBone.localRotation;
            bone.transform.localScale = modelBone.localScale;
            bone.layer = parent.gameObject.layer;

            plan.Bones.Add(bone.transform);
            return grafted + 1;
        }

        /// <summary>
        /// Copies the model's bone rest transforms onto the character's existing bones. Mesh bind poses are
        /// baked against the model's rest pose, so rigs that share bone names but not proportions only
        /// deform correctly once the skeleton is moved to match.
        /// </summary>
        private static int AdoptRestPose(CharacterSwapPlan plan)
        {
            if (plan.ModelSkeletonRoot == null)
            {
                return 0;
            }

            int moved = 0;
            foreach (Transform modelBone in plan.ModelSkeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                Transform targetBone = plan.Bones.Find(modelBone.name);
                if (targetBone == null || targetBone == plan.SkeletonRoot)
                {
                    continue;
                }

                bool matches = Vector3.Distance(targetBone.localPosition, modelBone.localPosition) <= PositionTolerance &&
                               Quaternion.Angle(targetBone.localRotation, modelBone.localRotation) <= AngleTolerance &&
                               Vector3.Distance(targetBone.localScale, modelBone.localScale) <= PositionTolerance;
                if (matches)
                {
                    continue;
                }

                Undo.RecordObject(targetBone, "Adopt rest pose");
                targetBone.localPosition = modelBone.localPosition;
                targetBone.localRotation = modelBone.localRotation;
                targetBone.localScale = modelBone.localScale;
                moved++;
            }

            return moved;
        }

        private static void ApplyMesh(CharacterSwapPlan plan, MeshSwapPlan mesh, CharacterSwapOptions options,
            CharacterSwapResult result, List<KeyValuePair<Mesh, string>> generated)
        {
            SkinnedMeshRenderer renderer = mesh.Existing;
            if (renderer == null)
            {
                var host = new GameObject(mesh.Name);
                Undo.RegisterCreatedObjectUndo(host, "Add character mesh");
                host.transform.SetParent(plan.MeshParent, false);
                host.layer = plan.Template != null ? plan.Template.gameObject.layer : plan.Target.layer;
                renderer = Undo.AddComponent<SkinnedMeshRenderer>(host);
                CopyRendererSettings(plan.Template != null ? plan.Template : mesh.Source, renderer);
                mesh.Existing = renderer;
            }

            Dictionary<string, float> blendShapes = options.CopyBlendShapeWeights
                ? CaptureBlendShapeWeights(renderer)
                : null;

            Transform[] bones = RemapBones(mesh.Source.bones, plan.Bones);

            Mesh sharedMesh = mesh.Source.sharedMesh;
            if (plan.EffectiveFit == RigFitMode.RebakeBindPoses)
            {
                Mesh rebaked = RebakeBindPoses(plan, mesh, bones);
                if (rebaked != null)
                {
                    sharedMesh = rebaked;
                    result.MeshesRebaked++;
                    generated.Add(new KeyValuePair<Mesh, string>(rebaked, GeneratedMeshPath(plan, rebaked, options)));
                }
            }

            Undo.RecordObject(renderer, "Swap character mesh");

            renderer.sharedMesh = sharedMesh;
            renderer.bones = bones;
            renderer.rootBone = mesh.Source.rootBone != null
                ? plan.Bones.Find(mesh.Source.rootBone.name)
                : plan.SkeletonRoot;
            renderer.sharedMaterials = ResolveMaterials(mesh, options);
            renderer.quality = mesh.Source.quality;
            renderer.updateWhenOffscreen = mesh.Source.updateWhenOffscreen;
            renderer.skinnedMotionVectors = mesh.Source.skinnedMotionVectors;
            renderer.localBounds = mesh.Source.localBounds;

            if (blendShapes != null)
            {
                RestoreBlendShapeWeights(renderer, blendShapes);
            }

            EditorUtility.SetDirty(renderer);
        }

        /// <summary>
        /// Writes a copy of the model's mesh whose bind poses are measured against the character's own
        /// skeleton. The mesh then skins onto the existing bones without stretching, and keeps doing so
        /// while clips drive those bones, at the cost of an extra mesh asset per swapped part.
        /// </summary>
        private static Mesh RebakeBindPoses(CharacterSwapPlan plan, MeshSwapPlan mesh, Transform[] bones)
        {
            Matrix4x4[] sourceBindposes = mesh.Source.sharedMesh.bindposes;
            if (sourceBindposes.Length == 0 || bones.Length == 0)
            {
                return null;
            }

            Matrix4x4 modelMeshToWorld = ResolveMeshToWorld(mesh.Source, sourceBindposes);
            Matrix4x4 meshToWorld = plan.Target.transform.localToWorldMatrix *
                                    plan.Model.transform.worldToLocalMatrix * modelMeshToWorld;

            var bindposes = new Matrix4x4[sourceBindposes.Length];
            for (int i = 0; i < bindposes.Length; i++)
            {
                bindposes[i] = i < bones.Length && bones[i] != null
                    ? bones[i].worldToLocalMatrix * meshToWorld
                    : sourceBindposes[i];
            }

            var baked = UnityEngine.Object.Instantiate(mesh.Source.sharedMesh);
            baked.bindposes = bindposes;
            baked.name = Sanitise(string.Format("{0}__{1}__on_{2}", plan.Model.name, mesh.Source.sharedMesh.name, plan.Target.name));

            return baked;
        }

        /// <summary>
        /// Recovers the matrix that maps the mesh's own space to world space at bind time, which is what
        /// the existing bind poses were measured against.
        /// </summary>
        private static Matrix4x4 ResolveMeshToWorld(SkinnedMeshRenderer renderer, Matrix4x4[] bindposes)
        {
            Transform[] bones = renderer.bones;
            for (int i = 0; i < bones.Length && i < bindposes.Length; i++)
            {
                if (bones[i] != null)
                {
                    return bones[i].localToWorldMatrix * bindposes[i];
                }
            }

            return renderer.transform.localToWorldMatrix;
        }

        private static string GeneratedMeshPath(CharacterSwapPlan plan, Mesh baked, CharacterSwapOptions options)
        {
            return string.Format("{0}/{1}.asset", options.GeneratedMeshFolder.TrimEnd('/'), baked.name);
        }

        /// <summary>
        /// Turns the in-memory mesh into an asset. The renderer already points at this object, so the
        /// reference survives and there is nothing to reassign.
        /// </summary>
        private static void SaveGeneratedMesh(Mesh baked, string assetPath)
        {
            if (baked == null || AssetDatabase.Contains(baked))
            {
                return;
            }

            string folder = EnsureFolder(System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            if (folder == null)
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Mesh>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            AssetDatabase.CreateAsset(baked, assetPath);
        }

        private static string EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets", StringComparison.Ordinal))
            {
                return null;
            }

            // Created on disk rather than through CreateFolder so that a folder deleted outside Unity,
            // which the asset database still believes in, is repaired rather than failing the swap.
            string absolute = Application.dataPath + folder.Substring("Assets".Length);
            bool existed = System.IO.Directory.Exists(absolute);
            if (!existed)
            {
                System.IO.Directory.CreateDirectory(absolute);
            }

            if (!existed || !AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.Refresh();
            }

            return AssetDatabase.IsValidFolder(folder) ? folder : null;
        }

        private static string Sanitise(string name)
        {
            return Regex.Replace(name, "[^A-Za-z0-9_.-]", "_");
        }

        private static Transform[] RemapBones(Transform[] sourceBones, BoneMap bones)
        {
            var remapped = new Transform[sourceBones.Length];
            for (int i = 0; i < sourceBones.Length; i++)
            {
                remapped[i] = sourceBones[i] != null ? bones.Find(sourceBones[i].name) : null;
            }

            return remapped;
        }

        private static Material[] ResolveMaterials(MeshSwapPlan mesh, CharacterSwapOptions options)
        {
            Material[] fromModel = mesh.Source.sharedMaterials;
            int slots = mesh.Source.sharedMesh != null
                ? Mathf.Max(1, mesh.Source.sharedMesh.subMeshCount)
                : fromModel.Length;

            Material[] existing = mesh.Existing != null ? mesh.Existing.sharedMaterials : new Material[0];
            var resolved = new Material[slots];

            for (int i = 0; i < slots; i++)
            {
                bool keepExisting = options.Materials == MaterialMode.KeepExistingMaterials &&
                                    i < existing.Length && existing[i] != null;

                resolved[i] = keepExisting ? existing[i] : (i < fromModel.Length ? fromModel[i] : null);
            }

            return resolved;
        }

        private static Dictionary<string, float> CaptureBlendShapeWeights(SkinnedMeshRenderer renderer)
        {
            var weights = new Dictionary<string, float>();
            if (renderer.sharedMesh == null)
            {
                return weights;
            }

            for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
            {
                weights[renderer.sharedMesh.GetBlendShapeName(i)] = renderer.GetBlendShapeWeight(i);
            }

            return weights;
        }

        private static void RestoreBlendShapeWeights(SkinnedMeshRenderer renderer, Dictionary<string, float> weights)
        {
            if (renderer.sharedMesh == null || weights.Count == 0)
            {
                return;
            }

            for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
            {
                float weight;
                if (weights.TryGetValue(renderer.sharedMesh.GetBlendShapeName(i), out weight))
                {
                    renderer.SetBlendShapeWeight(i, weight);
                }
            }
        }

        private static void CopyRendererSettings(Renderer from, Renderer to)
        {
            to.shadowCastingMode = from.shadowCastingMode;
            to.receiveShadows = from.receiveShadows;
            to.lightProbeUsage = from.lightProbeUsage;
            to.reflectionProbeUsage = from.reflectionProbeUsage;
            to.probeAnchor = from.probeAnchor;
            to.motionVectorGenerationMode = from.motionVectorGenerationMode;
            to.allowOcclusionWhenDynamic = from.allowOcclusionWhenDynamic;
            to.renderingLayerMask = from.renderingLayerMask;
            to.lightmapIndex = -1;
        }

        private static void RefreshBounds(CharacterSwapPlan plan)
        {
            foreach (MeshSwapPlan mesh in plan.Meshes)
            {
                if (mesh.Existing == null || mesh.Existing.updateWhenOffscreen)
                {
                    continue;
                }

                // Recomputes localBounds against the character's own skeleton rather than the model's.
                mesh.Existing.updateWhenOffscreen = true;
                mesh.Existing.updateWhenOffscreen = false;
            }
        }
    }
}
