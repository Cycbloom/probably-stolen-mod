using System;
using System.Collections.Generic;

namespace DumpingGroundMap
{
    /// <summary>Keep resolved names only for the current locale; retry missing names later.</summary>
    internal sealed class ItemNameCache
    {
        private readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
        private string locale;

        internal string Get(string currentLocale, string id, Func<string, string> resolve)
        {
            currentLocale = currentLocale ?? "";
            if (!string.Equals(locale, currentLocale, StringComparison.OrdinalIgnoreCase))
            {
                names.Clear();
                locale = currentLocale;
            }
            string name;
            if (names.TryGetValue(id, out name)) return name;
            name = resolve(id);
            if (!string.IsNullOrEmpty(name)) names[id] = name;
            return name;
        }
    }
}
