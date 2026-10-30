// Vinyl - vinyl.dev

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace RemoteOSC.Unity.Editor.Tests
{
    // Copy into the package's Editor folder in an isolated SDK project, then execute Run.
    public static class AvatarExportRegression
    {
        public static void Run()
        {
            try
            {
                RunLargeAssetHashCase();
                RunDynamicsInspectionCase();
                RunSourceHashCase();
                RunPermissionCacheCase();
                RunTriggerProtectionCase(false);
                RunTriggerProtectionCase(true);
                RunCase(false, false, false);
                RunCase(false, false, true);
                RunCase(false, true, false);
                RunCase(false, true, true);
                RunCase(true, true, false);
                RunGeneratedBindingCase(false);
                RunGeneratedBindingCase(true);
                RunGeneratedBindingCase(true, true);
                RunPreviewCase(0);
                RunPreviewCase(1);
                RunPreviewCase(2);
                RunSplitBindingCase();
                UnityEngine.Debug.Log("PASS RemoteOSC export exclusions survive menu/parameter changes and unresolved mappings remain blocked.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void RunDynamicsInspectionCase()
        {
            var root = new GameObject("Dynamics inspection regression");
            try
            {
                VRCAvatarDescriptor descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                root.AddComponent<VRCContactReceiver>().parameter = "Contact";
                root.AddComponent<VRCPhysBone>().parameter = "Bone";
                AvatarSourceDiscovery source = AvatarSourceDiscovery.Read(root, descriptor);
                Check(source.Warnings.Count == 0, string.Join("\n", source.Warnings));
                Check(source.Parameters.Any(parameter => parameter.Name == "Contact" && parameter.Type == "Bool"),
                    "The contact receiver parameter was not discovered.");
                Check(source.Parameters.Count(parameter => parameter.Name.StartsWith("Bone_", StringComparison.Ordinal)) == 5,
                    "PhysBone parameter suffixes were not discovered.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void RunLargeAssetHashCase()
        {
            var root = new GameObject("Large source asset hash regression");
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            var mesh = new Mesh();
            var material = new Material(Shader.Find("Standard"));
            string assetPath = "Assets/RemoteOscLargeSource-" + Guid.NewGuid().ToString("N") + ".asset";
            try
            {
                mesh.vertices = Enumerable.Range(0, 10000).Select(index => index % 2 == 0 ? Vector3.zero : Vector3.one).ToArray();
                AssetDatabase.CreateAsset(material, assetPath);
                AssetDatabase.AddObjectToAsset(texture, material);
                AssetDatabase.AddObjectToAsset(mesh, material);
                material.mainTexture = texture;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
                root.AddComponent<MeshRenderer>().sharedMaterial = material;
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                Check(EditorUtility.CollectDependencies(new UnityEngine.Object[] { root }).Contains(texture),
                    "The large asset fixture is missing its texture dependency.");
                Hash128 original = AvatarBuildPreview.GetSourceHash(root);
                Check(AvatarBuildPreview.GetSourceHash(root) == original, "Unchanged native assets changed the source hash.");

                EditorUtility.SetDirty(texture);
                EditorUtility.SetDirty(mesh);
                Check(AvatarBuildPreview.GetSourceHash(root) == original, "Native asset dirty notifications invalidated unchanged content.");
                texture.SetPixel(0, 0, Color.red);
                texture.Apply();
                EditorUtility.SetDirty(texture);
                Hash128 afterTextureEdit = AvatarBuildPreview.GetSourceHash(root);
                Check(afterTextureEdit != original, "An unsaved texture edit did not change the source hash.");
                mesh.vertices = Enumerable.Range(0, 10000).Select(index => index % 2 == 0 ? Vector3.one : Vector3.zero).ToArray();
                EditorUtility.SetDirty(mesh);
                Check(AvatarBuildPreview.GetSourceHash(root) != afterTextureEdit, "An unsaved mesh edit did not change the source hash.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void RunPermissionCacheCase()
        {
            const int EntryCount = 3000;
            var root = new GameObject("Permission cache regression");
            VRCExpressionParameters parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            VRCExpressionsMenu menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            RemoteOscAvatarEditor editor = null;
            try
            {
                VRCAvatarDescriptor descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.expressionParameters = parameters;
                descriptor.expressionsMenu = menu;
                descriptor.baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                parameters.parameters = Enumerable.Range(0, EntryCount).Select(index => new VRCExpressionParameters.Parameter
                {
                    name = "Parameter" + index,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                }).ToArray();
                menu.controls = parameters.parameters.Select(parameter => new VRCExpressionsMenu.Control
                {
                    name = parameter.name,
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter.name },
                }).ToList();
                RemoteOscAvatar component = root.AddComponent<RemoteOscAvatar>();
                editor = (RemoteOscAvatarEditor)UnityEditor.Editor.CreateEditor(component);
                MethodInfo getPreview = typeof(RemoteOscAvatarEditor).GetMethod("GetPermissionPreview", BindingFlags.Instance | BindingFlags.NonPublic);
                var arguments = new object[] { descriptor };
                var stopwatch = Stopwatch.StartNew();
                var preview = (AvatarPermissionPreview)getPreview.Invoke(editor, arguments);
                Check(preview.Parameters.Count == EntryCount && preview.Controls.Count == EntryCount, "Large preview lost entries.");
                long buildMilliseconds = stopwatch.ElapsedMilliseconds;
                stopwatch.Restart();
                for (int index = 0; index < 1000; index++)
                {
                    Check(ReferenceEquals(preview, getPreview.Invoke(editor, arguments)), "An unchanged Inspector rebuilt permission rows.");
                }

                UnityEngine.Debug.Log("Permission preview: " + EntryCount + " entries built in " + buildMilliseconds
                    + " ms; 1000 cached reads in " + stopwatch.ElapsedMilliseconds + " ms.");
                SerializedProperty blocked = editor.serializedObject.FindProperty("BlockedParameterNames");
                RemoteOscAvatarEditor.SetAllowed(blocked, "Parameter100", false);
                editor.serializedObject.ApplyModifiedProperties();
                var updated = (AvatarPermissionPreview)getPreview.Invoke(editor, arguments);
                Check(!ReferenceEquals(preview, updated) && !updated.Parameters.Single(row => row.Key == "Parameter100").bIsAllowed,
                    "The cached preview ignored a serialized permission change.");

                var controls = new[]
                {
                    new AvatarConfigExporter.ExportedControl { Path = "Private/Child", Parameter = "Shared" },
                    new AvatarConfigExporter.ExportedControl { Path = "Public", Parameter = "Shared" },
                    new AvatarConfigExporter.ExportedControl { Path = "PrivateSibling", Parameter = "Other" },
                };
                var definitions = new[]
                {
                    new AvatarConfigExporter.ExportedParameter { Name = "Shared" },
                    new AvatarConfigExporter.ExportedParameter { Name = "Other" },
                };
                var grouped = new AvatarPermissionPreview(definitions, controls, Array.Empty<string>(), new[] { "Private" });
                Check(grouped.Parameters.Single(row => row.Key == "Shared").MenuPaths.Length == 2,
                    "The binding index lost a shared parameter's menu paths.");
                Check(grouped.Controls.Where(row => row.Key != "PrivateSibling").All(row => !row.bIsAllowed && !row.bCanEdit)
                    && grouped.Controls.Single(row => row.Key == "PrivateSibling").bIsAllowed,
                    "Parent menu exclusions or shared bindings changed during indexing.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(menu);
            }
        }

        private static void RunSourceHashCase()
        {
            var root = new GameObject("Source hash regression");
            VRCExpressionParameters parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            string assetPath = "Assets/RemoteOscSourceHash-" + Guid.NewGuid().ToString("N") + ".asset";
            try
            {
                AssetDatabase.CreateAsset(parameters, assetPath);
                VRCAvatarDescriptor descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.customExpressions = true;
                descriptor.expressionParameters = parameters;
                RemoteOscAvatar component = root.AddComponent<RemoteOscAvatar>();
                Hash128 original = AvatarBuildPreview.GetSourceHash(root);
                component.BlockedParameterNames.Add("Private");
                EditorUtility.SetDirty(component);
                Check(AvatarBuildPreview.GetSourceHash(root) == original, "Permission edits changed the preview source hash.");

                EditorUtility.SetDirty(root);
                EditorUtility.SetDirty(descriptor);
                EditorUtility.SetDirty(parameters);
                Check(AvatarBuildPreview.GetSourceHash(root) == original, "Dirty notifications without content changes invalidated the preview.");
                EditorUtility.ClearDirty(root);
                EditorUtility.ClearDirty(descriptor);
                EditorUtility.ClearDirty(parameters);
                Check(AvatarBuildPreview.GetSourceHash(root) == original, "Clearing dirty counters invalidated the preview.");

                parameters.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter { name = "NewParameter" },
                };
                Check(AvatarBuildPreview.GetSourceHash(root) != original, "An unsaved parameter edit did not change the preview source hash.");
                Hash128 beforeHierarchyChange = AvatarBuildPreview.GetSourceHash(root);
                var child = new GameObject("New source child");
                child.transform.SetParent(root.transform, false);
                Check(AvatarBuildPreview.GetSourceHash(root) != beforeHierarchyChange, "A hierarchy edit did not change the preview source hash.");
                var sibling = new GameObject("Source sibling");
                sibling.transform.SetParent(root.transform, false);
                Hash128 beforeReorder = AvatarBuildPreview.GetSourceHash(root);
                sibling.transform.SetSiblingIndex(0);
                Check(AvatarBuildPreview.GetSourceHash(root) != beforeReorder, "Reordering source children did not change the preview source hash.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void RunTriggerProtectionCase(bool bUseExpressionParameters)
        {
            var root = new GameObject("Trigger protection regression");
            var triggerController = new AnimatorController();
            var boolController = new AnimatorController();
            VRCExpressionParameters expressions = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            try
            {
                triggerController.AddParameter("Protected", AnimatorControllerParameterType.Trigger);
                boolController.AddParameter("Protected", AnimatorControllerParameterType.Bool);
                VRCAvatarDescriptor descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.baseAnimationLayers = new[]
                {
                    new VRCAvatarDescriptor.CustomAnimLayer { animatorController = triggerController },
                    new VRCAvatarDescriptor.CustomAnimLayer { animatorController = boolController },
                };
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                if (bUseExpressionParameters)
                {
                    expressions.parameters = new[]
                    {
                        new VRCExpressionParameters.Parameter { name = "Protected", valueType = VRCExpressionParameters.ValueType.Bool },
                    };
                    descriptor.expressionParameters = expressions;
                }

                AvatarConfigExporter.ExportedParameter parameter = AvatarConfigExporter.ReadParameters(root, descriptor)
                    .Single(item => item.Name == "Protected");
                Check(!AvatarConfigExporter.CanControl(parameter), "A merged boolean parameter made an animator trigger controllable.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(triggerController);
                UnityEngine.Object.DestroyImmediate(boolController);
                UnityEngine.Object.DestroyImmediate(expressions);
            }
        }

        private static void RunSplitBindingCase()
        {
            var controls = new[]
            {
                new AvatarConfigExporter.ExportedControl { Path = "First", Parameter = "GeneratedA" },
                new AvatarConfigExporter.ExportedControl { Path = "Second", Parameter = "GeneratedB" },
            };
            var parameters = new[]
            {
                new AvatarConfigExporter.ExportedParameter { Name = "GeneratedA" },
                new AvatarConfigExporter.ExportedParameter { Name = "GeneratedB" },
            };
            var binding = new RemoteOscAvatar.ParameterMenuBinding
            {
                Name = "OldSharedName",
                MenuPaths = new System.Collections.Generic.List<string> { "First", "Second" },
            };
            var preview = new AvatarPermissionPreview(parameters, controls, new[] { "OldSharedName" }, Array.Empty<string>(), new[] { binding });
            Check(preview.Parameters.All(row => !row.bIsAllowed && !row.bCanEdit),
                "Allowing one split parameter could clear another parameter's exclusion.");
        }

        private static void RunPreviewCase(int failure)
        {
            var root = new GameObject("Preview regression");
            var pending = new AvatarSnapshot();
            AvatarBuildCapture.Snapshots[root.GetInstanceID()] = pending;
            GameObject clone = null;
            VRCExpressionParameters generatedParameters = null;
            VRCExpressionsMenu generatedMenu = null;
            try
            {
                VRCAvatarDescriptor descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                root.AddComponent<RemoteOscAvatar>();
                AvatarBuildPreview preview = null;
                try
                {
                    preview = AvatarBuildPreview.Create(root, processed =>
                    {
                        clone = processed;
                        Check(AvatarBuildPreview.IsPreview(processed), "Preview callbacks were not suppressed.");
                        Check(new AvatarBuildCapture().OnPreprocessAvatar(processed), "Preview capture failed.");
                        // Even a stale snapshot must not cause a preview to write YAML.
                        PipelineManager pipeline = processed.GetComponent<PipelineManager>() ?? processed.AddComponent<PipelineManager>();
                        pipeline.blueprintId = "avtr_preview_regression";
                        var snapshot = new AvatarSnapshot { OutputDirectory = "\0" };
                        AvatarBuildCapture.Snapshots[processed.GetInstanceID()] = snapshot;
                        Check(new AvatarBuildExport().OnPreprocessAvatar(processed), "Preview attempted export.");
                        Check(AvatarBuildCapture.Snapshots[processed.GetInstanceID()] == snapshot, "Preview consumed a build snapshot.");
                        AvatarBuildCapture.Snapshots.Remove(processed.GetInstanceID());
                        if (failure == 1)
                        {
                            return false;
                        }

                        if (failure == 2)
                        {
                            throw new InvalidOperationException("Expected processor failure.");
                        }

                        VRCAvatarDescriptor builtDescriptor = processed.GetComponent<VRCAvatarDescriptor>();
                        generatedParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                        generatedParameters.parameters = new[]
                        {
                            new VRCExpressionParameters.Parameter { name = "Generated", valueType = VRCExpressionParameters.ValueType.Bool },
                        };
                        generatedMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                        generatedMenu.controls.Add(new VRCExpressionsMenu.Control
                        {
                            name = "Generated control",
                            type = VRCExpressionsMenu.Control.ControlType.Toggle,
                            parameter = new VRCExpressionsMenu.Control.Parameter { name = "Generated" },
                        });
                        builtDescriptor.expressionParameters = generatedParameters;
                        builtDescriptor.expressionsMenu = generatedMenu;
                        return true;
                    });
                    Check(failure == 0, "Failed processing produced a preview.");
                }
                catch (InvalidOperationException)
                {
                    Check(failure != 0, "Successful preview threw an exception.");
                }

                Check(clone == null, "Preview clone survived cleanup.");
                Check(descriptor.expressionParameters == null && descriptor.expressionsMenu == null, "Preview modified the source descriptor.");
                Check(AvatarBuildCapture.Snapshots[root.GetInstanceID()] == pending, "Preview cleared a pending build snapshot.");
                if (failure == 0)
                {
                    Check(preview.Parameters.Any(parameter => parameter.Name == "Generated"), "Generated parameter is missing.");
                    Check(preview.Controls.Any(control => control.Path == "Generated control"), "Generated control is missing.");
                }
            }
            finally
            {
                AvatarBuildCapture.Snapshots.Remove(root.GetInstanceID());
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(generatedParameters);
                UnityEngine.Object.DestroyImmediate(generatedMenu);
            }
        }

        private static void RunGeneratedBindingCase(bool bRemoveBinding, bool bMenuOnly = false)
        {
            string directory = Path.Combine(Path.GetTempPath(), "remoteosc-generated-" + Guid.NewGuid().ToString("N"));
            var root = new GameObject("Generated exclusion regression");
            VRCExpressionParameters parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            VRCExpressionsMenu menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            try
            {
                VRCAvatarDescriptor descriptor = root.AddComponent<VRCAvatarDescriptor>();
                PipelineManager pipeline = root.GetComponent<PipelineManager>() ?? root.AddComponent<PipelineManager>();
                pipeline.blueprintId = "avtr_generated_regression";
                descriptor.baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                RemoteOscAvatar component = root.AddComponent<RemoteOscAvatar>();
                component.OutputDirectory = directory;
                if (bMenuOnly)
                {
                    component.BlockedMenuPaths.Add("Private");
                    component.BlockedGeneratedMenuPaths.Add("Private");
                }
                else
                {
                    component.BlockedParameterNames.Add("PreviewName");
                    component.BlockedParameterBindings.Add(new RemoteOscAvatar.ParameterMenuBinding
                    {
                        Name = "PreviewName",
                        MenuPaths = new System.Collections.Generic.List<string> { "Private" },
                    });
                }
                Check(new AvatarBuildCapture().OnPreprocessAvatar(root), "Generated exclusion capture failed.");
                parameters.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter { name = "BuildName", valueType = VRCExpressionParameters.ValueType.Bool },
                    new VRCExpressionParameters.Parameter { name = "Public", valueType = VRCExpressionParameters.ValueType.Bool },
                };
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = bRemoveBinding ? "Moved" : "Private",
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = "BuildName" },
                });
                descriptor.expressionParameters = parameters;
                descriptor.expressionsMenu = menu;
                if (!bRemoveBinding)
                {
                    var preview = new AvatarPermissionPreview(AvatarConfigExporter.ReadParameters(root, descriptor),
                        AvatarConfigExporter.ReadControls(menu), component.BlockedParameterNames, component.BlockedMenuPaths,
                        component.BlockedParameterBindings);
                    AvatarPermissionPreview.Row row = preview.Parameters.Single(item => item.Key == "BuildName");
                    Check(!row.bIsAllowed && row.bCanEdit && row.BlockedKeys.Contains("PreviewName"),
                        "Refreshed preview lost the exclusion or cannot allow it again.");
                }

                UnityEngine.Object.DestroyImmediate(component);
                Check(new AvatarBuildExport().OnPreprocessAvatar(root), "Generated exclusion export failed.");
                string yaml = File.ReadAllText(Path.Combine(directory, pipeline.blueprintId + ".yaml"));
                Check(!IsAllowed(yaml, "BuildName"), "Generated parameter became allowed after renaming.");
                Check(IsAllowed(yaml, "Public") == !bRemoveBinding, "Missing generated binding did not fail closed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(menu);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void RunCase(bool bBlockParameter, bool bRenameParameter, bool bRenameMenu)
        {
            string directory = Path.Combine(Path.GetTempPath(), "remoteosc-export-" + Guid.NewGuid().ToString("N"));
            var root = new GameObject("Export regression");
            VRCExpressionParameters parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            VRCExpressionsMenu menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            try
            {
                VRCAvatarDescriptor descriptor = root.AddComponent<VRCAvatarDescriptor>();
                PipelineManager pipeline = root.GetComponent<PipelineManager>() ?? root.AddComponent<PipelineManager>();
                pipeline.blueprintId = "avtr_regression";
                descriptor.baseAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                parameters.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter { name = "Secret", valueType = VRCExpressionParameters.ValueType.Bool },
                    new VRCExpressionParameters.Parameter { name = "Public", valueType = VRCExpressionParameters.ValueType.Bool },
                };
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Private",
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = "Secret" },
                    value = 1,
                });
                descriptor.expressionParameters = parameters;
                descriptor.expressionsMenu = menu;
                RemoteOscAvatar component = root.AddComponent<RemoteOscAvatar>();
                component.OutputDirectory = directory;
                if (bBlockParameter)
                {
                    component.BlockedParameterNames.Add("Secret");
                }
                else
                {
                    component.BlockedMenuPaths.Add("Private");
                }

                Check(new AvatarBuildCapture().OnPreprocessAvatar(root), "Capture failed.");
                string finalName = bRenameParameter ? "GeneratedSecret" : "Secret";
                parameters.parameters[0].name = finalName;
                menu.controls[0].parameter.name = finalName;
                if (bRenameMenu)
                {
                    menu.controls[0].name = "Generated Private";
                }

                // Build tools may remove the editor-only component between these callbacks.
                UnityEngine.Object.DestroyImmediate(component);
                Check(new AvatarBuildExport().OnPreprocessAvatar(root), "Export failed.");
                string yaml = File.ReadAllText(Path.Combine(directory, pipeline.blueprintId + ".yaml"));
                Check(!IsAllowed(yaml, finalName), "A denied parameter became controllable after transformation.");
                Check(IsAllowed(yaml, "Public") == !(bRenameParameter && bRenameMenu), "Unexpected unresolved-mapping behavior.");
                Check(!AvatarBuildCapture.Snapshots.ContainsKey(root.GetInstanceID()), "Build snapshot was retained after export.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(menu);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        internal static bool IsAllowed(string yaml, string name)
        {
            string current = "";
            using (var reader = new StringReader(yaml))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("  - name: ", StringComparison.Ordinal))
                    {
                        current = line.Substring("  - name: ".Length).Trim('"');
                    }
                    else if (current == name && line.StartsWith("    allowed: ", StringComparison.Ordinal))
                    {
                        return bool.Parse(line.Substring("    allowed: ".Length));
                    }
                }
            }

            throw new InvalidOperationException("Missing exported parameter: " + name);
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
