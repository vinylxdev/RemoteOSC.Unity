// Vinyl - vinyl.dev

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;

namespace RemoteOSC.Unity.Editor.Tests
{
    // Optional integration fixture. Install VRCFury and Modular Avatar in the isolated project.
    public static class AvatarGeneratedToolsRegression
    {
        public static void Run()
        {
            try
            {
                RunCase();
                RunInspectorLifecycle();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void RunInspectorLifecycle()
        {
            var root = new GameObject("Preview lifecycle regression");
            root.AddComponent<Animator>();
            VRCAvatarDescriptor descriptor = root.AddComponent<VRCAvatarDescriptor>();
            descriptor.baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            RemoteOscAvatar component = root.AddComponent<RemoteOscAvatar>();
            var prop = new GameObject("Generated toggle target");
            prop.transform.SetParent(root.transform, false);
            object toggle = FindType("com.vrcfury.api.FuryComponents").GetMethod("CreateToggle").Invoke(null, new object[] { root });
            toggle.GetType().GetMethod("SetMenuPath").Invoke(toggle, new object[] { "Fury/Lifecycle toggle" });
            object actions = toggle.GetType().GetMethod("GetActions").Invoke(toggle, null);
            actions.GetType().GetMethod("AddTurnOn").Invoke(actions, new object[] { prop });
            prop.AddComponent(FindType("nadena.dev.modular_avatar.core.ModularAvatarParameters"));
            var editor = (RemoteOscAvatarEditor)UnityEditor.Editor.CreateEditor(component);
            GameObject change = null;
            Action cleanup = () =>
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(change);
            };
            try
            {
                typeof(RemoteOscAvatarEditor).GetMethod("RefreshPreview", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);
                // Let both the preview's change notifications and its completion callback run.
                EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
                {
                    try
                    {
                        Check(typeof(RemoteOscAvatarEditor).GetField("buildPreview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor) != null,
                            "Inspector did not generate a preview.");
                        FieldInfo staleField = typeof(RemoteOscAvatarEditor).GetField("bIsPreviewStale", BindingFlags.Instance | BindingFlags.NonPublic);
                        Check(!(bool)staleField.GetValue(editor), "Preview invalidated itself after generation.");
                        editor.serializedObject.Update();
                        SerializedProperty blocked = editor.serializedObject.FindProperty("BlockedParameterNames");
                        blocked.arraySize = 1;
                        blocked.GetArrayElementAtIndex(0).stringValue = "Example";
                        editor.serializedObject.ApplyModifiedProperties();
                        typeof(RemoteOscAvatarEditor).GetMethod("InvalidatePreview", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);
                        Check(!(bool)staleField.GetValue(editor), "Permission edits invalidated the generated preview.");
                        change = new GameObject("Source hierarchy change");
                        change.transform.SetParent(root.transform, false);
                        EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
                        {
                            try
                            {
                                Check((bool)staleField.GetValue(editor), "Source changes did not invalidate the preview.");
                                cleanup();
                                Debug.Log("PASS generated preview discovers VRCFury and Modular Avatar controls, preserves exclusions and source assets, and invalidates only after source edits.");
                                EditorApplication.Exit(0);
                            }
                            catch (Exception error)
                            {
                                cleanup();
                                Debug.LogException(error);
                                EditorApplication.Exit(1);
                            }
                        };
                    }
                    catch (Exception error)
                    {
                        cleanup();
                        Debug.LogException(error);
                        EditorApplication.Exit(1);
                    }
                };
            }
            catch
            {
                cleanup();
                throw;
            }
        }

        private static Type FindType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name))
                .FirstOrDefault(type => type != null) ?? throw new InvalidOperationException("Install the build tool containing " + name);
        }

