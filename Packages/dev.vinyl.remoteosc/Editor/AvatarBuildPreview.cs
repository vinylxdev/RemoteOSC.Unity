// Vinyl - vinyl.dev

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace RemoteOSC.Unity.Editor
{
    internal sealed class AvatarBuildPreview
    {
        private static readonly HashSet<int> previewRoots = new HashSet<int>();
        internal readonly List<AvatarConfigExporter.ExportedParameter> Parameters;
        internal readonly List<AvatarConfigExporter.ExportedControl> Controls;

        private AvatarBuildPreview(GameObject root, VRCAvatarDescriptor descriptor)
        {
            Parameters = AvatarConfigExporter.ReadParameters(root, descriptor);
            Controls = AvatarConfigExporter.ReadControls(descriptor.expressionsMenu);
        }

        internal static bool IsPreview(GameObject root)
        {
            return previewRoots.Contains(root.GetInstanceID());
        }

        internal static Hash128 GetSourceHash(GameObject source)
        {
            var state = new StringBuilder();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (UnityEngine.Object dependency in EditorUtility.CollectDependencies(new UnityEngine.Object[] { source })
                .Where(item => item != null && !(item is RemoteOscAvatar)).OrderBy(item => item.GetInstanceID()))
            {
                state.Append(dependency.GetInstanceID()).Append(':').Append(EditorUtility.GetDirtyCount(dependency)).Append('\n');
                if (dependency is GameObject || dependency is Component || dependency is ScriptableObject)
                {
                    state.Append(EditorJsonUtility.ToJson(dependency)).Append('\n');
                }

                string path = AssetDatabase.GetAssetPath(dependency);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            foreach (string path in paths.OrderBy(path => path, StringComparer.Ordinal))
            {
                state.Append(path).Append(':').Append(AssetDatabase.GetAssetDependencyHash(path)).Append('\n');
            }

            return Hash128.Compute(state.ToString());
        }

        internal static AvatarBuildPreview Create(GameObject source)
        {
            return Create(source, VRCBuildPipelineCallbacks.OnPreprocessAvatar);
        }

        internal static AvatarBuildPreview Create(GameObject source, Func<GameObject, bool> preprocess)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            GameObject clone = null;
            int id = 0;
            try
            {
                // Use the same SDK callbacks as an upload/test copy so optional build tools
                // own their transformations and asset cloning, including parameter renaming.
                clone = UnityEngine.Object.Instantiate(source);
                clone.name = source.name + "(Clone)";
                SceneManager.MoveGameObjectToScene(clone, scene);
                id = clone.GetInstanceID();
                previewRoots.Add(id);
                if (!preprocess(clone))
                {
                    throw new InvalidOperationException(EditorText.Get("unity.preview.preprocessing_failed"));
                }

                VRCAvatarDescriptor descriptor = clone.GetComponent<VRCAvatarDescriptor>();
                if (descriptor == null)
                {
                    throw new InvalidOperationException(EditorText.Get("unity.preview.descriptor_missing"));
                }

                return new AvatarBuildPreview(clone, descriptor);
            }
            finally
            {
                previewRoots.Remove(id);
                if (clone != null)
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                }

                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
