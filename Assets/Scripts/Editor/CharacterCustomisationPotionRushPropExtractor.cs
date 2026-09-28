using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CharacterCustomisationPotionRush
{
    public static class CharacterCustomisationPotionRushPropExtractor
    {
        public const string SourceModelPath = "Assets/Art/Characters/character_animations_01.fbx";
        public const string LibraryFolderPath = "Assets/Art/Characters/CharacterCustomisationPotionRushProps";
        public const string PrefabFolderPath = LibraryFolderPath + "/Prefabs";
        public const string MeshFolderPath = LibraryFolderPath + "/Meshes";
        public const string LibraryAssetPath = LibraryFolderPath + "/CharacterCustomisationPotionRushPropLibrary.asset";

        [MenuItem("Tools/Character Customisation Potion Rush/Extract Props From Reference FBX")]
        public static void ExtractFromReferenceFbx()
        {
            CharacterPropLibrary library = Extract(SourceModelPath);
            if (library != null)
            {
                Selection.activeObject = library;
                EditorGUIUtility.PingObject(library);
            }
        }

        public static CharacterPropLibrary Extract(string sourceModelPath)
        {
            GameObject sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(sourceModelPath);
            if (sourceModel == null)
            {
                EditorUtility.DisplayDialog(
                    "Character Prop Extraction",
                    string.Format("Could not load the reference model at '{0}'.", sourceModelPath),
                    "OK");
                return null;
            }

            List<Transform> propRoots = CharacterCustomisationPotionRushSwapper.FindRigidPropRoots(sourceModel);
            List<SkinnedMeshRenderer> skinnedProps = sourceModel
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer.sharedMesh != null && IsOptionalSkinnedProp(renderer.name))
                .ToList();
            if (propRoots.Count == 0 && skinnedProps.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Character Prop Extraction",
                    string.Format("No rigid props or optional skinned props were found in '{0}'.", sourceModel.name),
                    "OK");
                return null;
            }

            EnsureFolder(LibraryFolderPath);
            EnsureFolder(PrefabFolderPath);
            EnsureFolder(MeshFolderPath);

            CharacterPropLibrary library = AssetDatabase.LoadAssetAtPath<CharacterPropLibrary>(LibraryAssetPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CharacterPropLibrary>();
                AssetDatabase.CreateAsset(library, LibraryAssetPath);
            }

            Undo.RecordObject(library, "Extract character props");
            library.Props.Clear();

            HashSet<Transform> sourceBones = CollectBones(sourceModel);
            foreach (Transform prop in propRoots)
            {
                Matrix4x4 offset = GetOffsetFromAnchor(prop, sourceModel.transform, sourceBones, out Transform anchor);
                string prefabPath = string.Format("{0}/{1}.prefab", PrefabFolderPath, MakeSafeFileName(prop.name));
                GameObject prefab = SavePropPrefab(prop, prefabPath);
                if (prefab == null)
                {
                    continue;
                }

                library.Props.Add(new CharacterPropLibraryEntry
                {
                    Name = prop.name,
                    Prefab = prefab,
                    AttachBoneName = anchor != null ? anchor.name : string.Empty,
                    LocalPosition = offset.GetColumn(3),
                    LocalRotation = offset.rotation,
                    LocalScale = offset.lossyScale
                });
            }

            foreach (SkinnedMeshRenderer renderer in skinnedProps)
            {
                Transform rootBone = renderer.rootBone != null
                    ? renderer.rootBone
                    : renderer.bones.FirstOrDefault(bone => bone != null);
                if (rootBone == null)
                {
                    Debug.LogWarning(string.Format(
                        "Skipping skinned prop '{0}' because it has no root bone.",
                        renderer.name), renderer);
                    continue;
                }

                string meshPath = string.Format(
                    "{0}/{1}.asset",
                    MeshFolderPath,
                    MakeSafeFileName(renderer.sharedMesh.name));
                Mesh meshAsset = SaveMeshAsset(renderer.sharedMesh, meshPath);
                if (meshAsset == null)
                {
                    continue;
                }

                Matrix4x4 offset = rootBone.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                library.Props.Add(new CharacterPropLibraryEntry
                {
                    Name = renderer.name,
                    IsSkinnedMesh = true,
                    SkinnedMesh = meshAsset,
                    Materials = renderer.sharedMaterials,
                    BoneNames = renderer.bones
                        .Select(bone => bone != null ? bone.name : string.Empty)
                        .ToArray(),
                    RootBoneName = rootBone.name,
                    AttachBoneName = rootBone.name,
                    LocalPosition = offset.GetColumn(3),
                    LocalRotation = offset.rotation,
                    LocalScale = offset.lossyScale,
                    LocalBounds = renderer.localBounds,
                    Quality = renderer.quality,
                    UpdateWhenOffscreen = renderer.updateWhenOffscreen,
                    SkinnedMotionVectors = renderer.skinnedMotionVectors
                });
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return library;
        }

        private static HashSet<Transform> CollectBones(GameObject model)
        {
            var bones = new HashSet<Transform>();
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.bones != null)
                {
                    foreach (Transform bone in renderer.bones)
                    {
                        if (bone != null)
                        {
                            bones.Add(bone);
                        }
                    }
                }

                if (renderer.rootBone != null)
                {
                    bones.Add(renderer.rootBone);
                }
            }

            return bones;
        }

        private static Matrix4x4 GetOffsetFromAnchor(
            Transform prop,
            Transform modelRoot,
            HashSet<Transform> sourceBones,
            out Transform anchor)
        {
            Matrix4x4 offset = Matrix4x4.TRS(prop.localPosition, prop.localRotation, prop.localScale);
            anchor = prop.parent;
            while (anchor != null && anchor != modelRoot && !sourceBones.Contains(anchor))
            {
                offset = Matrix4x4.TRS(anchor.localPosition, anchor.localRotation, anchor.localScale) * offset;
                anchor = anchor.parent;
            }

            if (anchor == modelRoot)
            {
                anchor = null;
            }

            return offset;
        }

        private static GameObject SavePropPrefab(Transform source, string assetPath)
        {
            GameObject instance = UnityEngine.Object.Instantiate(source.gameObject);
            instance.name = source.name;
            instance.transform.SetParent(null, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, assetPath);
            UnityEngine.Object.DestroyImmediate(instance);
            return prefab;
        }

        private static Mesh SaveMeshAsset(Mesh source, string assetPath)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(source, existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            Mesh copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name;
            AssetDatabase.CreateAsset(copy, assetPath);
            return AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        }

        private static bool IsOptionalSkinnedProp(string rendererName)
        {
            string name = rendererName.Trim().ToLowerInvariant();
            return name != "arms" && name != "head" && name != "legs" &&
                   name != "torso" && name != "body" && name != "character";
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static string MakeSafeFileName(string name)
        {
            string invalid = new string(Path.GetInvalidFileNameChars());
            return new string(name.Where(character => invalid.IndexOf(character) < 0).ToArray());
        }
    }
}
