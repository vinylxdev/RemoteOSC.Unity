// Vinyl - vinyl.dev

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace RemoteOSC.Unity.Editor
{
    internal sealed class ModularAvatarDiscovery
    {
        private const string ComponentNamespace = "nadena.dev.modular_avatar.core.";
        private static readonly string[] PrefixSuffixes = { "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish" };
        private readonly AvatarSourceDiscovery result;
        private readonly Component[] components;
        private readonly Dictionary<string, string> effectiveNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<Component, string> autoMenuParameters = new Dictionary<Component, string>();
        private readonly Dictionary<GameObject, Dictionary<string, string>> mappings
            = new Dictionary<GameObject, Dictionary<string, string>>();
        private readonly HashSet<UnityEngine.Object> ancestors = new HashSet<UnityEngine.Object>();
        private readonly List<Component> installers;
        private readonly HashSet<Component> redirectedInstallers = new HashSet<Component>();
        private object parameterInfo;
        private MethodInfo getParameterMappings;

        private ModularAvatarDiscovery(AvatarSourceDiscovery result, GameObject root)
        {
            this.result = result;
            components = root.GetComponentsInChildren<Component>(true)
                .Where(component => component != null)
                .ToArray();
            installers = components.Where(component => IsComponent(component, "ModularAvatarMenuInstaller")).ToList();
            foreach (Component target in components.Where(component => IsComponent(component, "ModularAvatarMenuInstallTarget")))
            {
                if (AvatarSourceDiscovery.Get(target, "installer") is Component installer)
                {
                    redirectedInstallers.Add(installer);
                }
            }
        }

        internal static void Read(AvatarSourceDiscovery result, GameObject root, VRCAvatarDescriptor descriptor)
        {
            var reader = new ModularAvatarDiscovery(result, root);
            reader.ReadParameters(root);
            reader.VisitMenu(descriptor.expressionsMenu, "", null);
            foreach (Component installer in reader.installers.Where(installer => !reader.redirectedInstallers.Contains(installer)
                && AvatarSourceDiscovery.Get(installer, "installTargetMenu") == null))
            {
                reader.VisitInstaller(installer, "");
            }

            foreach (Component merge in reader.components
                .Where(component => IsComponent(component, "ModularAvatarMergeAnimator")))
            {
                result.ReadController(
                    AvatarSourceDiscovery.Get(merge, "animator") as RuntimeAnimatorController,
                    reader.CreateParameterResolver(merge));
            }
        }

        private void ReadParameters(GameObject root)
        {
            Type type = AvatarSourceDiscovery.FindType("nadena.dev.ndmf.ParameterInfo");
            if (type == null)
            {
                if (components.Any(component => component.GetType().FullName.StartsWith(ComponentNamespace, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "This Modular Avatar version does not expose NDMF parameter inspection. Use the exact preview.");
                }

                ReadDynamicsParameters();

                return;
            }

            parameterInfo = type.GetField("ForUI", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            MethodInfo getParameters = type.GetMethod(
                "GetParametersForObject",
                new[] { typeof(GameObject), type.GetNestedType("ConflictHandler") });
            getParameterMappings = type.GetMethod("GetParameterRemappingsAt", new[] { typeof(GameObject) });
            if (parameterInfo == null || getParameters == null || getParameterMappings == null)
            {
                throw new InvalidOperationException("Parameter inspection is unavailable in this NDMF version.");
            }

            foreach (object parameter in AvatarSourceDiscovery.Items(
                getParameters.Invoke(parameterInfo, new object[] { root, null })))
            {
                string original = AvatarSourceDiscovery.Get(parameter, "OriginalName") as string ?? "";
                string effective = AvatarSourceDiscovery.Get(parameter, "EffectiveName") as string ?? original;
                var source = AvatarSourceDiscovery.Get(parameter, "Source") as Component;
                bool bIsGenerated = AvatarSourceDiscovery.Get(parameter, "IsHidden") is true;
                bool bIsPrefix = AvatarSourceDiscovery.Get(parameter, "Namespace")?.ToString() == "PhysBonesPrefix";
                string parameterType = AvatarSourceDiscovery.Get(parameter, "ParameterType")?.ToString() ?? "Float";
                bool bIsDefinition = AvatarSourceDiscovery.Get(parameter, "IsAnimatorOnly") is false;
                double defaultValue = Convert.ToDouble(AvatarSourceDiscovery.Get(parameter, "DefaultValue") ?? 0);
                string sourceKey = IsComponent(source, "ModularAvatarMenuItem") && bIsGenerated ? "menu" : original;
                string display = IsComponent(source, "ModularAvatarMenuItem") && bIsGenerated ? GetMenuLabel(source) : original;
                if (bIsPrefix)
                {
                    foreach (string suffix in PrefixSuffixes)
                    {
                        effectiveNames[effective + suffix] = result.AddParameter(
                            bIsGenerated ? display + suffix : effective + suffix,
                            suffix == "_IsGrabbed" || suffix == "_IsPosed" ? "Bool" : "Float",
                            source: source,
                            sourceKey: sourceKey + suffix,
                            bIsGenerated: bIsGenerated);
                    }
                }
                else
                {
                    string key = result.AddParameter(
                        bIsGenerated ? display : effective,
                        parameterType == "Trigger" ? "Bool" : parameterType,
                        defaultValue,
                        source,
                        sourceKey,
                        bIsGenerated,
                        parameterType == "Trigger",
                        bIsDefinition);
                    effectiveNames[effective] = key;
                    if (IsComponent(source, "ModularAvatarMenuItem") && bIsGenerated)
                    {
                        autoMenuParameters[source] = key;
                    }
                }
            }
        }

        private void ReadDynamicsParameters()
        {
            foreach (Component component in components)
            {
                string componentType = component.GetType().FullName;
                if (componentType != "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone"
                    && componentType != "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver")
                {
                    continue;
                }

                string name = AvatarSourceDiscovery.Get(component, "parameter") as string;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (componentType == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone")
                {
                    foreach (string suffix in PrefixSuffixes)
                    {
                        result.AddParameter(name + suffix, suffix == "_IsGrabbed" || suffix == "_IsPosed" ? "Bool" : "Float");
                    }
                }
                else
                {
                    result.AddParameter(
                        name,
                        AvatarSourceDiscovery.Get(component, "receiverType")?.ToString() == "Proximity" ? "Float" : "Bool");
                }
            }
        }

        private Func<string, string> CreateParameterResolver(Component context)
        {
            if (context == null || parameterInfo == null)
            {
                return name => result.AddParameter(name);
            }

            if (!mappings.TryGetValue(context.gameObject, out Dictionary<string, string> names))
            {
                names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (object entry in AvatarSourceDiscovery.Items(
                    getParameterMappings.Invoke(parameterInfo, new object[] { context.gameObject })))
                {
                    object pair = AvatarSourceDiscovery.Get(entry, "Key");
                    object value = AvatarSourceDiscovery.Get(entry, "Value");
                    string original = AvatarSourceDiscovery.Get(pair, "Item2") as string ?? "";
                    string effective = AvatarSourceDiscovery.Get(value, "ParameterName") as string ?? original;
                    if (AvatarSourceDiscovery.Get(pair, "Item1")?.ToString() == "PhysBonesPrefix")
                    {
                        foreach (string suffix in PrefixSuffixes)
                        {
                            names[original + suffix] = effective + suffix;
                        }
                    }
                    else
                    {
                        names[original] = effective;
                    }
                }

                mappings.Add(context.gameObject, names);
            }

            return name =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    return "";
                }

                string effective = names.TryGetValue(name, out string remapped) ? remapped : name;
                return effectiveNames.TryGetValue(effective, out string key) ? key : result.AddParameter(effective);
            };
        }

        private void VisitMenu(VRCExpressionsMenu menu, string parent, Component context)
        {
            if (menu == null || !ancestors.Add(menu))
            {
                return;
            }

            try
            {
                Func<string, string> resolve = CreateParameterResolver(context);
                foreach (VRCExpressionsMenu.Control control in menu.controls)
                {
                    string key = "asset:" + AvatarSourceDiscovery.ControlBindingKey(
                        control.type.ToString(),
                        (control.subParameters ?? Array.Empty<VRCExpressionsMenu.Control.Parameter>())
                            .Select(parameter => parameter?.name ?? "")
                            .Concat(new[] { control.parameter?.name ?? "" }));
                    result.AddControl(control, control.name, parent, context, result.NextControlKey(context, key), resolve);
                    if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    {
                        VisitMenu(control.subMenu, AvatarSourceDiscovery.JoinPath(parent, control.name), context);
                    }
                }

                foreach (Component installer in installers.Where(installer => !redirectedInstallers.Contains(installer)
                    && AvatarSourceDiscovery.Get(installer, "installTargetMenu") as VRCExpressionsMenu == menu))
                {
                    VisitInstaller(installer, parent);
                }
            }
            finally
            {
                ancestors.Remove(menu);
            }
        }

        private void VisitInstaller(Component installer, string parent)
        {
            if (!ancestors.Add(installer))
            {
                return;
            }

            try
            {
                Component source = FindMenuSource(installer.gameObject);
                if (source != null)
                {
                    VisitSource(source, parent);
                }
                else
                {
                    VisitMenu(AvatarSourceDiscovery.Get(installer, "menuToAppend") as VRCExpressionsMenu, parent, installer);
                }
            }
            finally
            {
                ancestors.Remove(installer);
            }
        }

        private void VisitSource(Component source, string parent)
        {
            if (!ancestors.Add(source))
            {
                return;
            }

            try
            {
                if (IsComponent(source, "ModularAvatarMenuInstallTarget"))
                {
                    if (AvatarSourceDiscovery.Get(source, "installer") is Component installer)
                    {
                        VisitInstaller(installer, parent);
                    }
                }
                else if (IsComponent(source, "ModularAvatarMenuGroup"))
                {
                    VisitChildren(GetMenuRoot(source, "targetObject"), parent);
                }
                else if (IsComponent(source, "ModularAvatarMenuItem"))
                {
                    var control = AvatarSourceDiscovery.Get(source, "Control") as VRCExpressionsMenu.Control;
                    if (control == null)
                    {
                        return;
                    }

                    string label = GetMenuLabel(source);
                    Func<string, string> resolve = CreateParameterResolver(source);
                    if (string.IsNullOrEmpty(control.parameter?.name) && autoMenuParameters.TryGetValue(source, out string key))
                    {
                        Func<string, string> named = resolve;
                        resolve = name => string.IsNullOrEmpty(name) ? key : named(name);
                    }

                    result.AddControl(control, label, parent, source, "menu", resolve);
                    if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    {
                        string path = AvatarSourceDiscovery.JoinPath(parent, label);
                        if (AvatarSourceDiscovery.Get(source, "MenuSource")?.ToString() == "Children")
                        {
                            VisitChildren(GetMenuRoot(source, "menuSource_otherObjectChildren"), path);
                        }
                        else
                        {
                            VisitMenu(control.subMenu, path, source);
                        }
                    }
                }
            }
            finally
            {
                ancestors.Remove(source);
            }
        }

        private void VisitChildren(GameObject root, string parent)
        {
            foreach (Transform child in root.transform)
            {
                Component source = FindMenuSource(child.gameObject);
                if (source != null)
                {
                    VisitSource(source, parent);
                }
            }
        }

        private static Component FindMenuSource(GameObject gameObject)
        {
            return gameObject.GetComponents<Component>()
                .FirstOrDefault(component => IsComponent(component, "ModularAvatarMenuItem")
                    || IsComponent(component, "ModularAvatarMenuGroup")
                    || IsComponent(component, "ModularAvatarMenuInstallTarget"));
        }

        private static GameObject GetMenuRoot(Component source, string field)
        {
            var target = AvatarSourceDiscovery.Get(source, field) as GameObject;
            // Unassigned references on a cloned component can be Unity's special null objects.
            return target == null ? source.gameObject : target;
        }

        private static bool IsComponent(Component component, string name)
        {
            return component != null && component.GetType().FullName == ComponentNamespace + name;
        }

        private static string GetMenuLabel(Component source)
        {
            string label = AvatarSourceDiscovery.Get(source, "label") as string;
            return string.IsNullOrEmpty(label) ? source.gameObject.name : label;
        }
    }
}
