// Vinyl - vinyl.dev

using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

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

        // Bind denied preview parameters to menus before generated names can change.
        [HideInInspector]
        public List<ParameterMenuBinding> BlockedParameterBindings = new List<ParameterMenuBinding>();

        [HideInInspector]
        public List<string> BlockedGeneratedMenuPaths = new List<string>();

        // Component references survive hierarchy edits and are remapped onto the upload clone.
        [HideInInspector]
        public List<SourceExclusion> BlockedSourceEntries = new List<SourceExclusion>();

        [Serializable]
        public sealed class SourceExclusion
        {
            public Component Source;
            public string Key = "";
        }

        [Serializable]
        public sealed class ParameterMenuBinding
        {
            public string Name = "";
            public List<string> MenuPaths = new List<string>();
        }
    }
}
