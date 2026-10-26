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

        /// <summary>
        /// Source recorded for props added by hand, so re-extracting the reference model never removes them.
        /// </summary>
        public const string AddedPropSource = "Added prop";

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

            CharacterPropLibrary library = LoadOrCreateLibrary();

            Undo.RecordObject(library, "Extract character props");

            // Only this model's own entries are refreshed; clothing added from other models stays in the
            // library so a re-extract never wipes the user's wardrobe.
            library.Props.RemoveAll(entry => entry == null ||
                                             string.IsNullOrEmpty(entry.SourceModelName) ||
                                             entry.SourceModelName == sourceModel.name);

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
                    SourceModelName = sourceModel.name,
                    LocalPosition = offset.GetColumn(3),
                    LocalRotation = offset.rotation,
                    LocalScale = offset.lossyScale
                });
            }

            foreach (SkinnedMeshRenderer renderer in skinnedProps)
            {
                CharacterPropLibraryEntry entry = BuildSkinnedEntry(renderer, sourceModel.name);
                if (entry != null)
                {
                    library.Props.Add(entry);
                }
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return library;
        }

        [MenuItem("Tools/Character Customisation Potion Rush/Add Clothing From Selected Model")]
        private static void AddClothingFromSelection()
        {
            var model = Selection.activeObject as GameObject;
            if (model == null)
            {
                EditorUtility.DisplayDialog(
                    "Character Clothing",
                    "Select a model or prefab that contains the garment's Skinned Mesh Renderer first.",
                    "OK");
                return;
            }

            CharacterPropLibrary library = AddSkinnedGarments(LoadOrCreateLibrary(), model);
            if (library != null)
            {
                Selection.activeObject = library;
                EditorGUIUtility.PingObject(library);
            }
        }

        /// <summary>
        /// Adds every skinned mesh of <paramref name="model"/> to the library as clothing, so a robe or any
        /// other garment authored on its own rig can be offered alongside the rigid accessories. Existing
        /// entries of the same name are refreshed rather than duplicated.
        /// </summary>
        public static CharacterPropLibrary AddSkinnedGarments(CharacterPropLibrary library, GameObject model)
        {
            if (model == null)
            {
                return library;
            }

            if (library == null)
            {
                library = LoadOrCreateLibrary();
            }

            List<SkinnedMeshRenderer> garments = model
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer.sharedMesh != null)
                .ToList();

            if (garments.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Character Clothing",
                    string.Format("'{0}' has no Skinned Mesh Renderer to add as clothing.", model.name),
                    "OK");
                return library;
            }

            EnsureFolder(LibraryFolderPath);
            EnsureFolder(MeshFolderPath);

            Undo.RecordObject(library, "Add character clothing");

            foreach (SkinnedMeshRenderer renderer in garments)
            {
                CharacterPropLibraryEntry entry = BuildSkinnedEntry(renderer, model.name);
                if (entry == null)
                {
                    continue;
                }

                int existing = library.Props.FindIndex(p => p != null && p.Name == entry.Name);
                if (existing >= 0)
                {
                    library.Props[existing] = entry;
                }
                else
                {
                    library.Props.Add(entry);
                }
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return library;
        }

        /// <summary>
        /// Adds a rigid accessory that no model carries (a staff, glasses, wings...) to the library. It is saved
        /// as its own prefab and attached to <paramref name="boneName"/> at the given offset whenever it is ticked.
        /// An existing entry of the same name is replaced.
        /// </summary>
        public static CharacterPropLibrary AddRigidProp(
            CharacterPropLibrary library,
            GameObject source,
            string boneName,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale)
        {
            if (source == null || string.IsNullOrEmpty(boneName))
            {
                return library;
            }

            if (source.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
            {
                EditorUtility.DisplayDialog(
                    "Character Prop",
                    string.Format("'{0}' is skinned. Use 'Add clothing' for garments that deform with the body.", source.name),
                    "OK");
                return library;
            }

            bool hasMesh = source.GetComponentsInChildren<MeshFilter>(true)
                .Any(filter => filter.sharedMesh != null && filter.GetComponent<MeshRenderer>() != null);
            if (!hasMesh)
            {
                EditorUtility.DisplayDialog(
                    "Character Prop",
                    string.Format("'{0}' has no Mesh Renderer to add as a prop.", source.name),
                    "OK");
                return library;
            }

            if (library == null)
            {
                library = LoadOrCreateLibrary();
            }

            EnsureFolder(LibraryFolderPath);
            EnsureFolder(PrefabFolderPath);

            string prefabPath = string.Format("{0}/{1}.prefab", PrefabFolderPath, MakeSafeFileName(source.name));
            GameObject prefab = SavePropPrefab(source.transform, prefabPath);
            if (prefab == null)
            {
                return library;
            }

            Undo.RecordObject(library, "Add character prop");

            var entry = new CharacterPropLibraryEntry
            {
                Name = source.name,
                Prefab = prefab,
                AttachBoneName = boneName,
                SourceModelName = AddedPropSource,
                LocalPosition = localPosition,
                LocalRotation = localRotation,
                LocalScale = localScale
            };

            int existing = library.Props.FindIndex(p => p != null && p.Name == entry.Name);
            if (existing >= 0)
            {
                library.Props[existing] = entry;
            }
            else
            {
                library.Props.Add(entry);
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        public static CharacterPropLibrary LoadOrCreateLibrary()
        {
            CharacterPropLibrary library = AssetDatabase.LoadAssetAtPath<CharacterPropLibrary>(LibraryAssetPath);
            if (library != null)
            {
                return library;
            }

            EnsureFolder(LibraryFolderPath);
            library = ScriptableObject.CreateInstance<CharacterPropLibrary>();
            AssetDatabase.CreateAsset(library, LibraryAssetPath);
            return library;
        }

        /// <summary>
        /// Records everything needed to rebuild a garment on another skeleton: its mesh, materials, the
        /// bone names it is skinned to and each bone's ancestor chain, plus where it sits relative to its
        /// root bone so it lands in the right place on the body.
        /// </summary>
        private static CharacterPropLibraryEntry BuildSkinnedEntry(SkinnedMeshRenderer renderer, string sourceModelName)
        {
            Transform rootBone = renderer.rootBone != null
                ? renderer.rootBone
                : renderer.bones.FirstOrDefault(bone => bone != null);
            if (rootBone == null)
            {
                Debug.LogWarning(string.Format(
                    "Skipping skinned prop '{0}' because it has no root bone.",
                    renderer.name), renderer);
                return null;
            }

            string meshPath = string.Format(
                "{0}/{1}.asset",
                MeshFolderPath,
                MakeSafeFileName(renderer.sharedMesh.name));
            Mesh meshAsset = SaveMeshAsset(renderer.sharedMesh, meshPath);
            if (meshAsset == null)
            {
                return null;
            }

            Matrix4x4 offset = rootBone.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            return new CharacterPropLibraryEntry
            {
                Name = renderer.name,
                IsSkinnedMesh = true,
                SkinnedMesh = meshAsset,
                Materials = renderer.sharedMaterials,
                BoneNames = renderer.bones
                    .Select(bone => bone != null ? bone.name : string.Empty)
                    .ToArray(),
                BoneParentNames = renderer.bones
                    .Select(DescribeAncestors)
                    .ToArray(),
                RootBoneName = rootBone.name,
                AttachBoneName = rootBone.name,
                SourceModelName = sourceModelName,
                LocalPosition = offset.GetColumn(3),
                LocalRotation = offset.rotation,
                LocalScale = offset.lossyScale,
                LocalBounds = renderer.localBounds,
                Quality = renderer.quality,
                UpdateWhenOffscreen = renderer.updateWhenOffscreen,
                SkinnedMotionVectors = renderer.skinnedMotionVectors
            };
        }

        private static string DescribeAncestors(Transform bone)
        {
            if (bone == null)
            {
                return string.Empty;
            }

            var names = new List<string>();
            for (Transform parent = bone.parent; parent != null; parent = parent.parent)
            {
                names.Add(parent.name);
            }

            return string.Join("/", names.ToArray());
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
