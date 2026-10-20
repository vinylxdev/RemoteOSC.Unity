// Vinyl - vinyl.dev

using System;
using System.Collections.Generic;
using System.Linq;

namespace RemoteOSC.Unity.Editor
{
    // A source preview only: the build exporter still owns final names and permissions.
    internal sealed class AvatarPermissionPreview
    {
        internal readonly List<Row> Parameters = new List<Row>();
        internal readonly List<Row> Controls = new List<Row>();

        internal AvatarPermissionPreview(
            IEnumerable<AvatarConfigExporter.ExportedParameter> parameters,
            IEnumerable<AvatarConfigExporter.ExportedControl> controls,
            IEnumerable<string> blockedParameters,
            IEnumerable<string> blockedMenuPaths)
        {
            var definitions = parameters.ToList();
            var menus = controls.ToList();
            var blockedNames = new HashSet<string>(blockedParameters, StringComparer.Ordinal);
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
                string reason = bIsProtected ? EditorText.Get("System parameters and animator triggers are always blocked.") : "";
                if (!bIsProtected && menuRules.TryGetValue(parameter.Name, out HashSet<string> rules))
                {
                    reason = EditorText.Get("Blocked by menu rule: {0}", string.Join(", ", rules.OrderBy(path => path, StringComparer.Ordinal)));
                }

                Parameters.Add(new Row(parameter.Name, parameter.Name, "", parameter.Type, reason,
                    blockedNames.Contains(parameter.Name), bIsProtected));
            }

            foreach (AvatarConfigExporter.ExportedControl control in menus.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                string[] bindings = Bindings(control).ToArray();
                bool bIsProtected = bindings.Any(parameter => protectedNames.Contains(parameter));
                string reason = bIsProtected ? EditorText.Get("Uses a system parameter or animator trigger.") : "";
                string[] deniedNames = bindings.Where(parameter => blockedNames.Contains(parameter)).ToArray();
                if (!bIsProtected && deniedNames.Length > 0)
                {
                    reason = EditorText.Get("Blocked by parameter: {0}", string.Join(", ", deniedNames));
                }

                // Allowing one row must never clear a parent rule or another control's exclusion.
                string[] otherRules = blockedPaths.Where(path => path != control.Path && MatchesMenuPath(control.Path, path))
                    .Concat(bindings.Where(parameter => menuRules.ContainsKey(parameter))
                        .SelectMany(parameter => menuRules[parameter]).Where(path => path != control.Path))
                    .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                if (string.IsNullOrEmpty(reason) && otherRules.Length > 0)
                {
                    reason = EditorText.Get("Blocked by menu rule: {0}", string.Join(", ", otherRules));
                }

                string details = EditorText.Get(control.Kind) + (bindings.Length == 0 ? "" : "  ·  " + string.Join(", ", bindings));
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

            internal Row(string key, string label, string group, string details, string reason, bool bIsBlocked, bool bIsProtected)
            {
                Key = key;
                Label = label;
                Group = group;
                Details = details;
                Reason = reason;
                bCanEdit = string.IsNullOrEmpty(reason);
                bIsAllowed = bCanEdit && !bIsBlocked;
                this.bIsProtected = bIsProtected;
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
