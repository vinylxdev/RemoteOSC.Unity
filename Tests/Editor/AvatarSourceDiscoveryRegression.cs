// Vinyl - vinyl.dev

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;

namespace RemoteOSC.Unity.Editor.Tests
{
    public static class AvatarSourceDiscoveryRegression
    {
        public static void Run()
        {
            try
            {
                RunControllerCase();
                RunComponentCase();
                Debug.Log("PASS component inspection discovers scoped parameters without processing; source exclusions survive renaming and resolve during the final build.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void RunControllerCase()
        {
            var root = new GameObject("Controller inspection regression");
            var controller = new AnimatorController();
            VRCExpressionsMenu menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            try
            {
                VRCAvatarDescriptor descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                controller.AddParameter("Local", AnimatorControllerParameterType.Int);
                controller.AddParameter("Global", AnimatorControllerParameterType.Float);
                controller.AddParameter("Protected", AnimatorControllerParameterType.Trigger);
                controller.AddParameter("Unbound", AnimatorControllerParameterType.Bool);
                Component fury = root.AddComponent(FindType("VF.Model.VRCFury"));
                object model = Activator.CreateInstance(FindType("VF.Model.Feature.FullController"));
                fury.GetType().GetField("content").SetValue(fury, model);
                object entry = Activator.CreateInstance(model.GetType().GetNestedType("ControllerEntry"));
                object wrapper = Activator.CreateInstance(FindType("VF.Model.GuidController"));
                wrapper.GetType().GetField("objRef").SetValue(wrapper, controller);
                entry.GetType().GetField("controller").SetValue(entry, wrapper);
                ((IList)model.GetType().GetField("controllers").GetValue(model)).Add(entry);
                ((IList)model.GetType().GetField("globalParams").GetValue(model)).Add("Global");
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Local control",
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = "Local" },
                });
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Local alias",
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = "Local" },
                });
                object menuEntry = Activator.CreateInstance(model.GetType().GetNestedType("MenuEntry"));
                object menuWrapper = Activator.CreateInstance(FindType("VF.Model.GuidMenu"));
                menuWrapper.GetType().GetField("objRef").SetValue(menuWrapper, menu);
                menuEntry.GetType().GetField("menu").SetValue(menuEntry, menuWrapper);
                menuEntry.GetType().GetField("prefix").SetValue(menuEntry, "Imported");
                ((IList)model.GetType().GetField("menus").GetValue(model)).Add(menuEntry);
                Component merge = root.AddComponent(FindType("nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator"));
                merge.GetType().GetField("animator").SetValue(merge, controller);
                AddToggle(root, "Shared slider");
                Component slider = root.GetComponents<Component>().Last(component => component.GetType().FullName == "VF.Model.VRCFury");
                object sliderModel = slider.GetType().GetField("content").GetValue(slider);
                sliderModel.GetType().GetField("slider").SetValue(sliderModel, true);
                sliderModel.GetType().GetField("useGlobalParam").SetValue(sliderModel, true);
                sliderModel.GetType().GetField("globalParam").SetValue(sliderModel, "ExplicitSlider");

