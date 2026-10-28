// Vinyl - vinyl.dev

using System;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace RemoteOSC.Unity.Editor
{
    public sealed class AvatarBuildExport : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (AvatarBuildPreview.IsPreview(avatarGameObject))
            {
                return true;
            }

            int id = avatarGameObject.GetInstanceID();
            if (!AvatarBuildCapture.Snapshots.TryGetValue(id, out AvatarSnapshot snapshot))
            {
                return true;
            }

            AvatarBuildCapture.Snapshots.Remove(id);
            try
            {
                AvatarConfigExporter.Export(avatarGameObject, snapshot);
                return true;
            }
            catch (Exception error)
            {
                Debug.LogError(EditorText.Get("unity.export.failed", error.Message));
                return false;
            }
        }
    }
}
