// Vinyl - vinyl.dev

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace RemoteOSC.Localization
{
    // Shared with the desktop core; keep this file compatible with Unity's C# 9.
    public sealed class LocalizationCatalog
    {
        private readonly Dictionary<string, Dictionary<string, string>> translations = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> Languages { get; }
        public string Language { get; private set; } = "en";
        public IEnumerable<string> Keys => translations["en"].Keys;

        public LocalizationCatalog(IEnumerable<LanguageData> catalogs)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (LanguageData catalog in catalogs)
            {
                if (catalog == null || string.IsNullOrWhiteSpace(catalog.code) || string.IsNullOrWhiteSpace(catalog.name)
                    || catalog.entries == null)
                {
                    throw new FormatException("A localization catalog needs a language code, name and entries.");
                }

                string code = CultureInfo.GetCultureInfo(catalog.code).Name;
                if (code.Length == 0 || names.ContainsKey(code))
                {
                    throw new FormatException("Invalid or duplicate localization language: " + catalog.code);
                }

                var entries = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Entry entry in catalog.entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.key) || entry.value == null || entries.ContainsKey(entry.key))
                    {
                        throw new FormatException("Invalid or duplicate localization entry in " + code);
                    }

                    // Empty translations remain untranslated rather than hiding interface text.
                    entries.Add(entry.key, entry.value);
                }

                names.Add(code, catalog.name);
                translations.Add(code, entries);
            }

            if (!translations.ContainsKey("en"))
            {
                throw new FormatException("The English localization catalog is required.");
            }

            Languages = new ReadOnlyDictionary<string, string>(names);
        }

        public void SelectLanguage(string preference, CultureInfo systemCulture)
        {
            string requested = string.IsNullOrWhiteSpace(preference) || preference == "system" ? systemCulture.Name : preference;
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(requested);
            }
            catch (CultureNotFoundException)
            {
                Language = "en";
                return;
            }

            while (culture.Name.Length > 0)
            {
                if (translations.ContainsKey(culture.Name))
                {
                    Language = culture.Name;
                    return;
                }

                culture = culture.Parent;
            }

            Language = "en";
        }

        public string Get(string key, params object[] arguments)
        {
            string english = translations["en"].TryGetValue(key, out string? original)
                && original != null && original.Length > 0 ? original : key;
            string value = english;
            var culture = CultureInfo.GetCultureInfo(Language);
            for (CultureInfo candidate = culture; candidate.Name.Length > 0; candidate = candidate.Parent)
            {
                if (translations.TryGetValue(candidate.Name, out Dictionary<string, string>? entries)
                    && entries != null && entries.TryGetValue(key, out string? translated)
                    && translated != null && translated.Length > 0)
                {
                    value = translated;
                    break;
                }
            }

            if (arguments.Length == 0)
            {
                return value;
            }

            try
            {
                return string.Format(culture, value, arguments);
            }
            catch (FormatException) when (value != english)
            {
                // A bad translation must not break an action or suppress a safety message.
                return string.Format(CultureInfo.GetCultureInfo("en"), english, arguments);
            }
        }

        [Serializable]
        public sealed class LanguageData
        {
            // Field names are the shared JSON schema, including Unity's JsonUtility.
            public string code = "";
            public string name = "";
            public Entry[] entries = Array.Empty<Entry>();
        }

        [Serializable]
        public sealed class Entry
        {
            public string key = "";
            public string value = "";
        }
    }
}