                AvatarSourceDiscovery discovered = AvatarSourceDiscovery.Read(root, descriptor);
                Check(discovered.Warnings.Count == 0, string.Join("\n", discovered.Warnings));
                AvatarConfigExporter.ExportedControl control = discovered.Controls.Single(item => item.Path == "Imported/Local control");
                Check(
                    discovered.Parameters.Single(item => item.Name == control.Parameter).Type == "Int",
                    "Full Controller parameter types were not inspected.");
                Check(
                    discovered.Parameters.Any(item => item.Name == "Global" && !item.bIsGenerated),
                    "An explicit global parameter was treated as generated.");
                Check(
                    discovered.Parameters.Any(item => item.DisplayName == "Local" && item.bIsGenerated),
                    "The local imported parameter was not scoped.");
                Check(
                    discovered.Parameters.Any(item => item.Name == "Local" && !item.bIsGenerated),
                    "MA Merge Animator parameters were not inspected.");
                Check(discovered.Parameters.Where(item => item.DisplayName == "Protected").All(item => !AvatarConfigExporter.CanControl(item)),
                    "Controller inspection made a trigger controllable.");
                Check(discovered.Parameters.Any(item => item.Name == "ExplicitSlider" && item.Type == "Float" && !item.bIsGenerated),
                    "A VRCFury global slider was not discovered with its exact type and name.");
                var permissions = new AvatarPermissionPreview(
                    discovered.Parameters,
                    discovered.Controls,
                    Array.Empty<string>(),
                    Array.Empty<string>());
                Check(permissions.Parameters.Single(row => row.Label == "Unbound" && row.Source != null).bIsAllowed
                    && !permissions.Parameters.Single(row => row.Label == "Unbound" && row.Source != null).bCanEdit,
                    "An unbound generated parameter was editable using a guessed final name.");
                var deniedControl = new RemoteOscAvatar.SourceExclusion { Source = control.Source, Key = control.SourceKey };
                var denied = new AvatarPermissionPreview(discovered.Parameters, discovered.Controls, Array.Empty<string>(), Array.Empty<string>(),
                    blockedSources: new[] { deniedControl });
                Check(!denied.Controls.Single(row => row.Key == control.Path).bIsAllowed && denied.Controls.Single(row => row.Key == control.Path).bCanEdit,
                    "A shared parameter alias made its source exclusion impossible to allow again.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(controller);
                UnityEngine.Object.DestroyImmediate(menu);
            }
        }

        private static void RunComponentCase()
        {
            string directory = Path.Combine(Path.GetTempPath(), "remoteosc-source-" + Guid.NewGuid().ToString("N"));
            var root = new GameObject("Source inspection regression");
            var menus = new List<VRCExpressionsMenu>();
            RemoteOscAvatarEditor editor = null;
            GameObject built = null;
            try
            {
                root.AddComponent<Animator>();
                VRCAvatarDescriptor descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.customExpressions = true;
                PipelineManager pipeline = root.GetComponent<PipelineManager>() ?? root.AddComponent<PipelineManager>();
                pipeline.blueprintId = "avtr_source_inspection_regression";
                RemoteOscAvatar exporter = root.AddComponent<RemoteOscAvatar>();
                exporter.OutputDirectory = directory;
                object privateToggle = AddToggle(root, "Fury/Private");
                AddToggle(root, "Fury/Public");
                AddModularMenu(root, menus, "MA private");
                AddModularMenu(root, menus, "MA public");
                AddModularMenuItem(root);
                string before = EditorJsonUtility.ToJson(root);
                Dictionary<Component, string> sourceModels = root.GetComponentsInChildren<Component>(true)
                    .Where(component => component != null && component != exporter).ToDictionary(component => component, EditorJsonUtility.ToJson);
                string[] generatedAssets = GeneratedAssets();
                AvatarSourceDiscovery source = AvatarSourceDiscovery.Read(root, descriptor);
                Check(source.Warnings.Count == 0, string.Join("\n", source.Warnings));
                Check(source.Controls.Count == 5, "Component inspection did not discover all five controls.");
                Check(source.Parameters.Count(item => item.DisplayName == "Accessory" && item.bIsGenerated) == 2,
                    "Identically named internal MA parameters were merged across scopes.");
                Check(
                    source.Parameters.Select(item => item.Name).Distinct().Count() == source.Parameters.Count,
                    "Source parameter identities collided.");

                editor = (RemoteOscAvatarEditor)UnityEditor.Editor.CreateEditor(exporter);
                exporter.BlockedParameterNames.Add("OldGeneratedName");
                exporter.BlockedParameterBindings.Add(new RemoteOscAvatar.ParameterMenuBinding
                {
                    Name = "OldGeneratedName",
                    MenuPaths = new List<string> { "Fury/Private" },
                });
                editor.serializedObject.Update();
                var permissions = (AvatarPermissionPreview)Invoke(editor, "GetPermissionPreview", descriptor);
                Check(GetField(editor, "buildPreview") == null, "Default discovery generated a build preview.");
                SerializedProperty blockedMenus = editor.serializedObject.FindProperty("BlockedMenuPaths");
                SerializedProperty blockedParameters = editor.serializedObject.FindProperty("BlockedParameterNames");
                string furyName = source.Controls.Single(control => control.Path == "Fury/Private").Parameter;
                AvatarPermissionPreview.Row legacy = permissions.Parameters.Single(row => row.Key == furyName);
                Check(!legacy.bIsAllowed && legacy.bCanEdit, "A legacy generated exclusion was lost in the component list.");
                Invoke(editor, "ApplyPermission", blockedParameters, legacy, true);
                editor.serializedObject.ApplyModifiedProperties();
                Check(exporter.BlockedParameterNames.Count == 0 && exporter.BlockedParameterBindings.Count == 0,
                    "Allowing a component parameter left its legacy generated exclusion behind.");
                permissions = (AvatarPermissionPreview)Invoke(editor, "GetPermissionPreview", descriptor);
                AvatarPermissionPreview.Row furyRow = permissions.Controls.Single(row => row.Key == "Fury/Private");
                string maName = source.Controls.Single(control => control.Path == "MA private").Parameter;
                AvatarPermissionPreview.Row maRow = permissions.Parameters.Single(row => row.Key == maName);
                AvatarPermissionPreview.Row itemRow = permissions.Controls.Single(row => row.Key == "Wardrobe/Generated outfit");
                Invoke(editor, "ApplyPermission", blockedMenus, furyRow, false);
                Invoke(editor, "ApplyPermission", blockedParameters, maRow, false);
                Invoke(editor, "ApplyPermission", blockedMenus, itemRow, false);
                editor.serializedObject.ApplyModifiedProperties();
                Check(exporter.BlockedSourceEntries.Count == 3 && exporter.BlockedParameterNames.Count == 0 && exporter.BlockedMenuPaths.Count == 0,
                    "The Inspector stored guessed final names instead of component exclusions.");
                Invoke(editor, "ApplyPermission", blockedMenus, furyRow, true);
                editor.serializedObject.ApplyModifiedProperties();
                Check(exporter.BlockedSourceEntries.Count == 2, "Allowing a component did not remove its exclusion.");
                Invoke(editor, "ApplyPermission", blockedMenus, furyRow, false);
                editor.serializedObject.ApplyModifiedProperties();
                Check(exporter.BlockedSourceEntries.Count == 3, "Blocking a component duplicated its exclusion.");
                Check(!Directory.Exists(directory) && AvatarBuildCapture.Snapshots.Count == 0 && GeneratedAssets().SequenceEqual(generatedAssets),
                    "Default inspection processed the avatar or generated assets.");
                Check(EditorJsonUtility.ToJson(root) == before && descriptor.expressionParameters == null && descriptor.expressionsMenu == null,
                    "Default inspection modified the source descriptor.");
                Check(sourceModels.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value),
                    "Default inspection modified a source component.");

                privateToggle.GetType().GetMethod("SetMenuPath").Invoke(privateToggle, new object[] { "Fury/Renamed private" });
                AvatarPermissionPreview.Row publicMenu = permissions.Controls.Single(row => row.Key == "MA public");
                Invoke(editor, "ApplyPermission", blockedMenus, publicMenu, false);
                editor.serializedObject.ApplyModifiedProperties();
                menus[1].controls[0].name = "MA renamed public";
                Invoke(editor, "InvalidatePreview");
                permissions = (AvatarPermissionPreview)Invoke(editor, "GetPermissionPreview", descriptor);
                Check(!permissions.Controls.Single(row => row.Key == "Fury/Renamed private").bIsAllowed,
                    "A renamed source toggle lost its exclusion.");
                publicMenu = permissions.Controls.Single(row => row.Key == "MA renamed public");
                Check(!publicMenu.bIsAllowed, "Renaming a source menu asset control lost its exclusion.");
                Invoke(editor, "ApplyPermission", blockedMenus, publicMenu, true);
                editor.serializedObject.ApplyModifiedProperties();
                Invoke(editor, "RefreshPreview");
                Check(GetField(editor, "buildPreview") != null, "Optional exact preview failed.");
                permissions = (AvatarPermissionPreview)Invoke(editor, "GetPermissionPreview", descriptor);
                Check(!permissions.Controls.Single(row => row.Key == "Fury/Renamed private").bIsAllowed
                    && !permissions.Controls.Single(row => row.Key == "MA private").bIsAllowed,
                    "Optional exact preview lost component exclusions.");
                Invoke(editor, "UseComponentPreview");
                Check(GetField(editor, "buildPreview") == null, "Returning to component inspection retained the generated view.");

                built = UnityEngine.Object.Instantiate(root);
                Check(built.GetComponent<RemoteOscAvatar>().BlockedSourceEntries.All(entry => entry.Source.transform.IsChildOf(built.transform)),
                    "Component exclusions were not remapped onto the build clone.");
                Check(VRCBuildPipelineCallbacks.OnPreprocessAvatar(built), "Actual SDK build failed.");
                string path = Path.Combine(directory, pipeline.blueprintId + ".yaml");
                string yaml = File.ReadAllText(path);
                List<AvatarConfigExporter.ExportedControl> finalControls = AvatarConfigExporter.ReadControls(built.GetComponent<VRCAvatarDescriptor>().expressionsMenu);
                foreach (string label in new[] { "Fury/Renamed private", "MA private", "Fury/Public", "MA renamed public", "Wardrobe/Generated outfit" })
                {
                    string parameter = finalControls.Single(control => control.Path == label).Parameter;
                    Check(AvatarExportRegression.IsAllowed(yaml, parameter) == (label == "Fury/Public" || label == "MA renamed public"),
                        "Final permissions did not match the component exclusion for " + label);
                }

                exporter.BlockedSourceEntries.Add(new RemoteOscAvatar.SourceExclusion { Key = "control:removed" });
                Invoke(editor, "InvalidatePreview");
                Invoke(editor, "GetPermissionPreview", descriptor);
                Check((bool)GetField(editor, "bHasUnresolvedSourceExclusions"), "The Inspector did not report a missing source exclusion.");
                Check(new AvatarBuildCapture().OnPreprocessAvatar(root), "Unresolved source capture failed.");
                AvatarSnapshot unresolved = AvatarBuildCapture.Snapshots[root.GetInstanceID()];
                Check(unresolved.bHasUnresolvedSourceBlock, "A missing source component silently removed an exclusion.");
                AvatarConfigExporter.Export(built, unresolved);
                yaml = File.ReadAllText(path);
                Check(AvatarConfigExporter.ReadParameters(built, built.GetComponent<VRCAvatarDescriptor>())
                    .All(parameter => !AvatarExportRegression.IsAllowed(yaml, parameter.Name)), "An unresolved source exclusion did not fail closed.");
            }
            finally
            {
                AvatarBuildCapture.Snapshots.Clear();
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(built);
                UnityEngine.Object.DestroyImmediate(root);
                foreach (VRCExpressionsMenu menu in menus)
                {
                    UnityEngine.Object.DestroyImmediate(menu);
                }

                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static object AddToggle(GameObject root, string path)
        {
            var prop = new GameObject(path);
            prop.transform.SetParent(root.transform, false);
            object toggle = FindType("com.vrcfury.api.FuryComponents").GetMethod("CreateToggle").Invoke(null, new object[] { root });
            toggle.GetType().GetMethod("SetMenuPath").Invoke(toggle, new object[] { path });
            object actions = toggle.GetType().GetMethod("GetActions").Invoke(toggle, null);
            actions.GetType().GetMethod("AddTurnOn").Invoke(actions, new object[] { prop });
            return toggle;
        }

        private static void AddModularMenu(GameObject root, List<VRCExpressionsMenu> menus, string label)
        {
            var accessory = new GameObject(label);
            accessory.transform.SetParent(root.transform, false);
            VRCExpressionsMenu menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menus.Add(menu);
            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = label,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "Accessory" },
            });
            Component installer = accessory.AddComponent(FindType("nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller"));
            installer.GetType().GetField("menuToAppend").SetValue(installer, menu);
            Component parameters = accessory.AddComponent(FindType("nadena.dev.modular_avatar.core.ModularAvatarParameters"));
            using (var serialized = new SerializedObject(parameters))
            {
                SerializedProperty entries = serialized.FindProperty("parameters");
                entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("nameOrPrefix").stringValue = "Accessory";
                entry.FindPropertyRelative("internalParameter").boolValue = true;
                SerializedProperty type = entry.FindPropertyRelative("syncType");
                type.enumValueIndex = Array.IndexOf(type.enumNames, "Bool");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void AddModularMenuItem(GameObject root)
        {
            var group = new GameObject("Menu group");
            group.transform.SetParent(root.transform, false);
            group.AddComponent(FindType("nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller"));
            group.AddComponent(FindType("nadena.dev.modular_avatar.core.ModularAvatarMenuGroup"));
            var submenu = new GameObject("Wardrobe");
            submenu.transform.SetParent(group.transform, false);
            Component parent = submenu.AddComponent(FindType("nadena.dev.modular_avatar.core.ModularAvatarMenuItem"));
            parent.GetType().GetField("Control").SetValue(parent, new VRCExpressionsMenu.Control
            {
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
            });
            FieldInfo menuSource = parent.GetType().GetField("MenuSource");
            menuSource.SetValue(parent, Enum.Parse(menuSource.FieldType, "Children"));
            var toggle = new GameObject("Generated outfit");
            toggle.transform.SetParent(submenu.transform, false);
            Component item = toggle.AddComponent(FindType("nadena.dev.modular_avatar.core.ModularAvatarMenuItem"));
            item.GetType().GetField("Control").SetValue(item, new VRCExpressionsMenu.Control
            {
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                value = 1,
            });
            item.GetType().GetField("automaticValue").SetValue(item, true);
            toggle.AddComponent(FindType("nadena.dev.modular_avatar.core.ModularAvatarObjectToggle"));
        }

        private static string[] GeneratedAssets()
        {
            string directory = "Packages/com.vrcfury.temp/Builds";
            return AssetDatabase.IsValidFolder(directory) ? AssetDatabase.FindAssets("", new[] { directory }).OrderBy(id => id).ToArray() : Array.Empty<string>();
        }

        private static Type FindType(string name)
        {
            return AvatarSourceDiscovery.FindType(name) ?? throw new InvalidOperationException("Install the build tool containing " + name);
        }

        private static object Invoke(RemoteOscAvatarEditor editor, string method, params object[] arguments)
        {
            return typeof(RemoteOscAvatarEditor).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, arguments);
        }

        private static object GetField(RemoteOscAvatarEditor editor, string field)
        {
            return typeof(RemoteOscAvatarEditor).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
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
