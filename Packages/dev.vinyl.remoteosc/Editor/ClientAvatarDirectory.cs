// Vinyl - vinyl.dev

using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RemoteOSC.Unity.Editor
{
    internal static class ClientAvatarDirectory
    {
        internal static string Find()
        {
            string dataDirectory = GetDataDirectory(
                Application.platform,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetEnvironmentVariable);
            return Find(dataDirectory);
        }

        internal static string GetDataDirectory(
            RuntimePlatform platform,
            string profile,
            string localData,
            Func<string, string> readEnvironment)
        {
            string configured = readEnvironment("REMOTEOSC_DATA_DIR");
            if (configured != null)
            {
                return configured;
            }

            // Unity's Mono folder paths can differ from the desktop client's .NET paths.
            if (platform == RuntimePlatform.LinuxEditor)
            {
                string xdg = readEnvironment("XDG_DATA_HOME");
                localData = !string.IsNullOrEmpty(xdg) && xdg.StartsWith("/", StringComparison.Ordinal)
                    ? xdg : string.IsNullOrEmpty(profile) ? localData : Path.Combine(profile, ".local", "share");
            }
            else if (platform == RuntimePlatform.OSXEditor && !string.IsNullOrEmpty(profile))
            {
                localData = Path.Combine(profile, "Library", "Application Support");
            }

            return string.IsNullOrEmpty(localData) ? null : Path.Combine(localData, "RemoteOSC");
        }

        internal static string Find(string dataDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dataDirectory))
                {
                    return null;
                }

                dataDirectory = Path.GetFullPath(dataDirectory);
                string settingsPath = Path.Combine(dataDirectory, "settings.yaml");
                string configured = File.Exists(settingsPath) ? ReadDirectorySetting(File.ReadAllText(settingsPath)) : "avatars";
                if (string.IsNullOrWhiteSpace(configured))
                {
                    return null;
                }

                string directory = Path.GetFullPath(
                    Path.IsPathRooted(configured) ? configured : Path.Combine(dataDirectory, configured));
                return Directory.Exists(directory) ? directory : null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is SecurityException || error is ArgumentException
                || error is NotSupportedException || error is FormatException)
            {
                // Detection is a picker convenience; unreadable settings must not prevent browsing.
                return null;
            }
        }

        internal static string ReadDirectorySetting(string text)
        {
            var settings = new DirectorySettings();
            if (text.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                // Unity coerces numeric JSON values into strings; the client requires a string.
                if (Regex.IsMatch(text, "\"avatarConfigDirectory\"\\s*:\\s*[^\\s\"]", RegexOptions.CultureInvariant))
                {
                    throw new FormatException("Client avatar directory must be a string.");
                }

                JsonUtility.FromJsonOverwrite(text, settings);
                return settings.avatarConfigDirectory;
            }

            if (text.Contains("\t"))
            {
                throw new FormatException("Client YAML settings cannot contain tabs.");
            }

            bool bHasDirectory = false;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || char.IsWhiteSpace(line[0]))
                    {
                        continue;
                    }

                    int colon = line.IndexOf(':');
                    if (colon < 0 || !string.Equals(
                        line.Substring(0, colon).Trim(),
                        "avatarConfigDirectory",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (bHasDirectory)
                    {
                        throw new FormatException("Duplicate client avatar directory setting.");
                    }

                    bHasDirectory = true;
                    string value = StripComment(line.Substring(colon + 1)).Trim();
                    if (value.StartsWith("\"", StringComparison.Ordinal))
                    {
                        JsonUtility.FromJsonOverwrite("{\"avatarConfigDirectory\":" + value + "}", settings);
                    }
                    else if (value.StartsWith("'", StringComparison.Ordinal))
                    {
                        if (value.Length < 2 || !value.EndsWith("'", StringComparison.Ordinal))
                        {
                            throw new FormatException("Unterminated client directory setting.");
                        }

                        settings.avatarConfigDirectory = value.Substring(1, value.Length - 2).Replace("''", "'");
                    }
                    else
                    {
                        if (value.Length == 0 || value == "null" || value == "~" || value == "true" || value == "false"
                            || "&*!|>[{".IndexOf(value[0]) >= 0
                            || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        {
                            throw new FormatException("Client avatar directory must be a string.");
                        }

                        settings.avatarConfigDirectory = value;
                    }
                }
            }

            return settings.avatarConfigDirectory;
        }

        private static string StripComment(string value)
        {
            char quote = '\0';
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (quote == '"' && current == '\\')
                {
                    index++;
                    continue;
                }

                if (current == '\'' || current == '"')
                {
                    if (quote == '\0')
                    {
                        quote = current;
                    }
                    else if (quote == current)
                    {
                        quote = '\0';
                    }
                }

                if (current == '#' && quote == '\0' && (index == 0 || char.IsWhiteSpace(value[index - 1])))
                {
                    return value.Substring(0, index);
                }
            }

            return value;
        }

        [Serializable]
        private sealed class DirectorySettings
        {
            // Match the existing client settings key without bringing its .NET runtime into Unity.
            public string avatarConfigDirectory = "avatars";
        }
    }
}
