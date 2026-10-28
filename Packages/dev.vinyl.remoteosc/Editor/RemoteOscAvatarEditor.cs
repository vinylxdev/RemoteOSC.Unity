// Vinyl - vinyl.dev

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;

namespace RemoteOSC.Unity.Editor
{
    [CustomEditor(typeof(RemoteOscAvatar))]
    public sealed class RemoteOscAvatarEditor : UnityEditor.Editor
    {
        private static string[] Filters => new[] { EditorText.Get("common.all"), EditorText.Get("permissions.shared"), EditorText.Get("permissions.blocked") };
        private readonly HashSet<string> collapsedMenus = new HashSet<string>(StringComparer.Ordinal);
        private SerializedProperty outputDirectory;
        private SerializedProperty blockedParameters;
        private SerializedProperty blockedMenuPaths;
        private SerializedProperty blockedParameterBindings;
        private SerializedProperty blockedGeneratedMenuPaths;
        private AvatarBuildPreview buildPreview;
        private Hash128 previewSourceHash;
        private string previewError = "";
        private bool bIsPreviewStale;
        private bool bIsPreviewQueued;
        private SearchField searchField;
        private string search = "";
        private int selectedTab;
        private int selectedFilter;
        private bool bNeedsInitialTab = true;
        private bool bShowAdvanced;
        private Vector2 scrollPosition;
        private GUIStyle titleStyle;
        private GUIStyle cardStyle;
        private GUIStyle stateStyle;

        private void OnEnable()
        {
            outputDirectory = serializedObject.FindProperty("OutputDirectory");
            blockedParameters = serializedObject.FindProperty("BlockedParameterNames");
            blockedMenuPaths = serializedObject.FindProperty("BlockedMenuPaths");
            blockedParameterBindings = serializedObject.FindProperty("BlockedParameterBindings");
            blockedGeneratedMenuPaths = serializedObject.FindProperty("BlockedGeneratedMenuPaths");
            searchField = new SearchField();
            Undo.undoRedoPerformed += InvalidatePreview;
            Undo.postprocessModifications += OnModifications;
            EditorApplication.hierarchyChanged += InvalidatePreview;
            EditorApplication.projectChanged += InvalidatePreview;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= InvalidatePreview;
            Undo.postprocessModifications -= OnModifications;
            EditorApplication.hierarchyChanged -= InvalidatePreview;
            EditorApplication.projectChanged -= InvalidatePreview;
            EditorApplication.delayCall -= RefreshPreview;
        }

        private UndoPropertyModification[] OnModifications(UndoPropertyModification[] modifications)
        {
            if (modifications.Any(modification => modification.currentValue.target != target))
            {
                InvalidatePreview();
            }

            return modifications;
        }

        private void InvalidatePreview()
        {
            if (buildPreview == null || target == null)
            {
                return;
            }

            var component = (RemoteOscAvatar)target;
            VRCAvatarDescriptor descriptor = component.GetComponentInParent<VRCAvatarDescriptor>();
            bIsPreviewStale = descriptor == null || AvatarBuildPreview.GetSourceHash(descriptor.gameObject) != previewSourceHash;
            Repaint();
        }

        private void RefreshPreview()
        {
            bIsPreviewQueued = false;
            if (target == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return;
            }

            var component = (RemoteOscAvatar)target;
            VRCAvatarDescriptor descriptor = component.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return;
            }

