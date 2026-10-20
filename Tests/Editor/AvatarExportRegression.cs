// Vinyl - vinyl.dev

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace RemoteOSC.Unity.Editor.Tests
{
    // Copy into the package's Editor folder in an isolated SDK project, then execute Run.
    public static class AvatarExportRegression
    {
        public static void Run()
        {
            try
            {
                RunCase(false, false, false);
                RunCase(false, false, true);
                RunCase(false, true, false);
                RunCase(false, true, true);
                RunCase(true, true, false);
                Debug.Log("PASS RemoteOSC export exclusions survive menu/parameter changes and unresolved mappings remain blocked.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
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

        private static bool IsAllowed(string yaml, string name)
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
