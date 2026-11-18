namespace DumpingGroundMap
{
    /// <summary>One official ModLog for every subsystem, including early patch discovery.</summary>
    internal static class ModLogger
    {
        // ModLoader applies Harmony patches before calling Core.Init. TargetMethod
        // diagnostics must already work then; use our stable ID until Init supplies
        // the actual display name from the manifest.
        private static ModLog logger = new ModLog(new ModManifest { Name = Core.Id });

        internal static void Init(ModManifest manifest)
        {
            logger = new ModLog(manifest);
        }

        internal static void Info(object message) { logger.Log(message); }
        internal static void Warning(object message) { logger.Warning(message); }
        internal static void Error(object message) { logger.Error(message); }
    }
}
