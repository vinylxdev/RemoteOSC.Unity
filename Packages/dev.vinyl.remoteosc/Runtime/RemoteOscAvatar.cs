// Vinyl - vinyl.dev

using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

// Unity 2022.3 uses C# 9, which requires block-scoped namespaces.
namespace RemoteOSC.Unity
{
    [DisallowMultipleComponent]
    [AddComponentMenu("RemoteOSC/Avatar Config Exporter")]
    public sealed class RemoteOscAvatar : MonoBehaviour, IEditorOnly
    {
        [Tooltip("Folder used by the client for avatar configs. Empty writes to Assets/.")]
        public string OutputDirectory = "";

        [Tooltip("Exact parameter names that must never be shared with peers.")]
        public List<string> BlockedParameterNames = new List<string>();

        [Tooltip("Menu/control paths to exclude, such as Clothing/Hat. These survive parameter renaming.")]
        public List<string> BlockedMenuPaths = new List<string>();
    }
}
