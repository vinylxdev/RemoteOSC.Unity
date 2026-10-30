// Vinyl - vinyl.dev

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RemoteOSC.Unity.Editor
{
    // Permission rows can use source or processed data; the build exporter owns final permissions.
    internal sealed class AvatarPermissionPreview
    {
        internal readonly List<Row> Parameters = new List<Row>();
        internal readonly List<Row> Controls = new List<Row>();

        internal AvatarPermissionPreview(
            IEnumerable<AvatarConfigExporter.ExportedParameter> parameters,
            IEnumerable<AvatarConfigExporter.ExportedControl> controls,
            IEnumerable<string> blockedParameters,
            IEnumerable<string> blockedMenuPaths,
            IEnumerable<RemoteOscAvatar.ParameterMenuBinding> blockedBindings = null,
            IEnumerable<RemoteOscAvatar.SourceExclusion> blockedSources = null)
        {
            List<AvatarConfigExporter.ExportedParameter> definitions = parameters.ToList();
            List<AvatarConfigExporter.ExportedControl> menus = controls.ToList();
            var displayNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (AvatarConfigExporter.ExportedParameter parameter in definitions)
            {
                displayNames[parameter.Name] = string.IsNullOrEmpty(parameter.DisplayName)
                    ? parameter.Name : parameter.DisplayName;
            }

            string DisplayName(string name) => displayNames.TryGetValue(name, out string label) ? label : name;

            var sourceRules = new HashSet<(Component, string)>((blockedSources ?? Enumerable.Empty<RemoteOscAvatar.SourceExclusion>())
                .Where(rule => rule.Source != null)
                .Select(rule => (rule.Source, rule.Key)));
            var menuBindings = new Dictionary<AvatarConfigExporter.ExportedControl, string[]>();
            var parametersByPath = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var pathsByParameter = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (AvatarConfigExporter.ExportedControl control in menus)
            {
                string[] bindings = Bindings(control).ToArray();
                menuBindings[control] = bindings;
                if (!parametersByPath.TryGetValue(control.Path, out HashSet<string> names))
                {
                    names = new HashSet<string>(StringComparer.Ordinal);
                    parametersByPath.Add(control.Path, names);
                }

                names.UnionWith(bindings);
                foreach (string name in bindings)
                {
                    if (!pathsByParameter.TryGetValue(name, out HashSet<string> paths))
                    {
                        paths = new HashSet<string>(StringComparer.Ordinal);
                        pathsByParameter.Add(name, paths);
                    }

                    paths.Add(control.Path);
                }
            }

            var blockedNames = new HashSet<string>(blockedParameters, StringComparer.Ordinal);
            blockedNames.UnionWith(definitions.Where(parameter => parameter.Source != null
                && sourceRules.Contains((parameter.Source, parameter.SourceKey))).Select(parameter => parameter.Name));
            List<RemoteOscAvatar.ParameterMenuBinding> deniedBindings =
                (blockedBindings ?? Enumerable.Empty<RemoteOscAvatar.ParameterMenuBinding>())
                    .Where(binding => blockedNames.Contains(binding.Name))
                    .ToList();
            var renamedBlocks = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var splitRules = new HashSet<string>(StringComparer.Ordinal);
            foreach (RemoteOscAvatar.ParameterMenuBinding binding in deniedBindings)
            {
                string[] targetNames = binding.MenuPaths.Where(parametersByPath.ContainsKey)
                    .SelectMany(path => parametersByPath[path])
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (targetNames.Length > 1)
                {
                    splitRules.Add(binding.Name);
                }

                foreach (string name in targetNames)
                {
                    if (!renamedBlocks.TryGetValue(name, out HashSet<string> sourceNames))
                    {
                        sourceNames = new HashSet<string>(StringComparer.Ordinal);
                        renamedBlocks.Add(name, sourceNames);
                    }

                    sourceNames.Add(binding.Name);
                }
            }

            blockedNames.UnionWith(renamedBlocks.Keys);
            var blockedPaths = new HashSet<string>(blockedMenuPaths, StringComparer.Ordinal);
            blockedPaths.UnionWith(menus.Where(control => control.Source != null
                && sourceRules.Contains((control.Source, control.SourceKey))).Select(control => control.Path));
            var protectedNames = new HashSet<string>(definitions.Where(parameter => !AvatarConfigExporter.CanControl(parameter))
                .Select(parameter => parameter.Name), StringComparer.Ordinal);
            var menuRules = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (AvatarConfigExporter.ExportedControl control in menus)
            {
                foreach (string path in MatchingMenuPaths(control.Path, blockedPaths))
                {
                    foreach (string parameter in menuBindings[control])
                    {
                        if (!menuRules.TryGetValue(parameter, out HashSet<string> rules))
                        {
                            rules = new HashSet<string>(StringComparer.Ordinal);
                            menuRules.Add(parameter, rules);
                        }

                        rules.Add(path);
                    }
                }
            }

            foreach (AvatarConfigExporter.ExportedParameter parameter in definitions.OrderBy(item =>
                string.IsNullOrEmpty(item.DisplayName) ? item.Name : item.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                bool bIsProtected = protectedNames.Contains(parameter.Name);
                string reason = bIsProtected ? EditorText.Get("unity.permissions.protected_hint") : "";
                if (!bIsProtected && menuRules.TryGetValue(parameter.Name, out HashSet<string> rules))
                {
                    reason = EditorText.Get(
                        "unity.permissions.blocked_by_menu",
                        string.Join(", ", rules.OrderBy(path => path, StringComparer.Ordinal)));
                }

                renamedBlocks.TryGetValue(parameter.Name, out HashSet<string> sourceNames);
                if (string.IsNullOrEmpty(reason) && sourceNames != null && sourceNames.Any(splitRules.Contains))
                {
                    reason = EditorText.Get("unity.permissions.split_exclusion");
                }

                string label = string.IsNullOrEmpty(parameter.DisplayName) ? parameter.Name : parameter.DisplayName;
                string details = parameter.Type
                    + (parameter.bIsGenerated ? "  ·  " + EditorText.Get("unity.preview.generated_name") : "");
                string sourcePath = parameter.Source == null ? ""
                    : AnimationUtility.CalculateTransformPath(parameter.Source.transform, null);
                if (!string.IsNullOrEmpty(sourcePath))
                {
                    details += "  ·  " + sourcePath;
                }

                bool bIsReadOnly = parameter.bIsGenerated && !pathsByParameter.ContainsKey(parameter.Name);
                if (bIsReadOnly)
                {
                    details += "  ·  " + EditorText.Get("unity.preview.unbound_parameter");
                }

                Parameters.Add(new Row(
                    parameter.Name,
                    label,
                    sourcePath,
                    details,
                    reason,
                    blockedNames.Contains(parameter.Name),
                    bIsProtected,
                    pathsByParameter.TryGetValue(parameter.Name, out HashSet<string> paths)
                        ? paths.ToArray() : Array.Empty<string>(),
                    sourceNames != null
                        ? sourceNames.Concat(new[] { parameter.Name }).Distinct().ToArray() : new[] { parameter.Name },
                    parameter.Source,
                    parameter.SourceKey,
                    bIsReadOnly));
            }

            foreach (AvatarConfigExporter.ExportedControl control in menus.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                string[] bindings = menuBindings[control];
                bool bIsProtected = bindings.Any(parameter => protectedNames.Contains(parameter));
                string reason = bIsProtected ? EditorText.Get("unity.permissions.protected_binding_hint") : "";
                string[] deniedNames = bindings.Where(parameter => blockedNames.Contains(parameter)).ToArray();
                if (!bIsProtected && deniedNames.Length > 0)
                {
                    reason = EditorText.Get(
                        "unity.permissions.blocked_by_parameter",
                        string.Join(", ", deniedNames.Select(DisplayName)));
                }

                // Allowing one row must never clear a parent rule or another control's exclusion.
                string[] otherRules = MatchingMenuPaths(control.Path, blockedPaths).Where(path => path != control.Path)
                    .Concat(bindings.Where(parameter => menuRules.ContainsKey(parameter))
                        .SelectMany(parameter => menuRules[parameter]).Where(path => path != control.Path))
                    .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                if (string.IsNullOrEmpty(reason) && otherRules.Length > 0)
                {
                    reason = EditorText.Get("unity.permissions.blocked_by_menu", string.Join(", ", otherRules));
                }

                string details = EditorText.GetControlKind(control.Kind)
                    + (bindings.Length == 0 ? "" : "  ·  " + string.Join(", ", bindings.Select(DisplayName)));
                Controls.Add(new Row(control.Path, control.Label, control.Menu, details, reason,
                    blockedPaths.Contains(control.Path), bIsProtected, source: control.Source, sourceKey: control.SourceKey));
            }
        }

        internal static bool MatchesMenuPath(string controlPath, string blockedPath)
        {
            return controlPath == blockedPath || controlPath.StartsWith(blockedPath + "/", StringComparison.Ordinal);
        }

        private static IEnumerable<string> MatchingMenuPaths(string controlPath, HashSet<string> blockedPaths)
        {
            string path = controlPath;
            while (true)
            {
                if (blockedPaths.Contains(path))
                {
                    yield return path;
                }

                int separator = path.LastIndexOf('/');
                if (separator < 0)
                {
                    yield break;
                }

                path = path.Substring(0, separator);
            }
        }

        private static IEnumerable<string> Bindings(AvatarConfigExporter.ExportedControl control)
        {
            return new[] { control.Parameter }.Concat(control.SubParameters).Where(name => !string.IsNullOrEmpty(name))
                .Distinct(StringComparer.Ordinal);
        }

        internal sealed class Row
        {
            internal readonly string Key;
            internal readonly string Label;
            internal readonly string Group;
            internal readonly string Details;
            internal readonly string Reason;
            internal readonly bool bIsAllowed;
            internal readonly bool bCanEdit;
            internal readonly bool bIsProtected;
            internal readonly string[] MenuPaths;
            internal readonly string[] BlockedKeys;
            internal readonly Component Source;
            internal readonly string SourceKey;

            internal Row(
                string key,
                string label,
                string group,
                string details,
                string reason,
                bool bIsBlocked,
                bool bIsProtected,
                string[] menuPaths = null,
                string[] blockedKeys = null,
                Component source = null,
                string sourceKey = "",
                bool bIsReadOnly = false)
            {
                Key = key;
                Label = label;
                Group = group;
                Details = details;
                Reason = reason;
                bCanEdit = string.IsNullOrEmpty(reason) && !bIsReadOnly;
                bIsAllowed = string.IsNullOrEmpty(reason) && !bIsBlocked;
                this.bIsProtected = bIsProtected;
                MenuPaths = menuPaths ?? Array.Empty<string>();
                BlockedKeys = blockedKeys ?? new[] { key };
                Source = source;
                SourceKey = sourceKey;
            }

            internal bool Matches(string search, int filter)
            {
                bool bMatchesState = filter == 0 || (filter == 1 ? bIsAllowed : !bIsAllowed);
                if (!bMatchesState || string.IsNullOrWhiteSpace(search))
                {
                    return bMatchesState;
                }

                string query = search.Trim();
                return Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || Group.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || Details.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || Reason.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }
}
