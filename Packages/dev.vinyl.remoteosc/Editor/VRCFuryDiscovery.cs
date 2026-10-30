// Vinyl - vinyl.dev

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace RemoteOSC.Unity.Editor
{
    internal static class VRCFuryDiscovery
    {
        internal static void Read(AvatarSourceDiscovery result, GameObject root)
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true)
                .Where(component => component != null && component.GetType().FullName == "VF.Model.VRCFury"))
            {
                // Read serialized models directly; Get/Upgrade/build helpers can mutate the source.
                object content = AvatarSourceDiscovery.Get(component, "content");
                IEnumerable<object> features = AvatarSourceDiscovery.Items(AvatarSourceDiscovery.Get(
                    AvatarSourceDiscovery.Get(component, "config"), "features"));
                if (content != null)
                {
                    features = features.Concat(new[] { content });
                }

                int index = 0;
                foreach (object feature in features)
                {
                    string key = "feature:" + index++;
                    if (feature.GetType().FullName == "VF.Model.Feature.Toggle")
                    {
                        ReadToggle(result, component, feature, key);
                    }
                    else if (feature.GetType().FullName == "VF.Model.Feature.FullController")
                    {
                        ReadController(result, component, feature, key);
                    }
                }
            }
        }

        private static void ReadToggle(AvatarSourceDiscovery result, Component source, object model, string sourceKey)
        {
            string path = AvatarSourceDiscovery.Get(model, "name") as string ?? "";
            bool bIsSlider = AvatarSourceDiscovery.Get(model, "slider") is true;
            bool bIsGlobal = AvatarSourceDiscovery.Get(model, "useGlobalParam") is true
                && !string.IsNullOrWhiteSpace(AvatarSourceDiscovery.Get(model, "globalParam") as string);
            string name = bIsGlobal ? (string)AvatarSourceDiscovery.Get(model, "globalParam")
                : string.IsNullOrEmpty(path) ? source.gameObject.name : path;
            double defaultValue = bIsSlider ? Convert.ToDouble(AvatarSourceDiscovery.Get(model, "defaultSliderValue") ?? 0)
                : AvatarSourceDiscovery.Get(model, "defaultOn") is true ? 1 : 0;
            string parameter = result.AddParameter(name, bIsSlider ? "Float" : "Bool", defaultValue,
                source, sourceKey, !bIsGlobal, bIsDefinition: true);
            if (string.IsNullOrEmpty(path) || AvatarSourceDiscovery.Get(model, "addMenuItem") is false)
            {
                return;
            }

            int separator = path.LastIndexOf('/');
            result.Controls.Add(new AvatarConfigExporter.ExportedControl
            {
                Path = path,
                Menu = separator < 0 ? "" : path.Substring(0, separator),
                Label = separator < 0 ? path : path.Substring(separator + 1),
                Parameter = parameter,
                Kind = bIsSlider ? "RadialPuppet" : AvatarSourceDiscovery.Get(model, "holdButton") is true ? "Button" : "Toggle",
                Source = source,
                SourceKey = "control:" + sourceKey,
                SubParameters = bIsSlider ? new List<string> { parameter } : new List<string>(),
            });
        }

        private static void ReadController(
            AvatarSourceDiscovery result,
            Component source,
            object model,
            string sourceKey)
        {
            List<string> globals = AvatarSourceDiscovery.Items(AvatarSourceDiscovery.Get(model, "globalParams"))
                .OfType<string>()
                .ToList();

            bool IsGlobal(string name)
            {
                bool bIsGlobal = false;
                foreach (string rule in globals)
                {
                    bool bIsNegative = rule.StartsWith("!", StringComparison.Ordinal);
                    string pattern = bIsNegative ? rule.Substring(1) : rule;
                    bool bMatches = pattern.EndsWith("*", StringComparison.Ordinal)
                        ? name.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal) : name == pattern;
                    if (bMatches)
                    {
                        if (bIsNegative)
                        {
                            return false;
                        }

                        bIsGlobal = true;
                    }
                }

                return bIsGlobal;
            }

            string Resolve(string name)
            {
                return result.AddParameter(
                    name,
                    source: source,
                    sourceKey: sourceKey + ":" + name,
                    bIsGenerated: !IsGlobal(name));
            }

            foreach (object entry in AvatarSourceDiscovery.Items(AvatarSourceDiscovery.Get(model, "controllers")))
            {
                result.ReadController(
                    ReadAsset<RuntimeAnimatorController>(AvatarSourceDiscovery.Get(entry, "controller")),
                    Resolve);
            }

            foreach (object entry in AvatarSourceDiscovery.Items(AvatarSourceDiscovery.Get(model, "prms")))
            {
                VRCExpressionParameters asset = ReadAsset<VRCExpressionParameters>(
                    AvatarSourceDiscovery.Get(entry, "parameters"));
                if (asset == null)
                {
                    continue;
                }

                foreach (VRCExpressionParameters.Parameter parameter in asset.parameters)
                {
                    result.AddParameter(parameter.name, parameter.valueType.ToString(), parameter.defaultValue, source,
                        sourceKey + ":" + parameter.name, !IsGlobal(parameter.name), bIsDefinition: true);
                }
            }

            foreach (object entry in AvatarSourceDiscovery.Items(AvatarSourceDiscovery.Get(model, "menus")))
            {
                VRCExpressionsMenu menu = ReadAsset<VRCExpressionsMenu>(AvatarSourceDiscovery.Get(entry, "menu"));
                result.AddMenu(menu, AvatarSourceDiscovery.Get(entry, "prefix") as string ?? "", source, sourceKey, Resolve);
            }
        }

        private static T ReadAsset<T>(object wrapper) where T : UnityEngine.Object
        {
            if (AvatarSourceDiscovery.Get(wrapper, "objRef") is T asset)
            {
                return asset;
            }

            string id = AvatarSourceDiscovery.Get(wrapper, "id") as string;
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            string[] parts = id.Split(':');
            string path = AssetDatabase.GUIDToAssetPath(parts[0]);
            if (parts.Length < 2 || !long.TryParse(parts[1], out long fileId))
            {
                return AssetDatabase.LoadAssetAtPath<T>(path);
            }

            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<T>()
                .FirstOrDefault(item =>
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item, out string guid, out long localId)
                    && localId == fileId);
        }
    }
}
