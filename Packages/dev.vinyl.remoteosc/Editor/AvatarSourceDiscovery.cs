// Vinyl - vinyl.dev

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace RemoteOSC.Unity.Editor
{
    // Source-only inspection. Nothing here creates an avatar copy or invokes build callbacks.
    internal sealed class AvatarSourceDiscovery
    {
        internal readonly List<AvatarConfigExporter.ExportedParameter> Parameters = new List<AvatarConfigExporter.ExportedParameter>();
        internal readonly List<AvatarConfigExporter.ExportedControl> Controls = new List<AvatarConfigExporter.ExportedControl>();
        internal readonly List<string> Warnings = new List<string>();
        private readonly Dictionary<string, AvatarConfigExporter.ExportedParameter> parametersByName
            = new Dictionary<string, AvatarConfigExporter.ExportedParameter>(StringComparer.Ordinal);
        private readonly HashSet<string> declaredParameters = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<(Component, string), int> controlOccurrences = new Dictionary<(Component, string), int>();

        internal static AvatarSourceDiscovery Read(GameObject root, VRCAvatarDescriptor descriptor)
        {
            var result = new AvatarSourceDiscovery();
            foreach (AvatarConfigExporter.ExportedParameter parameter in AvatarConfigExporter.ReadParameters(root, descriptor))
            {
                result.AddParameter(
                    parameter.Name,
                    parameter.Type,
                    parameter.DefaultValue,
                    bIsTrigger: parameter.bIsTrigger,
                    bIsDefinition: true);
            }

            try
            {
                ModularAvatarDiscovery.Read(result, root, descriptor);
            }
            catch (Exception error)
            {
                result.Warnings.Add("Modular Avatar: " + (error.InnerException ?? error).Message);
                result.AddMenu(descriptor.expressionsMenu, "", null, "", name => name);
            }

            try
            {
                VRCFuryDiscovery.Read(result, root);
            }
            catch (Exception error)
            {
                result.Warnings.Add("VRCFury: " + (error.InnerException ?? error).Message);
            }

            return result;
        }

        internal string AddParameter(
            string name,
            string type = "Float",
            double defaultValue = 0,
            Component source = null,
            string sourceKey = "",
            bool bIsGenerated = false,
            bool bIsTrigger = false,
            bool bIsDefinition = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            // Built-ins and triggers must stay protected even when a component tries to scope them.
            bIsGenerated &= source != null && AvatarConfigExporter.CanControl(new AvatarConfigExporter.ExportedParameter
            {
                Name = name,
                bIsTrigger = bIsTrigger,
            });
            string key = bIsGenerated ? "source:" + source.GetInstanceID() + ":" + sourceKey : name;
            if (bIsDefinition)
            {
                declaredParameters.Add(key);
            }

            if (parametersByName.TryGetValue(key, out AvatarConfigExporter.ExportedParameter existing))
            {
                existing.bIsTrigger |= bIsTrigger;
                if (bIsDefinition)
                {
                    existing.Type = type;
                    existing.DefaultValue = defaultValue;
                }

                return key;
            }

            var parameter = new AvatarConfigExporter.ExportedParameter
            {
                Name = key,
                DisplayName = name,
                Type = type,
                DefaultValue = defaultValue,
                bIsTrigger = bIsTrigger,
                Source = bIsGenerated ? source : null,
                SourceKey = bIsGenerated ? "parameter:" + sourceKey : "",
                bIsGenerated = bIsGenerated,
            };
            parametersByName.Add(key, parameter);
            Parameters.Add(parameter);
            return key;
        }

        internal void ReadController(RuntimeAnimatorController controller, Func<string, string> resolve)
        {
            foreach (AvatarConfigExporter.ExportedParameter parameter in AvatarConfigExporter
                .ReadControllerParameters(controller))
            {
                string key = resolve(parameter.Name);
                if (parametersByName.TryGetValue(key, out AvatarConfigExporter.ExportedParameter existing))
                {
                    if (!declaredParameters.Contains(key))
                    {
                        existing.Type = parameter.Type;
                        existing.DefaultValue = parameter.DefaultValue;
                    }

                    existing.bIsTrigger |= parameter.bIsTrigger;
                }
            }
        }

        internal void AddMenu(
            VRCExpressionsMenu menu,
            string prefix,
            Component source,
            string sourceKey,
            Func<string, string> resolve)
        {
            foreach (AvatarConfigExporter.ExportedControl control in AvatarConfigExporter.ReadControls(menu))
            {
                Controls.Add(new AvatarConfigExporter.ExportedControl
                {
                    Path = JoinPath(prefix, control.Path),
                    Menu = JoinPath(prefix, control.Menu),
                    Label = control.Label,
                    Kind = control.Kind,
                    Parameter = resolve(control.Parameter),
                    SubParameters = control.SubParameters.Select(resolve).ToList(),
                    Value = control.Value,
                    Source = source,
                    SourceKey = "control:" + NextControlKey(source,
                        sourceKey + ":" + ControlBindingKey(control.Kind, control.SubParameters.Concat(new[] { control.Parameter }))),
                });
            }
        }

        internal void AddControl(
            VRCExpressionsMenu.Control control,
            string label,
            string parentPath,
            Component source,
            string sourceKey,
            Func<string, string> resolve)
        {
            if (control == null)
            {
                return;
            }

            string parameter = resolve(control.parameter == null ? "" : control.parameter.name);
            List<string> subParameters = (control.subParameters ?? Array.Empty<VRCExpressionsMenu.Control.Parameter>())
                .Select(item => resolve(item == null ? "" : item.name)).ToList();
            if (string.IsNullOrEmpty(parameter) && subParameters.All(string.IsNullOrEmpty))
            {
                return;
            }

            Controls.Add(new AvatarConfigExporter.ExportedControl
            {
                Path = JoinPath(parentPath, label),
                Menu = parentPath,
                Label = label,
                Kind = control.type.ToString(),
                Parameter = parameter,
                SubParameters = subParameters,
                Value = control.value,
                Source = source,
                SourceKey = "control:" + sourceKey,
            });
        }

        internal HashSet<string> ResolveExclusions(
            IEnumerable<RemoteOscAvatar.SourceExclusion> exclusions,
            out bool bHasUnresolvedBlock)
        {
            var pathsByParameter = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var pathsBySource = new Dictionary<(Component, string), HashSet<string>>();
            foreach (AvatarConfigExporter.ExportedControl control in Controls)
            {
                if (control.Source != null)
                {
                    AddPath(pathsBySource, (control.Source, control.SourceKey), control.Path);
                }

                foreach (string name in control.SubParameters
                    .Concat(new[] { control.Parameter })
                    .Where(name => !string.IsNullOrEmpty(name)))
                {
                    AddPath(pathsByParameter, name, control.Path);
                }
            }

            foreach (AvatarConfigExporter.ExportedParameter parameter in Parameters
                .Where(item => item.Source != null))
            {
                if (pathsByParameter.TryGetValue(parameter.Name, out HashSet<string> paths))
                {
                    foreach (string path in paths)
                    {
                        AddPath(pathsBySource, (parameter.Source, parameter.SourceKey), path);
                    }
                }
            }

            var blockedPaths = new HashSet<string>(StringComparer.Ordinal);
            bHasUnresolvedBlock = Warnings.Count > 0;
            foreach (RemoteOscAvatar.SourceExclusion exclusion in exclusions)
            {
                if (exclusion.Source == null
                    || !pathsBySource.TryGetValue((exclusion.Source, exclusion.Key), out HashSet<string> paths))
                {
                    bHasUnresolvedBlock = true;
                    continue;
                }

                blockedPaths.UnionWith(paths);
            }

            return blockedPaths;
        }

        private static void AddPath<TKey>(Dictionary<TKey, HashSet<string>> index, TKey key, string path)
        {
            if (!index.TryGetValue(key, out HashSet<string> paths))
            {
                paths = new HashSet<string>(StringComparer.Ordinal);
                index.Add(key, paths);
            }

            paths.Add(path);
        }

        internal static string JoinPath(string parent, string child)
        {
            return string.IsNullOrEmpty(parent) ? child : string.IsNullOrEmpty(child) ? parent : parent + "/" + child;
        }

        internal static string ControlBindingKey(string kind, IEnumerable<string> bindings)
        {
            // Labels and menu placement are editable; the local bindings identify the control's permission.
            return kind + ":" + string.Join(";", bindings.Select(name => (name ?? "").Length + ":" + name));
        }

        internal string NextControlKey(Component source, string key)
        {
            if (source == null)
            {
                return key;
            }

            var identity = (source, key);
            controlOccurrences.TryGetValue(identity, out int occurrence);
            controlOccurrences[identity] = occurrence + 1;
            return key + ":" + occurrence;
        }

        internal static Type FindType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(name))
                .FirstOrDefault(type => type != null);
        }

        internal static object Get(object instance, string name)
        {
            if (instance == null)
            {
                return null;
            }

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();
            return type.GetField(name, Flags)?.GetValue(instance) ?? type.GetProperty(name, Flags)?.GetValue(instance);
        }

        internal static IEnumerable<object> Items(object instance)
        {
            return instance is IEnumerable items ? items.Cast<object>().Where(item => item != null) : Enumerable.Empty<object>();
        }
    }
}
