// Vinyl - vinyl.dev

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace RemoteOSC.Unity.Editor
{
    internal static class AvatarConfigExporter
    {
        private static readonly HashSet<string> BuiltInParameters = new HashSet<string>
        {
            "IsLocal", "Viseme", "Voice", "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight",
            "AngularY", "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude", "Upright", "Grounded", "Seated",
            "AFK", "TrackingType", "VRMode", "MuteSelf", "InStation", "Earmuffs", "ScaleFactor", "ScaleFactorInverse",
            "EyeHeightAsMeters", "EyeHeightAsPercent",
        };

        internal static bool CanControl(ExportedParameter parameter)
        {
            return !parameter.bIsTrigger && !BuiltInParameters.Contains(parameter.Name);
        }

        internal static void Export(GameObject root, AvatarSnapshot snapshot)
        {
            VRCAvatarDescriptor descriptor = root.GetComponent<VRCAvatarDescriptor>();
            PipelineManager pipeline = root.GetComponent<PipelineManager>();
            if (descriptor == null || pipeline == null || string.IsNullOrEmpty(pipeline.blueprintId))
            {
                Debug.LogWarning(EditorText.Get("RemoteOSC: no avatar ID yet. Upload the avatar once, then build again to export its config."));
                return;
            }

            string avatarId = pipeline.blueprintId;
            if (!avatarId.StartsWith("avtr_", StringComparison.Ordinal) || avatarId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || avatarId.Contains("/") || avatarId.Contains("\\"))
            {
                throw new InvalidOperationException(EditorText.Get("The avatar has an invalid blueprint ID."));
            }

            List<ExportedParameter> parameters = ReadParameters(root, descriptor);
            List<ExportedControl> controls = ReadControls(descriptor.expressionsMenu);
            var names = new HashSet<string>(parameters.Select(parameter => parameter.Name));
            var blocked = new HashSet<string>(snapshot.BlockedParameters);
            foreach (ExportedControl control in controls)
            {
                if (snapshot.BlockedMenuPaths.Any(path => control.Path == path || control.Path.StartsWith(path + "/", StringComparison.Ordinal)))
                {
                    blocked.Add(control.Parameter);
                    blocked.UnionWith(control.SubParameters);
                }
            }

            // If a denied raw parameter was renamed without any menu binding, its destination is unknowable.
            // Keep the export closed until the owner excludes its final name or a stable menu path.
            var finalControlPaths = new HashSet<string>(controls.Select(control => control.Path));
            bool bHasUnresolvedBlock = snapshot.BlockedParameters.Any(name => !names.Contains(name)
                && (!snapshot.BlockedParameterPaths.TryGetValue(name, out HashSet<string> paths)
                    || !paths.All(path => finalControlPaths.Contains(path))));
            if (bHasUnresolvedBlock)
            {
                Debug.LogWarning(EditorText.Get("RemoteOSC: excluded names were renamed without a menu binding. All parameters are blocked; review the final mapping."));
            }

            var yaml = new StringBuilder();
            yaml.Append("version: 1\navatarId: ").Append(Quote(avatarId)).Append("\nname: ").Append(Quote(root.name.Replace("(Clone)", ""))).Append('\n');
            yaml.Append(parameters.Count == 0 ? "parameters: []\n" : "parameters:\n");
            foreach (ExportedParameter parameter in parameters.OrderBy(parameter => parameter.Name, StringComparer.Ordinal))
            {
                bool bIsAllowed = !blocked.Contains(parameter.Name) && CanControl(parameter) && !bHasUnresolvedBlock;
                yaml.Append("  - name: ").Append(Quote(parameter.Name)).Append('\n');
                yaml.Append("    type: ").Append(Quote(parameter.Type)).Append('\n');
                yaml.Append("    address: ").Append(Quote("/avatar/parameters/" + parameter.Name.Replace(' ', '_'))).Append('\n');
                yaml.Append("    allowed: ").Append(bIsAllowed ? "true" : "false").Append('\n');
                yaml.Append("    defaultValue: ").Append(Number(parameter.DefaultValue)).Append('\n');
                yaml.Append("    min: ").Append(parameter.Type == "Float" ? "-1" : "0").Append('\n');
                yaml.Append("    max: ").Append(parameter.Type == "Int" ? "255" : "1").Append('\n');
            }

            var validControls = controls.Where(control =>
                (string.IsNullOrEmpty(control.Parameter) || names.Contains(control.Parameter))
                && control.SubParameters.All(parameter => string.IsNullOrEmpty(parameter) || names.Contains(parameter))).ToList();
            yaml.Append(validControls.Count == 0 ? "controls: []\n" : "controls:\n");
            foreach (ExportedControl control in validControls)
            {
                yaml.Append("  - label: ").Append(Quote(control.Label)).Append('\n');
                yaml.Append("    menu: ").Append(Quote(control.Menu)).Append('\n');
                yaml.Append("    kind: ").Append(Quote(control.Kind)).Append('\n');
                yaml.Append("    parameter: ").Append(Quote(control.Parameter)).Append('\n');
                yaml.Append("    value: ").Append(Number(control.Value)).Append('\n');
                yaml.Append("    subParameters: [").Append(string.Join(", ", control.SubParameters.Select(Quote))).Append("]\n");
            }

            string projectDirectory = Path.GetDirectoryName(Application.dataPath);
            string directory = string.IsNullOrWhiteSpace(snapshot.OutputDirectory) ? Application.dataPath : snapshot.OutputDirectory;
            if (!Path.IsPathRooted(directory))
            {
                directory = Path.Combine(projectDirectory, directory);
            }

            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, avatarId + ".yaml");
            File.WriteAllText(path + ".tmp", yaml.ToString(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(path + ".tmp", path);
            Debug.Log(EditorText.Get("RemoteOSC exported final avatar mapping to {0}", path));
        }

        internal static List<ExportedParameter> ReadParameters(GameObject root, VRCAvatarDescriptor descriptor)
        {
            var parameters = new Dictionary<string, ExportedParameter>(StringComparer.Ordinal);
            var controllers = new List<RuntimeAnimatorController>();
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                controllers.Add(animator.runtimeAnimatorController);
            }

            foreach (VRCAvatarDescriptor.CustomAnimLayer layer in descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers))
            {
                controllers.Add(layer.animatorController);
            }

            foreach (RuntimeAnimatorController runtime in controllers.Where(controller => controller != null).Distinct())
            {
                RuntimeAnimatorController source = runtime;
                while (source is AnimatorOverrideController overrides)
                {
                    source = overrides.runtimeAnimatorController;
                }

                if (!(source is AnimatorController controller))
                {
                    continue;
                }

                foreach (AnimatorControllerParameter parameter in controller.parameters)
                {
                    parameters[parameter.name] = new ExportedParameter
                    {
                        Name = parameter.name,
                        Type = parameter.type == AnimatorControllerParameterType.Int ? "Int"
                            : parameter.type == AnimatorControllerParameterType.Float ? "Float" : "Bool",
                        DefaultValue = parameter.type == AnimatorControllerParameterType.Int ? parameter.defaultInt
                            : parameter.type == AnimatorControllerParameterType.Float ? parameter.defaultFloat : parameter.defaultBool ? 1 : 0,
                        bIsTrigger = parameter.type == AnimatorControllerParameterType.Trigger,
                    };
                }
            }

            if (descriptor.expressionParameters != null)
            {
                foreach (VRCExpressionParameters.Parameter parameter in descriptor.expressionParameters.parameters)
                {
                    if (string.IsNullOrEmpty(parameter.name))
                    {
                        continue;
                    }

                    parameters[parameter.name] = new ExportedParameter
                    {
                        Name = parameter.name,
                        Type = parameter.valueType.ToString(),
                        DefaultValue = parameter.defaultValue,
                    };
                }
            }

            return parameters.Values.ToList();
        }

        internal static List<ExportedControl> ReadControls(VRCExpressionsMenu menu)
        {
            var controls = new List<ExportedControl>();
            VisitMenu(menu, "", controls, new HashSet<VRCExpressionsMenu>());
            return controls;
        }

        private static void VisitMenu(VRCExpressionsMenu menu, string path, List<ExportedControl> result, HashSet<VRCExpressionsMenu> ancestors)
        {
            if (menu == null || !ancestors.Add(menu))
            {
                return;
            }

            foreach (VRCExpressionsMenu.Control control in menu.controls)
            {
                string controlPath = string.IsNullOrEmpty(path) ? control.name : path + "/" + control.name;
                string parameter = control.parameter == null ? "" : control.parameter.name ?? "";
                List<string> subParameters = control.subParameters == null ? new List<string>()
                    : control.subParameters.Select(item => item == null ? "" : item.name ?? "").ToList();
                if (!string.IsNullOrEmpty(parameter) || subParameters.Any(item => !string.IsNullOrEmpty(item)))
                {
                    result.Add(new ExportedControl
                    {
                        Label = control.name,
                        Menu = path,
                        Path = controlPath,
                        Kind = control.type.ToString(),
                        Parameter = parameter,
                        Value = control.value,
                        SubParameters = subParameters,
                    });
                }

                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                {
                    VisitMenu(control.subMenu, controlPath, result, ancestors);
                }
            }

            ancestors.Remove(menu);
        }

        private static string Number(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            foreach (char character in value ?? "")
            {
                switch (character)
                {
                    case '"':
                        result.Append("\\\"");
                        break;
                    case '\\':
                        result.Append("\\\\");
                        break;
                    case '\n':
                        result.Append("\\n");
                        break;
                    case '\r':
                        result.Append("\\r");
                        break;
                    case '\t':
                        result.Append("\\t");
                        break;
                    default:
                        if (character < 32)
                        {
                            result.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            result.Append(character);
                        }

                        break;
                }
            }

            return result.Append('"').ToString();
        }

        internal sealed class ExportedParameter
        {
            public string Name = "";
            public string Type = "Float";
            public double DefaultValue;
            public bool bIsTrigger;
        }

        internal sealed class ExportedControl
        {
            public string Label = "";
            public string Menu = "";
            public string Path = "";
            public string Kind = "Toggle";
            public string Parameter = "";
            public double Value = 1;
            public List<string> SubParameters = new List<string>();
        }
    }
}
