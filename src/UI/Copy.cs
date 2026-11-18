namespace DumpingGroundMap
{
    // Localization assets load after Init/OnEnable. Store keys in static data and
    // resolve them only when a game hook, UI callback or provider needs the text.
    internal static class Copy
    {
        internal static string Get(string key, params object[] args)
        {
            return string.IsNullOrEmpty(key) ? string.Empty : ModHelper.GetLocalized(Core.Id, key, args);
        }
    }
}
