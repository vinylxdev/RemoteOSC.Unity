// Vinyl - vinyl.dev

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace RemoteOSC.Unity.Editor
{
    // Capture before SDK/NDMF strips IEditorOnly components; export only after build transformations.
    public sealed class AvatarBuildCapture : IVRCSDKPreprocessAvatarCallback
    {
        internal static readonly Dictionary<int, AvatarSnapshot> Snapshots = new Dictionary<int, AvatarSnapshot>();
        public int callbackOrder => int.MinValue;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            Snapshots.Clear();
            RemoteOscAvatar[] exporters = avatarGameObject.GetComponentsInChildren<RemoteOscAvatar>(true);
            if (exporters.Length == 0)
            {
                return true;
            }

            if (exporters.Length != 1)
            {
                Debug.LogError(EditorText.Get("RemoteOSC: put exactly one exporter on the avatar root."));
                return false;
            }

            RemoteOscAvatar component = exporters[0];
            var snapshot = new AvatarSnapshot { OutputDirectory = component.OutputDirectory };
            snapshot.BlockedParameters.UnionWith(component.BlockedParameterNames);
            snapshot.BlockedMenuPaths.UnionWith(component.BlockedMenuPaths);
            VRCAvatarDescriptor descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                List<AvatarConfigExporter.ExportedControl> controls = AvatarConfigExporter.ReadControls(descriptor.expressionsMenu);
                // Retain denied bindings if a build tool moves or renames the source menu.
                foreach (AvatarConfigExporter.ExportedControl control in controls)
                {
                    if (snapshot.BlockedMenuPaths.Any(path => control.Path == path
                        || control.Path.StartsWith(path + "/", StringComparison.Ordinal)))
                    {
                        snapshot.BlockedParameters.UnionWith(control.SubParameters.Concat(new[] { control.Parameter })
                            .Where(name => !string.IsNullOrEmpty(name)));
                    }
                }

                foreach (AvatarConfigExporter.ExportedControl control in controls)
                {
                    if (snapshot.BlockedParameters.Contains(control.Parameter)
                        || control.SubParameters.Exists(parameter => snapshot.BlockedParameters.Contains(parameter)))
                    {
                        snapshot.BlockedMenuPaths.Add(control.Path);
                        foreach (string name in control.SubParameters.Concat(new[] { control.Parameter }))
                        {
                            if (!snapshot.BlockedParameters.Contains(name))
                            {
                                continue;
                            }

                            if (!snapshot.BlockedParameterPaths.TryGetValue(name, out HashSet<string> paths))
                            {
                                paths = new HashSet<string>();
                                snapshot.BlockedParameterPaths[name] = paths;
                            }

                            paths.Add(control.Path);
                        }
                    }
                }
            }

            Snapshots[avatarGameObject.GetInstanceID()] = snapshot;
            return true;
        }
    }
}