        private static void RunCase()
        {
            string directory = Path.Combine(Path.GetTempPath(), "remoteosc-tools-" + Guid.NewGuid().ToString("N"));
            var root = new GameObject("Generated tools regression");
            VRCExpressionsMenu menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            try
            {
                root.AddComponent<Animator>();
                VRCAvatarDescriptor descriptor = root.AddComponent<VRCAvatarDescriptor>();
                PipelineManager pipeline = root.GetComponent<PipelineManager>() ?? root.AddComponent<PipelineManager>();
                pipeline.blueprintId = "avtr_tools_regression";
                descriptor.baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.customExpressions = true;
                RemoteOscAvatar component = root.AddComponent<RemoteOscAvatar>();
                component.OutputDirectory = directory;
                var prop = new GameObject("Toggle target");
                prop.transform.SetParent(root.transform, false);
                prop.SetActive(false);
                object toggle = FindType("com.vrcfury.api.FuryComponents").GetMethod("CreateToggle").Invoke(null, new object[] { root });
                toggle.GetType().GetMethod("SetMenuPath").Invoke(toggle, new object[] { "Fury/Generated toggle" });
                object actions = toggle.GetType().GetMethod("GetActions").Invoke(toggle, null);
                actions.GetType().GetMethod("AddTurnOn").Invoke(actions, new object[] { prop });

                var accessory = new GameObject("MA accessory");
                accessory.transform.SetParent(root.transform, false);
                Component installer = accessory.AddComponent(FindType("nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller"));
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "MA generated toggle",
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = "Accessory" },
                    value = 1,
                });
                installer.GetType().GetField("menuToAppend").SetValue(installer, menu);
                Component maParameters = accessory.AddComponent(FindType("nadena.dev.modular_avatar.core.ModularAvatarParameters"));
                var serialized = new SerializedObject(maParameters);
                SerializedProperty entries = serialized.FindProperty("parameters");
                entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("nameOrPrefix").stringValue = "Accessory";
                entry.FindPropertyRelative("internalParameter").boolValue = true;
                SerializedProperty syncType = entry.FindPropertyRelative("syncType");
                syncType.enumValueIndex = Array.IndexOf(syncType.enumNames, "Bool");
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Check(AvatarConfigExporter.ReadControls(descriptor.expressionsMenu).Count == 0, "Fixture already has source menu controls.");
                AvatarBuildPreview preview = AvatarBuildPreview.Create(root);
                AvatarConfigExporter.ExportedControl furyControl = preview.Controls.Single(control => control.Path == "Fury/Generated toggle");
                AvatarConfigExporter.ExportedControl maControl = preview.Controls.Single(control => control.Path == "MA generated toggle");
                Check(preview.Parameters.Any(parameter => parameter.Name == furyControl.Parameter), "VRCFury parameter is missing.");
                Check(preview.Parameters.Any(parameter => parameter.Name == maControl.Parameter), "MA parameter is missing.");
                Check(descriptor.expressionsMenu == null && descriptor.expressionParameters == null, "Source descriptor was modified.");
                Check(menu.controls.Single().parameter.name == "Accessory", "Source menu asset was modified.");
                Check(!prop.activeSelf && root.GetComponents<Component>().Any(item => item != null && item.GetType().FullName == "VF.Model.VRCFury"),
                    "Source components were modified.");
                Check(!Directory.Exists(directory), "Preview wrote an export.");
                Check(AvatarBuildCapture.Snapshots.Count == 0, "Preview retained an export snapshot.");

                // Exercise the actual inspector serialization, including allowing a renamed rule again.
                var editor = (RemoteOscAvatarEditor)UnityEditor.Editor.CreateEditor(component);
                try
                {
                    FieldInfo field = typeof(RemoteOscAvatarEditor).GetField("buildPreview", BindingFlags.Instance | BindingFlags.NonPublic);
                    field.SetValue(editor, preview);
                    FieldInfo blockedField = typeof(RemoteOscAvatarEditor).GetField("blockedParameters", BindingFlags.Instance | BindingFlags.NonPublic);
                    var blocked = (SerializedProperty)blockedField.GetValue(editor);
                    MethodInfo apply = typeof(RemoteOscAvatarEditor).GetMethod("ApplyPermission", BindingFlags.Instance | BindingFlags.NonPublic);
                    var permissions = new AvatarPermissionPreview(
                        preview.Parameters,
                        preview.Controls,
                        component.BlockedParameterNames,
                        component.BlockedMenuPaths);
                    AvatarPermissionPreview.Row row = permissions.Parameters.Single(item => item.Key == furyControl.Parameter);
                    apply.Invoke(editor, new object[] { blocked, row, false });
                    editor.serializedObject.ApplyModifiedProperties();
                    Check(
                        component.BlockedParameterBindings.Single().MenuPaths.Contains(furyControl.Path),
                        "Inspector did not save the generated binding.");
                    apply.Invoke(editor, new object[] { blocked, row, true });
                    editor.serializedObject.ApplyModifiedProperties();
                    Check(
                        component.BlockedParameterNames.Count == 0 && component.BlockedParameterBindings.Count == 0,
                        "Allowing did not clear the generated exclusion.");
                    FieldInfo menuField = typeof(RemoteOscAvatarEditor).GetField("blockedMenuPaths", BindingFlags.Instance | BindingFlags.NonPublic);
                    var blockedMenus = (SerializedProperty)menuField.GetValue(editor);
                    AvatarPermissionPreview.Row menuRow = permissions.Controls.Single(item => item.Key == furyControl.Path);
                    apply.Invoke(editor, new object[] { blockedMenus, menuRow, false });
                    editor.serializedObject.ApplyModifiedProperties();
                    Check(component.BlockedGeneratedMenuPaths.Single() == furyControl.Path, "Inspector did not retain the generated menu exclusion.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(editor);
                }

                component.BlockedParameterNames.Add(maControl.Parameter);
                component.BlockedParameterBindings.Add(new RemoteOscAvatar.ParameterMenuBinding
                {
                    Name = maControl.Parameter,
                    MenuPaths = new System.Collections.Generic.List<string> { maControl.Path },
                });
                GameObject built = UnityEngine.Object.Instantiate(root);
                try
                {
                    Check(VRCBuildPipelineCallbacks.OnPreprocessAvatar(built), "Actual SDK build failed.");
                    string yaml = File.ReadAllText(Path.Combine(directory, pipeline.blueprintId + ".yaml"));
                    AvatarConfigExporter.ExportedControl[] finalControls = AvatarConfigExporter.ReadControls(built.GetComponent<VRCAvatarDescriptor>().expressionsMenu)
                        .Where(control => control.Path == furyControl.Path || control.Path == maControl.Path).ToArray();
                    Check(finalControls.Length == 2, "Generated controls are missing from the actual build.");
                    foreach (AvatarConfigExporter.ExportedControl control in finalControls)
                    {
                        Check(!AvatarExportRegression.IsAllowed(yaml, control.Parameter), "Generated exclusions were lost during the real build.");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(built);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(menu);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void Check(bool bIsSuccessful, string message)
        {
            if (!bIsSuccessful)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