            buildPreview = null;
            previewError = "";
            try
            {
                EditorUtility.DisplayProgressBar("RemoteOSC", EditorText.Get("unity.preview.generating"), 0.5f);
                buildPreview = AvatarBuildPreview.Create(descriptor.gameObject);
                previewSourceHash = AvatarBuildPreview.GetSourceHash(descriptor.gameObject);
                bIsPreviewStale = false;
                bNeedsInitialTab = true;
            }
            catch (Exception error)
            {
                previewError = EditorText.Get("unity.preview.failed", error.Message);
                Debug.LogException(error);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();
            var component = (RemoteOscAvatar)target;
            VRCAvatarDescriptor descriptor = component.GetComponentInParent<VRCAvatarDescriptor>();
            EditorText.DrawLanguageSelector();
            DrawHeader(descriptor);
            DrawOutput(descriptor);
            if (descriptor != null)
            {
                DrawPreviewActions();
                var preview = new AvatarPermissionPreview(
                    buildPreview == null ? AvatarConfigExporter.ReadParameters(descriptor.gameObject, descriptor) : buildPreview.Parameters,
                    buildPreview == null ? AvatarConfigExporter.ReadControls(descriptor.expressionsMenu) : buildPreview.Controls,
                    ReadStrings(blockedParameters), ReadStrings(blockedMenuPaths), component.BlockedParameterBindings);
                using (new EditorGUI.DisabledScope(bIsPreviewStale || bIsPreviewQueued))
                {
                    DrawPermissions(preview);
                }
            }

            DrawAdvanced();
            serializedObject.ApplyModifiedProperties();
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 20 };
            cardStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 10, 10) };
            stateStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        }

        private void DrawPreviewActions()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || bIsPreviewQueued))
            {
                if (GUILayout.Button(EditorText.Get("unity.preview.refresh")))
                {
                    bIsPreviewQueued = true;
                    EditorApplication.delayCall += RefreshPreview;
                }
            }

            if (!string.IsNullOrEmpty(previewError))
            {
                EditorGUILayout.HelpBox(previewError, MessageType.Error);
            }

            if (bIsPreviewStale)
            {
                EditorGUILayout.HelpBox(EditorText.Get("unity.preview.stale"), MessageType.Info);
            }
        }

        private void DrawHeader(VRCAvatarDescriptor descriptor)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("RemoteOSC", titleStyle, GUILayout.Height(28));
            GUILayout.Label(EditorText.Get("unity.permissions.description"), EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(cardStyle))
            {
                if (descriptor == null)
                {
                    EditorGUILayout.HelpBox(EditorText.Get("unity.avatar.descriptor_required"), MessageType.Error);
                    return;
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(EditorText.Get("common.avatar"), descriptor.gameObject, typeof(GameObject), true);
                }

                PipelineManager pipeline = descriptor.GetComponent<PipelineManager>();
                bool bHasId = pipeline != null && !string.IsNullOrEmpty(pipeline.blueprintId);
                EditorGUILayout.LabelField(bHasId ? EditorText.Get("unity.export.ready") : EditorText.Get("unity.export.waiting_for_id"), EditorStyles.boldLabel);
                if (bHasId)
                {
                    EditorGUILayout.SelectableLabel(pipeline.blueprintId, EditorStyles.textField,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
                else
                {
                    GUILayout.Label(EditorText.Get("unity.export.upload_hint"), EditorStyles.wordWrappedMiniLabel);
                }

                if (descriptor.GetComponentsInChildren<RemoteOscAvatar>(true).Length > 1)
                {
                    EditorGUILayout.HelpBox(EditorText.Get("unity.export.multiple_exporters"), MessageType.Error);
                }
            }
        }

        private void DrawOutput(VRCAvatarDescriptor descriptor)
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(cardStyle))
            {
                EditorGUILayout.LabelField(EditorText.Get("unity.export.location"), EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(outputDirectory, new GUIContent("", EditorText.Get("unity.export.directory_tooltip")));
                    if (GUILayout.Button(EditorText.Get("common.browse"), GUILayout.MinWidth(72)))
                    {
                        string initial = string.IsNullOrWhiteSpace(outputDirectory.stringValue)
                            ? ClientAvatarDirectory.Find() : ResolveDirectory(outputDirectory.stringValue);
                        string chosen = EditorUtility.OpenFolderPanel(EditorText.Get("unity.export.folder_dialog"), initial ?? Application.dataPath, "");
                        if (!string.IsNullOrEmpty(chosen))
                        {
                            string relative = FileUtil.GetProjectRelativePath(chosen);
                            outputDirectory.stringValue = string.IsNullOrEmpty(relative) ? chosen : relative;
                        }
                    }
                }

                GUILayout.Label(EditorText.Get("unity.export.directory_hint"), EditorStyles.wordWrappedMiniLabel);
                string directory = ResolveDirectory(outputDirectory.stringValue);
                if (directory == null)
                {
                    EditorGUILayout.HelpBox(EditorText.Get("folder.path_invalid"), MessageType.Warning);
                    return;
                }

                PipelineManager pipeline = descriptor == null ? null : descriptor.GetComponent<PipelineManager>();
                string filename = pipeline != null && !string.IsNullOrEmpty(pipeline.blueprintId)
                    ? pipeline.blueprintId + ".yaml" : "<avatar ID>.yaml";
                EditorGUILayout.Space(4);
                GUILayout.Label(new GUIContent(Path.Combine(directory, filename), EditorText.Get("unity.export.filename_hint")),
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(!Directory.Exists(directory)))
                {
                    if (GUILayout.Button(EditorText.Get("folder.show"), EditorStyles.miniButton))
                    {
                        EditorUtility.RevealInFinder(directory);
                    }
                }
            }
        }

        private void DrawPermissions(AvatarPermissionPreview preview)
        {
            if (bNeedsInitialTab)
            {
                selectedTab = preview.Controls.Count > 0 ? 0 : 1;
                bNeedsInitialTab = false;
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(cardStyle))
            {
                EditorGUILayout.LabelField(EditorText.Get("unity.permissions.title"), EditorStyles.boldLabel);
                int shared = preview.Parameters.Count(row => row.bIsAllowed);
                int protectedCount = preview.Parameters.Count(row => row.bIsProtected);
                GUILayout.Label(EditorText.Get("unity.permissions.summary", shared, preview.Parameters.Count - shared - protectedCount, protectedCount), EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label(buildPreview == null
                    ? EditorText.Get("unity.preview.source")
                    : EditorText.Get("unity.preview.generated"),
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(6);
                int nextTab = GUILayout.Toolbar(selectedTab, new[] { EditorText.Get("unity.permissions.controls_count", preview.Controls.Count), EditorText.Get("unity.permissions.parameters_count", preview.Parameters.Count) });
                if (nextTab != selectedTab)
                {
                    selectedTab = nextTab;
                    scrollPosition = Vector2.zero;
                    GUI.FocusControl(null);
                }

                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    search = searchField.OnGUI(search);
                    selectedFilter = EditorGUILayout.Popup(selectedFilter, Filters, GUILayout.MinWidth(80));
                }

                List<AvatarPermissionPreview.Row> allRows = selectedTab == 0 ? preview.Controls : preview.Parameters;
                List<AvatarPermissionPreview.Row> rows = allRows.Where(row => row.Matches(search, selectedFilter)).ToList();
                SerializedProperty blocked = selectedTab == 0 ? blockedMenuPaths : blockedParameters;
                DrawBulkActions(rows, blocked);
                if (allRows.Count == 0)
                {
                    EditorGUILayout.HelpBox(selectedTab == 0
                        ? EditorText.Get("unity.permissions.no_source_controls")
                        : EditorText.Get("unity.permissions.no_source_parameters"), MessageType.Info);
                    return;
                }

                if (rows.Count == 0)
                {
                    EditorGUILayout.HelpBox(EditorText.Get("unity.permissions.no_matches"), MessageType.Info);
                    return;
                }

                int visibleRows = selectedTab == 0
                    ? rows.Count(row => !collapsedMenus.Contains(row.Group) || !string.IsNullOrWhiteSpace(search))
                    : rows.Count;
                int groupCount = selectedTab == 0 ? rows.Select(row => row.Group).Distinct().Count() : 0;
                float height = Mathf.Min(420, visibleRows * 40 + groupCount * (EditorGUIUtility.singleLineHeight + 2) + 6);
                using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition, GUILayout.Height(height)))
                {
                    scrollPosition = scroll.scrollPosition;
                    if (selectedTab == 0)
                    {
                        DrawMenuGroups(rows, blocked);
                    }
                    else
                    {
                        foreach (AvatarPermissionPreview.Row row in rows)
                        {
                            DrawPermission(row, blocked);
                        }
                    }
                }
            }
        }

        private void DrawBulkActions(List<AvatarPermissionPreview.Row> rows, SerializedProperty blocked)
        {
            var allowLabel = new GUIContent(EditorText.Get("unity.permissions.allow_shown"));
            var blockLabel = new GUIContent(EditorText.Get("unity.permissions.block_shown"));
            float buttonWidth = Mathf.Ceil(Mathf.Max(
                EditorStyles.miniButtonLeft.CalcSize(allowLabel).x,
                EditorStyles.miniButtonRight.CalcSize(blockLabel).x)) + 16;
            float buttonHeight = EditorGUIUtility.singleLineHeight + 4;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(EditorText.Get("unity.permissions.shown_count", rows.Count), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!rows.Any(row => row.bCanEdit && !row.bIsAllowed)))
                {
                    if (GUILayout.Button(allowLabel, EditorStyles.miniButtonLeft, GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                    {
                        foreach (AvatarPermissionPreview.Row row in rows.Where(row => row.bCanEdit))
                        {
                            ApplyPermission(blocked, row, true);
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(!rows.Any(row => row.bCanEdit && row.bIsAllowed)))
                {
                    if (GUILayout.Button(blockLabel, EditorStyles.miniButtonRight, GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                    {
                        foreach (AvatarPermissionPreview.Row row in rows.Where(row => row.bCanEdit))
                        {
                            ApplyPermission(blocked, row, false);
                        }
                    }
                }
            }
        }

        private void DrawMenuGroups(List<AvatarPermissionPreview.Row> rows, SerializedProperty blocked)
        {
            foreach (IGrouping<string, AvatarPermissionPreview.Row> group in rows.GroupBy(row => row.Group))
            {
                string label = EditorText.Get("unity.menu.group_count", string.IsNullOrEmpty(group.Key) ? EditorText.Get("unity.menu.root") : group.Key, group.Count());
                bool bIsExpanded = !collapsedMenus.Contains(group.Key) || !string.IsNullOrWhiteSpace(search);
                bool bShouldExpand = EditorGUILayout.Foldout(bIsExpanded, label, true);
                if (bShouldExpand != bIsExpanded)
                {
                    if (bShouldExpand)
                    {
                        collapsedMenus.Remove(group.Key);
                    }
                    else
                    {
                        collapsedMenus.Add(group.Key);
                    }
                }

                if (!bShouldExpand)
                {
                    continue;
                }

                foreach (AvatarPermissionPreview.Row row in group)
                {
                    DrawPermission(row, blocked);
                }
            }
        }

        private void DrawPermission(AvatarPermissionPreview.Row row, SerializedProperty blocked)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 38);
            string tooltip = row.Key + "\n" + row.Details + (string.IsNullOrEmpty(row.Reason) ? "" : "\n" + row.Reason);
            var toggleRect = new Rect(rect.x + 4, rect.y + 2, Mathf.Max(0, rect.width - 78), EditorGUIUtility.singleLineHeight);
            using (new EditorGUI.DisabledScope(!row.bCanEdit))
            {
                bool bShouldAllow = EditorGUI.ToggleLeft(toggleRect, new GUIContent(row.Label, tooltip), row.bIsAllowed);
                if (bShouldAllow != row.bIsAllowed)
                {
                    ApplyPermission(blocked, row, bShouldAllow);
                }
            }

            string state = row.bIsProtected ? EditorText.Get("permissions.protected") : row.bIsAllowed ? EditorText.Get("permissions.shared") : EditorText.Get("permissions.blocked");
            stateStyle.normal.textColor = row.bIsAllowed
                ? (EditorGUIUtility.isProSkin ? new Color(0.55f, 0.85f, 0.68f) : new Color(0.12f, 0.40f, 0.23f))
                : EditorStyles.miniLabel.normal.textColor;
            GUI.Label(new Rect(rect.xMax - 72, rect.y + 2, 68, EditorGUIUtility.singleLineHeight), new GUIContent(state, tooltip), stateStyle);
            GUI.Label(new Rect(rect.x + 22, rect.y + 19, Mathf.Max(0, rect.width - 26), 16),
                new GUIContent(string.IsNullOrEmpty(row.Reason) ? row.Details : row.Reason, tooltip), EditorStyles.miniLabel);
        }

        private void DrawAdvanced()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(cardStyle))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    bShowAdvanced = EditorGUILayout.Foldout(bShowAdvanced, EditorText.Get("unity.exclusions.advanced"), true);
                }

                if (!bShowAdvanced)
                {
                    return;
                }

                GUILayout.Label(EditorText.Get("unity.exclusions.description"),
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(blockedParameters, new GUIContent(EditorText.Get("unity.exclusions.parameter_names"),
                        EditorText.Get("unity.exclusions.parameter_hint")), true);
                    EditorGUILayout.PropertyField(blockedMenuPaths, new GUIContent(EditorText.Get("unity.exclusions.menu_paths"),
                        EditorText.Get("unity.exclusions.menu_hint")), true);
                }
            }
        }

        internal static void SetAllowed(SerializedProperty blocked, string name, bool bShouldAllow)
        {
            bool bHasEntry = false;
            for (int index = blocked.arraySize - 1; index >= 0; index--)
            {
                if (blocked.GetArrayElementAtIndex(index).stringValue != name)
                {
                    continue;
                }

                bHasEntry = true;
                if (bShouldAllow)
                {
                    blocked.DeleteArrayElementAtIndex(index);
                }
            }

            if (!bShouldAllow && !bHasEntry)
            {
                int index = blocked.arraySize;
                blocked.InsertArrayElementAtIndex(index);
                blocked.GetArrayElementAtIndex(index).stringValue = name;
            }
        }

        private void ApplyPermission(SerializedProperty blocked, AvatarPermissionPreview.Row row, bool bShouldAllow)
        {
            if (blocked != blockedParameters)
            {
                SetAllowed(blocked, row.Key, bShouldAllow);
                SetAllowed(blockedGeneratedMenuPaths, row.Key, bShouldAllow || buildPreview == null);
                return;
            }

            foreach (string key in row.BlockedKeys)
            {
                SetAllowed(blocked, key, true);
                for (int index = blockedParameterBindings.arraySize - 1; index >= 0; index--)
                {
                    if (blockedParameterBindings.GetArrayElementAtIndex(index).FindPropertyRelative("Name").stringValue == key)
                    {
                        blockedParameterBindings.DeleteArrayElementAtIndex(index);
                    }
                }
            }

            if (bShouldAllow)
            {
                return;
            }

            SetAllowed(blocked, row.Key, false);
            if (buildPreview == null || row.MenuPaths.Length == 0)
            {
                return;
            }

            int bindingIndex = blockedParameterBindings.arraySize;
            blockedParameterBindings.InsertArrayElementAtIndex(bindingIndex);
            SerializedProperty binding = blockedParameterBindings.GetArrayElementAtIndex(bindingIndex);
            binding.FindPropertyRelative("Name").stringValue = row.Key;
            SerializedProperty paths = binding.FindPropertyRelative("MenuPaths");
            paths.arraySize = row.MenuPaths.Length;
            for (int index = 0; index < row.MenuPaths.Length; index++)
            {
                paths.GetArrayElementAtIndex(index).stringValue = row.MenuPaths[index];
            }
        }

        private static IEnumerable<string> ReadStrings(SerializedProperty property)
        {
            for (int index = 0; index < property.arraySize; index++)
            {
                yield return property.GetArrayElementAtIndex(index).stringValue;
            }
        }

        private static string ResolveDirectory(string value)
        {
            try
            {
                string project = Path.GetDirectoryName(Application.dataPath);
                string directory = string.IsNullOrWhiteSpace(value) ? Application.dataPath : value;
                return Path.GetFullPath(Path.IsPathRooted(directory) ? directory : Path.Combine(project, directory));
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }
}
