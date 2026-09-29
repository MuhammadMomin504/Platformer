using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CharacterCustomisationPotionRush
{
    /// <summary>
    /// Editor window for swapping a rigged character's model without rebuilding its Skinned Mesh Renderers.
    /// </summary>
    public class CharacterCustomisationPotionRushWindow : EditorWindow
    {
        private const string LibraryFolderKey = "CharacterCustomisationPotionRush.LibraryFolder";
        private const string DefaultLibraryFolder = "Assets/Art/Characters";

        [SerializeField] private GameObject _target;
        [SerializeField] private GameObject _model;
        [SerializeField] private CharacterPropLibrary _propLibrary;
        [SerializeField] private CharacterSwapOptions _options = new CharacterSwapOptions();
        [SerializeField] private string _libraryFolder = DefaultLibraryFolder;
        [SerializeField] private bool _swapOnSelect = true;
        [SerializeField] private bool _showOptions = true;
        [SerializeField] private bool _showProps = true;

        private readonly List<GameObject> _library = new List<GameObject>();
        private CharacterSwapPlan _plan;
        private CharacterSwapResult _lastResult;
        private Vector2 _scroll;
        private GUIStyle _tileStyle;

        [MenuItem("Tools/Open Character Customisation Tool")]
        public static void Open()
        {
            CharacterCustomisationPotionRushWindow window = GetWindow<CharacterCustomisationPotionRushWindow>("Character Customisation Potion Rush");
            window.minSize = new Vector2(360f, 420f);
            window.Show();
        }

        [MenuItem("Window/Character Customisation Potion Rush", false, 2010)]
        private static void OpenFromWindowMenu()
        {
            Open();
        }

        private void OnEnable()
        {
            _libraryFolder = EditorPrefs.GetString(LibraryFolderKey, DefaultLibraryFolder);
            if (_propLibrary == null)
            {
                _propLibrary = AssetDatabase.LoadAssetAtPath<CharacterPropLibrary>(
                    CharacterCustomisationPotionRushPropExtractor.LibraryAssetPath);
            }

            if (_target == null)
            {
                _target = FindCharacterIn(Selection.activeGameObject);
            }

            RefreshLibrary();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            _plan = null;
            Repaint();
        }

        private void OnSelectionChange()
        {
            GameObject character = FindCharacterIn(Selection.activeGameObject);
            if (character != null && character != _target)
            {
                _target = character;
                _plan = null;
                Repaint();
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawTargetSection();
            EditorGUILayout.Space();
            DrawLibrarySection();
            EditorGUILayout.Space();
            DrawOptionsSection();
            EditorGUILayout.Space();
            DrawPropsSection();
            EditorGUILayout.Space();
            DrawReportSection();
            EditorGUILayout.Space();
            DrawActionsSection();

            EditorGUILayout.EndScrollView();

            if (AssetPreview.IsLoadingAssetPreviews())
            {
                Repaint();
            }
        }

        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("Character", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _target = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Target", "The rigged character in the scene whose meshes will be replaced."),
                _target, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                _plan = null;
            }

            if (_target == null)
            {
                EditorGUILayout.HelpBox("Select a character in the Hierarchy, or drag one into the Target field.", MessageType.Info);
                return;
            }

            var renderers = _target.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList();
            Transform skeleton = CharacterCustomisationPotionRushSwapper.ResolveSkeletonRoot(_target.transform, renderers);
            int boneCount = skeleton != null ? skeleton.GetComponentsInChildren<Transform>(true).Length : 0;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Skeleton root", skeleton != null ? skeleton.name : "not found");
                EditorGUILayout.TextField("Setup", string.Format("{0} skinned mesh(es), {1} bones", renderers.Count, boneCount));
                EditorGUILayout.TextField("Current model", DescribeCurrentModel(renderers));
            }
        }

        private void DrawLibrarySection()
        {
            EditorGUILayout.LabelField("Model Library", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _libraryFolder = EditorGUILayout.TextField("Folder", _libraryFolder);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetString(LibraryFolderKey, _libraryFolder);
                    RefreshLibrary();
                }

                if (GUILayout.Button("Browse", GUILayout.Width(60f)))
                {
                    BrowseForFolder();
                }

                if (GUILayout.Button("Refresh", GUILayout.Width(60f)))
                {
                    RefreshLibrary();
                }
            }

            if (_library.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    string.Format("No rigged models found in '{0}'. Point the folder at your character models, or drag one into the Model field below.", _libraryFolder),
                    MessageType.Info);
            }
            else
            {
                DrawLibraryGrid();
            }

            EditorGUI.BeginChangeCheck();
            _model = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Model", "The model to swap in. Accepts an imported model asset or another character in the scene."),
                _model, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                _plan = null;
            }

            _swapOnSelect = EditorGUILayout.ToggleLeft(
                new GUIContent("Swap immediately when a model is picked", "Applies the swap as soon as you click a model so you can preview it in the Scene view."),
                _swapOnSelect);
        }

        private void DrawLibraryGrid()
        {
            if (_tileStyle == null)
            {
                _tileStyle = new GUIStyle(GUI.skin.button)
                {
                    imagePosition = ImagePosition.ImageAbove,
                    fixedHeight = 92f,
                    wordWrap = true,
                    fontSize = 10
                };
            }

            GUIContent[] tiles = _library.Select(BuildTile).ToArray();
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 30f) / 88f));
            int selected = _library.IndexOf(_model);

            EditorGUI.BeginChangeCheck();
            selected = GUILayout.SelectionGrid(selected, tiles, columns, _tileStyle);
            if (EditorGUI.EndChangeCheck() && selected >= 0 && selected < _library.Count)
            {
                _model = _library[selected];
                _plan = null;

                if (_swapOnSelect)
                {
                    ApplySwap();
                }
            }
        }

        private GUIContent BuildTile(GameObject model)
        {
            Texture2D preview = AssetPreview.GetAssetPreview(model) ?? AssetPreview.GetMiniThumbnail(model);
            return new GUIContent(model.name, preview, AssetDatabase.GetAssetPath(model));
        }

        private void DrawOptionsSection()
        {
            _showOptions = EditorGUILayout.Foldout(_showOptions, "Options", true, EditorStyles.foldoutHeader);
            if (!_showOptions)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.IndentLevelScope())
            {
                _options.Materials = (MaterialMode)EditorGUILayout.EnumPopup(
                    new GUIContent("Materials", "Take materials from the incoming model, or keep the ones already on the character."),
                    _options.Materials);

                _options.RigFit = (RigFitMode)EditorGUILayout.EnumPopup(
                    new GUIContent("Rig fit", "How to reconcile a model whose skeleton has the same bone names but a different rest pose. Auto picks per model."),
                    _options.RigFit);

                if (_options.RigFit != RigFitMode.Direct)
                {
                    _options.GeneratedMeshFolder = EditorGUILayout.TextField(
                        new GUIContent("Rebaked mesh folder", "Where the refitted mesh assets are written."),
                        _options.GeneratedMeshFolder);
                }

                _options.GraftMissingBones = EditorGUILayout.Toggle(
                    new GUIContent("Graft missing bones", "Adds any bone the incoming model needs that the character's skeleton does not have."),
                    _options.GraftMissingBones);

                _options.RemoveUnusedRenderers = EditorGUILayout.Toggle(
                    new GUIContent("Remove unused renderers", "Deletes mesh parts the incoming model does not use."),
                    _options.RemoveUnusedRenderers);

                _options.SwapAttachedProps = EditorGUILayout.Toggle(
                    new GUIContent("Handle attached props", "Detects accessories parented straight to a bone, such as hats, hair, bags and belts, so you can pick which ones to add."),
                    _options.SwapAttachedProps);

                if (_options.SwapAttachedProps)
                {
                    _options.RemoveUnselectedProps = EditorGUILayout.Toggle(
                        new GUIContent("Remove unselected props", "Takes accessories off the character again once they are unticked."),
                        _options.RemoveUnselectedProps);
                }

                _options.CopyBlendShapeWeights = EditorGUILayout.Toggle(
                    new GUIContent("Keep blend shape weights", "Re-applies blend shape weights by name after the mesh is replaced."),
                    _options.CopyBlendShapeWeights);

                _options.IgnoreBoneNamespace = EditorGUILayout.Toggle(
                    new GUIContent("Ignore bone namespace", "Matches 'mixamorig:Hips' to 'Hips' when exporters disagree on the bone prefix."),
                    _options.IgnoreBoneNamespace);
            }

            if (EditorGUI.EndChangeCheck())
            {
                _plan = null;
            }
        }

        /// <summary>
        /// The props the chosen model offers, each with its own tick box. Nothing is attached unless it is
        /// ticked here, so a model never drags its whole wardrobe onto the character.
        /// </summary>
        private void DrawPropsSection()
        {
            if (!_options.SwapAttachedProps)
            {
                return;
            }

            _showProps = EditorGUILayout.Foldout(_showProps, "Props & Accessories", true, EditorStyles.foldoutHeader);
            if (!_showProps)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            _propLibrary = (CharacterPropLibrary)EditorGUILayout.ObjectField(
                new GUIContent("Reusable prop library", "Props extracted from the reference character; available regardless of which character model is selected."),
                _propLibrary,
                typeof(CharacterPropLibrary),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                _plan = null;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(
                    _propLibrary != null
                        ? string.Format("{0} reusable prop(s) loaded.", _propLibrary.Props != null ? _propLibrary.Props.Count : 0)
                        : "No extracted prop library is assigned.",
                    _propLibrary != null ? MessageType.Info : MessageType.Warning);

                if (GUILayout.Button("Extract from reference FBX", GUILayout.Width(160f), GUILayout.Height(36f)))
                {
                    _propLibrary = CharacterCustomisationPotionRushPropExtractor.Extract(
                        CharacterCustomisationPotionRushPropExtractor.SourceModelPath);
                    _plan = null;
                }
            }

            CharacterSwapPlan plan = CurrentPlan();
            if (plan.Props.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    _model == null
                        ? "Pick a model to see the props it offers."
                        : string.Format("Neither '{0}' nor the character has any bone attached props.", _model.name),
                    MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    string.Format("{0} of {1} selected", plan.Props.Count(p => p.Selected), plan.Props.Count),
                    EditorStyles.miniLabel);

                if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(44f)))
                {
                    SelectAllProps(plan, true);
                }

                if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(44f)))
                {
                    SelectAllProps(plan, false);
                }
            }

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (PropSwapPlan prop in plan.Props)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        bool selected = EditorGUILayout.ToggleLeft(
                            new GUIContent(
                                prop.Inherited ? string.Format("{0}  (kept)", prop.Name) : prop.Name,
                                prop.Inherited
                                    ? string.Format("Already on the character. Attaches to '{0}'.", prop.AttachBoneName)
                                    : string.Format("Attaches to '{0}'.", prop.AttachBoneName)),
                            prop.Selected);
                        if (EditorGUI.EndChangeCheck())
                        {
                            _options.SetPropSelected(prop.Name, selected);
                            _plan = null;
                        }

                        EditorGUILayout.LabelField(
                            prop.AttachBoneMissing
                                ? string.Format("{0} (missing)", prop.AttachBoneName)
                                : prop.AttachBoneName,
                            EditorStyles.miniLabel,
                            GUILayout.Width(140f));
                    }
                }
            }
        }

        private void SelectAllProps(CharacterSwapPlan plan, bool selected)
        {
            foreach (PropSwapPlan prop in plan.Props)
            {
                _options.SetPropSelected(prop.Name, selected);
            }

            _plan = null;
        }

        private void DrawReportSection()
        {
            EditorGUILayout.LabelField("Compatibility", EditorStyles.boldLabel);

            CharacterSwapPlan plan = CurrentPlan();

            foreach (string error in plan.Errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            if (plan.Meshes.Count == 0)
            {
                return;
            }

            DrawRigFitAdvice(plan);

            foreach (string warning in plan.Warnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            foreach (MeshSwapPlan mesh in plan.Meshes)
            {
                string status = mesh.MissingBones.Count == 0
                    ? string.Format("{0}/{1} bones matched", mesh.MatchedBones, mesh.BoneCount)
                    : string.Format("{0}/{1} bones matched, {2} to graft: {3}",
                        mesh.MatchedBones, mesh.BoneCount, mesh.MissingBones.Count,
                        string.Join(", ", mesh.MissingBones.ToArray()));

                EditorGUILayout.LabelField(
                    new GUIContent(string.Format("{0}{1}", mesh.Name, mesh.IsNew ? "  (new)" : string.Empty)),
                    new GUIContent(status),
                    EditorStyles.miniLabel);
            }

            foreach (PropSwapPlan prop in plan.Props.Where(p => p.Selected))
            {
                EditorGUILayout.LabelField(
                    new GUIContent(string.Format("{0}  (prop)", prop.Name)),
                    new GUIContent(string.Format("attaches to {0}", prop.AttachBoneMissing
                        ? string.Format("{0} (missing)", prop.AttachBoneName)
                        : prop.AttachBoneName)),
                    EditorStyles.miniLabel);
            }
        }

        private void DrawRigFitAdvice(CharacterSwapPlan plan)
        {
            if (plan.RestPoseMatches)
            {
                EditorGUILayout.HelpBox(plan.FitAdvice, MessageType.None);
                return;
            }

            string summary = string.Format(
                "{0} of {1} shared bones differ in rest position (up to {2:F3} units). ",
                plan.MismatchedBoneCount, plan.SharedBoneCount, plan.MaxRestPoseDelta);

            bool followsAdvice = plan.EffectiveFit == plan.RecommendedFit;
            string applying = string.Format(" Applying '{0}'.", ObjectNames.NicifyVariableName(plan.EffectiveFit.ToString()));

            EditorGUILayout.HelpBox(summary + plan.FitAdvice + applying,
                followsAdvice ? MessageType.Info : MessageType.Warning);

            if (followsAdvice)
            {
                return;
            }

            if (GUILayout.Button(string.Format("Set rig fit to '{0}'", ObjectNames.NicifyVariableName(plan.RecommendedFit.ToString()))))
            {
                _options.RigFit = plan.RecommendedFit;
                _plan = null;
            }
        }

        private void DrawActionsSection()
        {
            CharacterSwapPlan plan = CurrentPlan();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!plan.CanApply))
                {
                    if (GUILayout.Button("Apply Swap", GUILayout.Height(28f)))
                    {
                        ApplySwap();
                    }
                }

                if (GUILayout.Button("Undo Last Swap", GUILayout.Height(28f), GUILayout.Width(120f)))
                {
                    Undo.PerformUndo();
                    _plan = null;
                }

                using (new EditorGUI.DisabledScope(_target == null))
                {
                    if (GUILayout.Button("Frame", GUILayout.Height(28f), GUILayout.Width(60f)))
                    {
                        FrameTarget();
                    }
                }
            }

            if (_lastResult != null)
            {
                EditorGUILayout.HelpBox(string.Join("\n", _lastResult.Messages.ToArray()), MessageType.Info);
            }
        }

        private CharacterSwapPlan CurrentPlan()
        {
            return _plan ?? (_plan = CharacterCustomisationPotionRushSwapper.BuildPlan(
                _target, _model, _options, _propLibrary));
        }

        private void ApplySwap()
        {
            _plan = CharacterCustomisationPotionRushSwapper.BuildPlan(
                _target, _model, _options, _propLibrary);
            if (!_plan.CanApply)
            {
                return;
            }

            _lastResult = CharacterCustomisationPotionRushSwapper.Apply(_plan, _options);
            _plan = null;

            EditorSceneManager.MarkSceneDirty(_target.scene);
            SceneView.RepaintAll();
        }

        private void FrameTarget()
        {
            Selection.activeGameObject = _target;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }
        }

        private string DescribeCurrentModel(IEnumerable<SkinnedMeshRenderer> renderers)
        {
            List<string> sources = renderers
                .Where(r => r.sharedMesh != null)
                .Select(r => AssetDatabase.GetAssetPath(r.sharedMesh))
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            if (sources.Count == 0)
            {
                return "unknown";
            }

            return sources.Count == 1
                ? System.IO.Path.GetFileNameWithoutExtension(sources[0])
                : string.Format("mixed ({0} models)", sources.Count);
        }

        private void RefreshLibrary()
        {
            _library.Clear();

            if (string.IsNullOrEmpty(_libraryFolder) || !AssetDatabase.IsValidFolder(_libraryFolder))
            {
                return;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { _libraryFolder }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                {
                    _library.Add(asset);
                }
            }

            _library.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
        }

        private void BrowseForFolder()
        {
            string absolute = EditorUtility.OpenFolderPanel("Model Library Folder", _libraryFolder, string.Empty);
            if (string.IsNullOrEmpty(absolute))
            {
                return;
            }

            if (!absolute.StartsWith(Application.dataPath))
            {
                EditorUtility.DisplayDialog("Character Customisation", "Pick a folder inside this project's Assets folder.", "OK");
                return;
            }

            _libraryFolder = "Assets" + absolute.Substring(Application.dataPath.Length);
            EditorPrefs.SetString(LibraryFolderKey, _libraryFolder);
            RefreshLibrary();
        }

        /// <summary>
        /// Walks up from a clicked bone or mesh part to the object that owns the skinned setup.
        /// Prefers the Animator host so leftover Mixamo armatures under Visual are ignored.
        /// </summary>
        private static GameObject FindCharacterIn(GameObject selected)
        {
            if (selected == null)
            {
                return null;
            }

            Transform current = selected.transform;
            GameObject fallback = null;
            while (current != null)
            {
                if (current.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
                {
                    current = current.parent;
                    continue;
                }

                fallback = current.gameObject;
                if (current.GetComponent<Animator>() != null)
                {
                    return current.gameObject;
                }

                current = current.parent;
            }

            if (fallback == null)
            {
                return null;
            }

            Animator animator = fallback.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
            {
                return animator.gameObject;
            }

            return fallback;
        }
    }
}
