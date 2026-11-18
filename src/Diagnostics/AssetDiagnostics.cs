using System;
using System.Collections.Generic;
using System.Globalization;

namespace DumpingGroundMap
{
    /// <summary>Inspect the loader's completed asset snapshot without loading files or resolving text.</summary>
    internal static class AssetDiagnostics
    {
        private static readonly string[] MapKeys =
        {
            "dumping_ground_map", "dumping_ground_map_art", "dumping_ground_map_base",
            "dumping_ground_landmarks"
        };

        internal static void OnLoaded(LoadedMod mod)
        {
            if (!Core.Enabled || mod == null || mod.Manifest == null || mod.Manifest.ID != Core.Id) return;

            var paperKeys = new List<string>();
            if (mod.PaperSprites != null)
                foreach (var item in mod.PaperSprites)
                    if (!ReferenceEquals(item.Value, null)) paperKeys.Add(item.Key);

            List<string> issues = Check(paperKeys, mod.Localization);
            foreach (string issue in issues) ModLogger.Warning("[assets] " + issue);
            ModLogger.Info(string.Format(CultureInfo.InvariantCulture,
                "[assets] load complete: paper sprites={0}, en={1}, zh={2}, warnings={3}",
                paperKeys.Count, LocaleCount(mod.Localization, "en"), LocaleCount(mod.Localization, "zh"), issues.Count));
        }

        // Pure checks are also exercised with real CSV dictionaries and damaged snapshots in tests.
        // Missing assets are diagnostic only: the existing UI and locale fallbacks keep working.
        internal static List<string> Check(IEnumerable<string> paperKeys,
            Dictionary<string, Dictionary<string, string>> localization)
        {
            var issues = new List<string>();
            var paper = new HashSet<string>(paperKeys ?? new string[0], StringComparer.Ordinal);
            var missingPaper = new List<string>();
            foreach (string key in MapKeys) CheckPaper(paper, key, missingPaper);
            foreach (Region region in Region.All) CheckPaper(paper, "dumping_ground_" + region.Id, missingPaper);
            AddIssue(issues, "missing paper sprites", missingPaper);

            Dictionary<string, string> en = CheckLocale(localization, "en", issues);
            Dictionary<string, string> zh = CheckLocale(localization, "zh", issues);
            if (en != null && zh != null)
            {
                var missingEn = new List<string>();
                var missingZh = new List<string>();
                foreach (string key in en.Keys) if (!zh.ContainsKey(key)) missingZh.Add(key);
                foreach (string key in zh.Keys) if (!en.ContainsKey(key)) missingEn.Add(key);
                AddIssue(issues, "missing Localization/en.csv keys", missingEn);
                AddIssue(issues, "missing Localization/zh.csv keys", missingZh);
            }
            return issues;
        }

        private static void CheckPaper(HashSet<string> paper, string key, List<string> missing)
        {
            if (!paper.Contains(key)) missing.Add("Textures/Paper/" + key + ".png");
        }

        private static Dictionary<string, string> CheckLocale(
            Dictionary<string, Dictionary<string, string>> localization, string locale, List<string> issues)
        {
            Dictionary<string, string> table;
            if (localization == null || !localization.TryGetValue(locale, out table) || table == null || table.Count == 0)
            {
                issues.Add("missing or empty Localization/" + locale + ".csv");
                return null;
            }
            var empty = new List<string>();
            foreach (var item in table) if (string.IsNullOrWhiteSpace(item.Value)) empty.Add(item.Key);
            AddIssue(issues, "empty Localization/" + locale + ".csv values", empty);
            return table;
        }

        private static int LocaleCount(Dictionary<string, Dictionary<string, string>> localization, string locale)
        {
            Dictionary<string, string> table;
            return localization != null && localization.TryGetValue(locale, out table) && table != null ? table.Count : 0;
        }

        private static void AddIssue(List<string> issues, string category, List<string> details)
        {
            if (details.Count == 0) return;
            details.Sort(StringComparer.Ordinal);
            int shown = Math.Min(details.Count, 5);
            issues.Add(category + " (" + details.Count.ToString(CultureInfo.InvariantCulture) + "): "
                + string.Join(", ", details.GetRange(0, shown).ToArray()) + (shown < details.Count ? ", ..." : ""));
        }
    }
}
