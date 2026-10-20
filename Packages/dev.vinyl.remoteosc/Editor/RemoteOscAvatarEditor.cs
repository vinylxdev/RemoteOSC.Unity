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
        private static string[] Filters => new[] { EditorText.Get("All"), EditorText.Get("Shared"), EditorText.Get("Blocked") };
        private readonly HashSet<string> collapsedMenus = new HashSet<string>(StringComparer.Ordinal);
        private SerializedProperty outputDirectory;
        private SerializedProperty blockedParameters;
        private SerializedProperty blockedMenuPaths;
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
            searchField = new SearchField();
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
                var preview = new AvatarPermissionPreview(
                    AvatarConfigExporter.ReadParameters(descriptor.gameObject, descriptor),
                    AvatarConfigExporter.ReadControls(descriptor.expressionsMenu),
                    ReadStrings(blockedParameters), ReadStrings(blockedMenuPaths));
                DrawPermissions(preview);
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

        private void DrawHeader(VRCAvatarDescriptor descriptor)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("RemoteOSC", titleStyle, GUILayout.Height(28));
            GUILayout.Label(EditorText.Get("Choose which avatar controls friends can use."), EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(cardStyle))
            {
                if (descriptor == null)
                {
                    EditorGUILayout.HelpBox(EditorText.Get("Add this component to an avatar with a VRCAvatarDescriptor."), MessageType.Error);
                    return;
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(EditorText.Get("Avatar"), descriptor.gameObject, typeof(GameObject), true);
                }

                PipelineManager pipeline = descriptor.GetComponent<PipelineManager>();
                bool bHasId = pipeline != null && !string.IsNullOrEmpty(pipeline.blueprintId);
                EditorGUILayout.LabelField(bHasId ? EditorText.Get("Ready to export on build") : EditorText.Get("Waiting for an avatar ID"), EditorStyles.boldLabel);
                if (bHasId)
                {
                    EditorGUILayout.SelectableLabel(pipeline.blueprintId, EditorStyles.textField,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
                else
                {
                    GUILayout.Label(EditorText.Get("Upload the avatar once, then build again to export its config."), EditorStyles.wordWrappedMiniLabel);
                }

                if (descriptor.GetComponentsInChildren<RemoteOscAvatar>(true).Length > 1)
                {
                    EditorGUILayout.HelpBox(EditorText.Get("This avatar has multiple RemoteOSC exporters. Keep one component so its build can export."), MessageType.Error);
                }
            }
        }

        private void DrawOutput(VRCAvatarDescriptor descriptor)
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(cardStyle))
            {
                EditorGUILayout.LabelField(EditorText.Get("Export location"), EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(outputDirectory, new GUIContent("", EditorText.Get("Folder used by the client for avatar configs. Empty writes to Assets/.")));
                    if (GUILayout.Button(EditorText.Get("Browse…"), GUILayout.MinWidth(72)))
                    {
                        string initial = string.IsNullOrWhiteSpace(outputDirectory.stringValue)
                            ? ClientAvatarDirectory.Find() : ResolveDirectory(outputDirectory.stringValue);
                        string chosen = EditorUtility.OpenFolderPanel(EditorText.Get("RemoteOSC config folder"), initial ?? Application.dataPath, "");
                        if (!string.IsNullOrEmpty(chosen))
                        {
                            string relative = FileUtil.GetProjectRelativePath(chosen);
                            outputDirectory.stringValue = string.IsNullOrEmpty(relative) ? chosen : relative;
                        }
                    }
                }

                GUILayout.Label(EditorText.Get("Empty exports to Assets. Browse starts in the client's avatar folder when it can be found."), EditorStyles.wordWrappedMiniLabel);
                string directory = ResolveDirectory(outputDirectory.stringValue);
                if (directory == null)
                {
                    EditorGUILayout.HelpBox(EditorText.Get("Enter a valid folder path."), MessageType.Warning);
                    return;
                }

                PipelineManager pipeline = descriptor == null ? null : descriptor.GetComponent<PipelineManager>();
                string filename = pipeline != null && !string.IsNullOrEmpty(pipeline.blueprintId)
                    ? pipeline.blueprintId + ".yaml" : "<avatar ID>.yaml";
                EditorGUILayout.Space(4);
                GUILayout.Label(new GUIContent(Path.Combine(directory, filename), EditorText.Get("The config is written here during the avatar build.")),
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(!Directory.Exists(directory)))
                {
                    if (GUILayout.Button(EditorText.Get("Show folder"), EditorStyles.miniButton))
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
                EditorGUILayout.LabelField(EditorText.Get("Remote permissions"), EditorStyles.boldLabel);
                int shared = preview.Parameters.Count(row => row.bIsAllowed);
                int protectedCount = preview.Parameters.Count(row => row.bIsProtected);
                GUILayout.Label(EditorText.Get("{0} shared  ·  {1} blocked  ·  {2} protected", shared, preview.Parameters.Count - shared - protectedCount, protectedCount), EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label(EditorText.Get("Source preview. Final parameter names and menus are exported after the avatar build tools run."),
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(6);
                int nextTab = GUILayout.Toolbar(selectedTab, new[] { EditorText.Get("Menu controls ({0})", preview.Controls.Count), EditorText.Get("Parameters ({0})", preview.Parameters.Count) });
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
                var rows = allRows.Where(row => row.Matches(search, selectedFilter)).ToList();
                SerializedProperty blocked = selectedTab == 0 ? blockedMenuPaths : blockedParameters;
                DrawBulkActions(rows, blocked);
                if (allRows.Count == 0)
                {
                    EditorGUILayout.HelpBox(selectedTab == 0
                        ? EditorText.Get("No source menu controls found. Choose Parameters, or add generated menu paths under Advanced exclusions.")
                        : EditorText.Get("No source parameters found. Assign expression parameters or an animator controller to preview them here."), MessageType.Info);
                    return;
                }

                if (rows.Count == 0)
                {
                    EditorGUILayout.HelpBox(EditorText.Get("No matches. Change the search or permission filter."), MessageType.Info);
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

        private static void DrawBulkActions(List<AvatarPermissionPreview.Row> rows, SerializedProperty blocked)
        {
            var allowLabel = new GUIContent(EditorText.Get("Allow shown"));
            var blockLabel = new GUIContent(EditorText.Get("Block shown"));
            float buttonWidth = Mathf.Ceil(Mathf.Max(
                EditorStyles.miniButtonLeft.CalcSize(allowLabel).x,
                EditorStyles.miniButtonRight.CalcSize(blockLabel).x)) + 16;
            float buttonHeight = EditorGUIUtility.singleLineHeight + 4;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(EditorText.Get("{0} shown", rows.Count), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!rows.Any(row => row.bCanEdit && !row.bIsAllowed)))
                {
                    if (GUILayout.Button(allowLabel, EditorStyles.miniButtonLeft, GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                    {
                        foreach (AvatarPermissionPreview.Row row in rows.Where(row => row.bCanEdit))
                        {
                            SetAllowed(blocked, row.Key, true);
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(!rows.Any(row => row.bCanEdit && row.bIsAllowed)))
                {
                    if (GUILayout.Button(blockLabel, EditorStyles.miniButtonRight, GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                    {
                        foreach (AvatarPermissionPreview.Row row in rows.Where(row => row.bCanEdit))
                        {
                            SetAllowed(blocked, row.Key, false);
                        }
                    }
                }
            }
        }

        private void DrawMenuGroups(List<AvatarPermissionPreview.Row> rows, SerializedProperty blocked)
        {
            foreach (IGrouping<string, AvatarPermissionPreview.Row> group in rows.GroupBy(row => row.Group))
            {
                string label = EditorText.Get("{0}  ({1})", string.IsNullOrEmpty(group.Key) ? EditorText.Get("Root menu") : group.Key, group.Count());
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
                    SetAllowed(blocked, row.Key, bShouldAllow);
                }
            }

            string state = row.bIsProtected ? EditorText.Get("Protected") : row.bIsAllowed ? EditorText.Get("Shared") : EditorText.Get("Blocked");
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
                    bShowAdvanced = EditorGUILayout.Foldout(bShowAdvanced, EditorText.Get("Advanced exclusions"), true);
                }

                if (!bShowAdvanced)
                {
                    return;
                }

                GUILayout.Label(EditorText.Get("Use exact names or stable menu paths for controls generated during the build. A menu path also excludes its child controls."),
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(blockedParameters, new GUIContent(EditorText.Get("Parameter names"),
                        EditorText.Get("Exact parameter names that must never be shared with peers.")), true);
                    EditorGUILayout.PropertyField(blockedMenuPaths, new GUIContent(EditorText.Get("Menu paths"),
                        EditorText.Get("Menu/control paths to exclude, such as Clothing/Hat. These survive parameter renaming.")), true);
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
