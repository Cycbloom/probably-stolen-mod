using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using DumpingGroundMap;
using HarmonyLib;

internal static class GroundOptionTests
{
    private const string Owner = "test.dumping-ground.optional-ground";
    private static int checks;
    private static ModConfig config;

    internal static int Run()
    {
        var harmony = new Harmony(Owner);
        var tuningState = new Dictionary<FieldInfo, object>();
        foreach (FieldInfo field in typeof(Tuning).GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            if (!field.IsInitOnly && !field.IsLiteral) tuningState[field] = field.GetValue(null);
        var groundState = new Dictionary<FieldInfo, object>();
        foreach (FieldInfo field in typeof(GroundExpand).GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            if (!field.IsInitOnly && !field.IsLiteral) groundState[field] = field.GetValue(null);
        bool enabled = Core.Enabled;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ModHelper), "GetConfig"),
                transpiler: new HarmonyMethod(typeof(GroundOptionTests), "ConfigBody"));
            foreach (string method in new[] { "Log", "Warning", "Error" })
                harmony.Patch(AccessTools.Method(typeof(ModLog), method),
                    prefix: new HarmonyMethod(typeof(GroundOptionTests), "Ignore"));

            Check(!Tuning.GroundEnabled, "pre-Init defaults do not claim the Ground");
            // Register real native config entries, without reading/writing user files.
            var option = Load(null);
            Check(option != null && !option.Value && !option.Default && !Tuning.GroundEnabled,
                "missing new key in old config defaults off despite saved expanded dimensions");
            Check(option.RequiresRestart && option.Section == Tuning.GroundSection,
                "native toggle is grouped under Ground and advertises restart");
            Check(option.Label() == Copy.Get("tuning_enable_ground_expansion")
                && option.Tooltip() == Copy.Get("tuning_ground_expansion_optional_restart"),
                "native toggle uses localized label and safety instructions");
            Check(Tuning.GroundWidth == 19 && Tuning.GroundHeight == 31,
                "upgrade retains old dimension preferences without enabling expansion");
            option.Set(true);
            Check(Tuning.GroundEnabled, "native Set applies explicit opt-in");
            string serialized = option.Serialize();
            option.Reset();
            Check(!Tuning.GroundEnabled, "native Reset returns the toggle to off");
            AccessTools.Method(typeof(ConfigEntry<bool>), "TryDeserialize").Invoke(option, new object[] { serialized });
            Check(Tuning.GroundEnabled, "native boolean serialization round-trips explicit opt-in");
            option.Reset();
            Tuning.ApplyGroundPreset(16, 13);
            Check(!Tuning.GroundEnabled, "size presets cannot implicitly reenable Ground expansion");

            option = Load("true");
            Check(Tuning.GroundEnabled, "saved explicit opt-in survives config re-registration");
            option = Load("false");
            Check(!Tuning.GroundEnabled, "saved opt-out remains off");

            // These intentionally uninitialized Unity objects throw if dereferenced.
            // Disabled entry points must return before any Unity/window/grid access.
            var entry = (EmporiumEntry)FormatterServices.GetUninitializedObject(typeof(EmporiumEntry));
            Core.Enabled = true;
            AccessTools.Field(typeof(GroundExpand), "applied").SetValue(null, true);
            AccessTools.Field(typeof(GroundExpand), "appliedOwner").SetValue(null, entry);
            GroundExpand.SetVisible(true);
            GroundExpand.Apply(entry);
            GroundExpand.Relayout(entry);
            Check(true, "off bypasses resizing and relayout even for an owned visible ground");
            AccessTools.Method(typeof(AfterhourGroundShowPatch), "Postfix").Invoke(null, new object[] { entry });
            AccessTools.Method(typeof(AfterhourGroundLayoutPatch), "Postfix").Invoke(null, new object[] { entry });
            AccessTools.Method(typeof(AfterhourGroundPatch), "Postfix").Invoke(null, new object[] { entry });
            Check(!GroundExpand.Visible, "Show/Validate/Setup stay safe with expansion off; setup clears ownership");
            option.Set(true);
            GroundExpand.Relayout(entry);
            Check(true, "opt-in cannot move an unowned ground, including the furnace path");
            AccessTools.Field(typeof(GroundExpand), "applied").SetValue(null, true);
            AccessTools.Field(typeof(GroundExpand), "appliedOwner").SetValue(null,
                FormatterServices.GetUninitializedObject(typeof(EmporiumEntry)));
            GroundExpand.Relayout(entry);
            Check(true, "ownership from a different entry cannot authorize relayout");
            Core.Enabled = false;
            GroundExpand.Apply(entry);
            GroundExpand.Relayout(entry);
            Check(true, "disabled mod cannot change Ground even with opt-in saved");
            Console.WriteLine("PASS: " + checks + " optional-Ground checks (native config upgrade/apply/reset, presets, disabled hooks and ownership)");
            return checks;
        }
        finally
        {
            harmony.UnpatchAll(Owner);
            foreach (var pair in tuningState) pair.Key.SetValue(null, pair.Value);
            foreach (var pair in groundState) pair.Key.SetValue(null, pair.Value);
            Core.Enabled = enabled; config = null;
        }
    }

    private static ConfigEntry<bool> Load(string saved)
    {
        config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.Public
            | BindingFlags.NonPublic, null, new object[] { Core.Id }, null);
        var raw = (Dictionary<string, string>)AccessTools.Field(typeof(ModConfig), "rawValues").GetValue(config);
        raw[Tuning.GroundWidthKey] = "19"; raw[Tuning.GroundHeightKey] = "31";
        if (saved != null) raw[Tuning.GroundEnabledKey] = saved;
        AccessTools.Field(typeof(Tuning), "initialized").SetValue(null, false);
        AccessTools.Field(typeof(Tuning), "hasLive").SetValue(null, false);
        Tuning.Init();
        // Detach the filesystem owner; actual Unity JSON/file saving needs the game.
        foreach (ConfigEntry entry in (List<ConfigEntry>)AccessTools.Field(typeof(ModConfig), "entries").GetValue(config))
            AccessTools.Field(typeof(ConfigEntry), "<Config>k__BackingField").SetValue(entry, null);
        return (ConfigEntry<bool>)AccessTools.Field(typeof(Tuning), "groundEnabledEntry").GetValue(null);
    }
    // Bypass native config lookup; Unity config loading cannot run in standalone CLR.
    private static IEnumerable<CodeInstruction> ConfigBody(IEnumerable<CodeInstruction> instructions)
    {
        yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(GroundOptionTests), "config"));
        yield return new CodeInstruction(OpCodes.Ret);
    }
    private static bool Ignore() { return false; }
    private static void Check(bool condition, string message)
    {
        ++checks; if (!condition) throw new Exception(message);
    }
}
