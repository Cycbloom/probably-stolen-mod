namespace DumpingGroundMap
{
    /// <summary>Contents are real nested items, matching native prebuilt supply boxes.</summary>
    internal static class WaterSupplyPack
    {
        internal const string HalfTest = "half_water_test_strip_box";
        internal const string HalfTablets = "half_water_purification_tablet_box";

        internal static string ContainerId(string token)
        {
            if (token == HalfTest || token == "full_water_test_strip_box") return "water_test_container";
            if (token == HalfTablets || token == "full_water_purification_tablet_box") return "water_tablet_container";
            return null;
        }

        internal static int Count(string token)
        {
            if (token == HalfTest || token == HalfTablets) return 5;
            if (token == "full_water_test_strip_box") return 10;
            if (token == "full_water_purification_tablet_box") return 9;
            return 0;
        }

        internal static GameItem Create(string token)
        {
            string container = ContainerId(token);
            if (container == null) return null;
            GameItem box = ItemSpawner.Spawn(container);
            if (box == null) return null;
            string content = container == "water_test_container" ? "water_test_strip" : "water_tablet";
            for (int i = 0; i < Count(token); ++i)
            {
                GameItem item = ItemSpawner.Spawn(content);
                if (item == null) throw new System.InvalidOperationException("Could not fill water supply pack: " + token);
                if (GraphUtils.TryAcceptAll(box, item, -1) != 0)
                    throw new System.InvalidOperationException("Water supply pack rejected its contents: " + token);
            }
            return box;
        }
    }
}
