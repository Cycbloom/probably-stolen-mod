using System;
using System.Reflection;
using DumpingGroundMap;
using HarmonyLib;

internal static class LoggingTests
{
    private const string Owner = "test.dumping-ground.logging";
    private static int checks, calls;
    private static string severity;
    private static object payload;
    private static ModLog receiver;

    internal static int Run()
    {
        var harmony = new Harmony(Owner);
        FieldInfo logger = AccessTools.Field(typeof(ModLogger), "logger");
        FieldInfo name = AccessTools.Field(typeof(ModLog), "modName");
        object original = logger.GetValue(null);
        try
        {
            // Capture the official API boundary; standalone tests have no Unity log sink.
            foreach (string method in new[] { "Log", "Warning", "Error" })
                harmony.Patch(AccessTools.Method(typeof(ModLog), method),
                    prefix: new HarmonyMethod(typeof(LoggingTests), method + "Prefix"));

            ModLogger.Info("before Init");
            Check((string)name.GetValue(receiver) == Core.Id, "pre-Init patch diagnostics use the stable mod ID");
            Check(severity == "info" && (string)payload == "before Init", "early info reaches official ModLog");

            ModLogger.Init(new ModManifest { ID = Core.Id, Name = "日志测试 / Logging Test" });
            ModLogger.Info("after Init");
            Check((string)name.GetValue(receiver) == "日志测试 / Logging Test", "Init uses the actual manifest display name");
            Check(severity == "info" && (string)payload == "after Init", "info payload is preserved without a second prefix");

            ModLogger.Warning("warning detail");
            Check(severity == "warning" && (string)payload == "warning detail", "warnings keep their severity and details");
            var error = new InvalidOperationException("outer", new Exception("inner"));
            ModLogger.Error(error);
            Check(severity == "error" && ReferenceEquals(payload, error), "error objects including inner exceptions are preserved");

            int before = calls;
            PerfLog.Line("disabled performance detail");
            Check(calls == before, "disabled performance logging does not reach the log sink");
            ModLogger.Init(new ModManifest { ID = Core.Id, Name = "Changed display name" });
            ModLogger.Info("reinitialized");
            Check((string)name.GetValue(receiver) == "Changed display name", "reinitialization cannot retain an outdated name");
            Console.WriteLine("PASS: " + checks + " logging checks (early patch diagnostics, manifest name, severity, payload and performance switch)");
            return checks;
        }
        finally
        {
            logger.SetValue(null, original);
            harmony.UnpatchAll(Owner);
        }
    }

    private static bool LogPrefix(ModLog __instance, object __0) { return Capture(__instance, __0, "info"); }
    private static bool WarningPrefix(ModLog __instance, object __0) { return Capture(__instance, __0, "warning"); }
    private static bool ErrorPrefix(ModLog __instance, object __0) { return Capture(__instance, __0, "error"); }
    private static bool Capture(ModLog instance, object message, string level)
    {
        ++calls; receiver = instance; payload = message; severity = level;
        return false;
    }
    private static void Check(bool value, string message)
    {
        ++checks;
        if (!value) throw new Exception(message);
    }
}
