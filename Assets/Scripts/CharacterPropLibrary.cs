using System;
using System.Collections.Generic;
using UnityEngine;

namespace CharacterCustomisationPotionRush
{
    [CreateAssetMenu(menuName = "Character Customisation Potion Rush/Prop Library", fileName = "CharacterCustomisationPotionRushPropLibrary")]
    public class CharacterPropLibrary : ScriptableObject
    {
        public List<CharacterPropLibraryEntry> Props = new List<CharacterPropLibraryEntry>();
    }

    [Serializable]
    public class CharacterPropLibraryEntry
    {
        public string Name;
        public GameObject Prefab;
        public bool IsSkinnedMesh;
        public Mesh SkinnedMesh;
        public Material[] Materials = Array.Empty<Material>();
        public string[] BoneNames = Array.Empty<string>();

        /// <summary>
        /// For every entry in <see cref="BoneNames"/>, the names of that bone's ancestors from the closest
        /// parent upwards, joined with '/'. It lets a garment fall back to the nearest ancestor a different
        /// skeleton does have, so a robe never ends up with an unbound (null) bone.
        /// </summary>
        public string[] BoneParentNames = Array.Empty<string>();

        public string RootBoneName;

        /// <summary>
        /// The model the garment or prop was extracted from, shown in the editor so several wardrobes can
        /// live in one library.
        /// </summary>
        public string SourceModelName;
        public string AttachBoneName;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation = Quaternion.identity;
        public Vector3 LocalScale = Vector3.one;
        public Bounds LocalBounds;
        public SkinQuality Quality = SkinQuality.Auto;
        public bool UpdateWhenOffscreen;
        public bool SkinnedMotionVectors = true;
    }
}
