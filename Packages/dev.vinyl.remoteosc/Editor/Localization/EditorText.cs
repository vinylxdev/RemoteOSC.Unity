// Vinyl - vinyl.dev

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RemoteOSC.Localization;
using UnityEditor;
using UnityEngine;

namespace RemoteOSC.Unity.Editor
{
    internal static class EditorText
    {
        private const string PreferenceKey = "RemoteOSC.Language";
        private const string CatalogDirectory = "Packages/dev.vinyl.remoteosc/Editor/Localization";
        private static LocalizationCatalog catalog;

        internal static string Get(string source, params object[] arguments)
        {
            EnsureCatalog();
            return catalog.Get(source, arguments);
        }

        internal static void DrawLanguageSelector()
        {
            EnsureCatalog();
            string[] codes = new[] { "system" }.Concat(catalog.Languages.Keys.OrderBy(code => code, StringComparer.Ordinal)).ToArray();
            string[] names = codes.Select(code => code == "system" ? Get("System default") : catalog.Languages[code]).ToArray();
            int current = Math.Max(0, Array.IndexOf(codes, EditorPrefs.GetString(PreferenceKey, "system")));
            int selected = EditorGUILayout.Popup(Get("Language"), current, names);
            if (selected != current)
            {
                EditorPrefs.SetString(PreferenceKey, codes[selected]);
                catalog.SelectLanguage(codes[selected], CultureInfo.CurrentUICulture);
            }
        }

        private static void EnsureCatalog()
        {
            if (catalog != null)
            {
                return;
            }

            var languages = new List<LocalizationCatalog.LanguageData>();
            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { CatalogDirectory }).OrderBy(value => value, StringComparer.Ordinal))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    languages.Add(JsonUtility.FromJson<LocalizationCatalog.LanguageData>(asset.text));
                }
            }

            catalog = new LocalizationCatalog(languages);
            catalog.SelectLanguage(EditorPrefs.GetString(PreferenceKey, "system"), CultureInfo.CurrentUICulture);
        }
    }
}
