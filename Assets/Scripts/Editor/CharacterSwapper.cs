using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CharacterCustomisation
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

            return null;
        }

        public static string Normalise(string boneName)
        {
            int separator = boneName.LastIndexOf(':');
            string trimmed = separator >= 0 ? boneName.Substring(separator + 1) : boneName;
            return trimmed.Replace(" ", string.Empty).ToLowerInvariant();
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
        public readonly List<SkinnedMeshRenderer> UnusedRenderers = new List<SkinnedMeshRenderer>();
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
        public int MeshesRebaked;
        public readonly List<string> Messages = new List<string>();
    }

    /// <summary>
    /// Swaps the skinned meshes of a character over to a different model while keeping the character's
    /// existing skeleton GameObjects. Bones are matched by name, so animation clip paths, the Animator
    /// and anything else holding a bone reference stay valid across a swap.
    /// </summary>
    public static class CharacterSwapper
    {
        private const float PositionTolerance = 0.001f;
        private const float AngleTolerance = 0.01f;

        public static CharacterSwapPlan BuildPlan(GameObject target, GameObject model, CharacterSwapOptions options)
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
            plan.ModelSkeletonRoot = ResolveSkeletonRoot(model.transform, sourceRenderers);
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
                "{0} renderer(s) updated, {1} created, {2} removed, {3} bone(s) grafted, {4} bone(s) repositioned, {5} mesh(es) rebaked.",
                result.RenderersUpdated, result.RenderersCreated, result.RenderersRemoved,
                result.BonesGrafted, result.BonesRepositioned, result.MeshesRebaked));

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
