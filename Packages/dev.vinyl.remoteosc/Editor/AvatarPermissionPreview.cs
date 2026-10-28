// Vinyl - vinyl.dev

using System;
using System.Collections.Generic;
using System.Linq;

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
            IEnumerable<RemoteOscAvatar.ParameterMenuBinding> blockedBindings = null)
        {
            List<AvatarConfigExporter.ExportedParameter> definitions = parameters.ToList();
            List<AvatarConfigExporter.ExportedControl> menus = controls.ToList();
            var blockedNames = new HashSet<string>(blockedParameters, StringComparer.Ordinal);
            List<RemoteOscAvatar.ParameterMenuBinding> deniedBindings = (blockedBindings ?? Enumerable.Empty<RemoteOscAvatar.ParameterMenuBinding>())
                .Where(binding => blockedNames.Contains(binding.Name)).ToList();
            var renamedBlocks = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var splitRules = new HashSet<string>(StringComparer.Ordinal);
            foreach (RemoteOscAvatar.ParameterMenuBinding binding in deniedBindings)
            {
                string[] targetNames = menus.Where(control => binding.MenuPaths.Contains(control.Path)).SelectMany(Bindings)
                    .Distinct(StringComparer.Ordinal).ToArray();
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
            var protectedNames = new HashSet<string>(definitions.Where(parameter => !AvatarConfigExporter.CanControl(parameter))
                .Select(parameter => parameter.Name), StringComparer.Ordinal);
            var menuRules = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (AvatarConfigExporter.ExportedControl control in menus)
            {
                foreach (string path in blockedPaths.Where(path => MatchesMenuPath(control.Path, path)))
                {
                    foreach (string parameter in Bindings(control))
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

            foreach (AvatarConfigExporter.ExportedParameter parameter in definitions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                bool bIsProtected = protectedNames.Contains(parameter.Name);
                string reason = bIsProtected ? EditorText.Get("unity.permissions.protected_hint") : "";
                if (!bIsProtected && menuRules.TryGetValue(parameter.Name, out HashSet<string> rules))
                {
                    reason = EditorText.Get("unity.permissions.blocked_by_menu", string.Join(", ", rules.OrderBy(path => path, StringComparer.Ordinal)));
                }

                renamedBlocks.TryGetValue(parameter.Name, out HashSet<string> sourceNames);
                if (string.IsNullOrEmpty(reason) && sourceNames != null && sourceNames.Any(splitRules.Contains))
                {
                    reason = EditorText.Get("unity.permissions.split_exclusion");
                }

                Parameters.Add(new Row(parameter.Name, parameter.Name, "", parameter.Type, reason,
                    blockedNames.Contains(parameter.Name), bIsProtected,
                    menus.Where(control => Bindings(control).Contains(parameter.Name)).Select(control => control.Path).Distinct().ToArray(),
                    sourceNames != null
                        ? sourceNames.Concat(new[] { parameter.Name }).Distinct().ToArray() : new[] { parameter.Name }));
            }

            foreach (AvatarConfigExporter.ExportedControl control in menus.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                string[] bindings = Bindings(control).ToArray();
                bool bIsProtected = bindings.Any(parameter => protectedNames.Contains(parameter));
                string reason = bIsProtected ? EditorText.Get("unity.permissions.protected_binding_hint") : "";
                string[] deniedNames = bindings.Where(parameter => blockedNames.Contains(parameter)).ToArray();
                if (!bIsProtected && deniedNames.Length > 0)
                {
                    reason = EditorText.Get("unity.permissions.blocked_by_parameter", string.Join(", ", deniedNames));
                }

                // Allowing one row must never clear a parent rule or another control's exclusion.
                string[] otherRules = blockedPaths.Where(path => path != control.Path && MatchesMenuPath(control.Path, path))
                    .Concat(bindings.Where(parameter => menuRules.ContainsKey(parameter))
                        .SelectMany(parameter => menuRules[parameter]).Where(path => path != control.Path))
                    .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                if (string.IsNullOrEmpty(reason) && otherRules.Length > 0)
                {
                    reason = EditorText.Get("unity.permissions.blocked_by_menu", string.Join(", ", otherRules));
                }

                string details = EditorText.GetControlKind(control.Kind) + (bindings.Length == 0 ? "" : "  ·  " + string.Join(", ", bindings));
                Controls.Add(new Row(control.Path, control.Label, control.Menu, details, reason,
                    blockedPaths.Contains(control.Path), bIsProtected));
            }
        }

        internal static bool MatchesMenuPath(string controlPath, string blockedPath)
        {
            return controlPath == blockedPath || controlPath.StartsWith(blockedPath + "/", StringComparison.Ordinal);
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

            internal Row(string key, string label, string group, string details, string reason, bool bIsBlocked, bool bIsProtected,
                string[] menuPaths = null, string[] blockedKeys = null)
            {
                Key = key;
                Label = label;
                Group = group;
                Details = details;
                Reason = reason;
                bCanEdit = string.IsNullOrEmpty(reason);
                bIsAllowed = bCanEdit && !bIsBlocked;
                this.bIsProtected = bIsProtected;
                MenuPaths = menuPaths ?? Array.Empty<string>();
                BlockedKeys = blockedKeys ?? new[] { key };
            }

            internal bool Matches(string search, int filter)
            {
                bool bMatchesState = filter == 0 || (filter == 1 ? bIsAllowed : !bIsAllowed);
                return bMatchesState && (string.IsNullOrWhiteSpace(search)
                    || new[] { Key, Label, Group, Details, Reason }.Any(value => value.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0));
            }
        }
    }
}
