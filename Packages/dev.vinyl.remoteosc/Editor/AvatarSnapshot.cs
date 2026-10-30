// Vinyl - vinyl.dev

using System.Collections.Generic;

namespace RemoteOSC.Unity.Editor
{
    internal sealed class AvatarSnapshot
    {
        public string OutputDirectory = "";
        public readonly HashSet<string> BlockedParameters = new HashSet<string>();
        public readonly Dictionary<string, HashSet<string>> BlockedParameterPaths = new Dictionary<string, HashSet<string>>();
        public readonly HashSet<string> BlockedMenuPaths = new HashSet<string>();
        public readonly HashSet<string> RequiredMenuPaths = new HashSet<string>();
        public bool bHasUnresolvedSourceBlock;
    }
}
