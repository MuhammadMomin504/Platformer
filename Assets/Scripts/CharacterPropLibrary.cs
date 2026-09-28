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
        public string RootBoneName;
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
