using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using DumpingGroundMap;
using HarmonyLib;

internal static class ContainerWindowScaleTests
{
    private const string Owner = "test.dumping-ground.window-scale";
    private static int checks, configReads, statusReads, infos, warnings;
    private static bool running, unavailable;
    private static ModConfig config;
    private static float position, scale, percent;
    private static Type hook;
    private static readonly List<string> order = new List<string>();

    internal static int Run()
    {
        var harmony = new Harmony(Owner);
        bool enabled = Core.Enabled;
        FieldInfo setting = AccessTools.Field(typeof(Tuning), "groundEnabledEntry");
        object oldSetting = setting.GetValue(null);
        var groundState = new Dictionary<FieldInfo, object>();
        foreach (FieldInfo field in typeof(GroundExpand).GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            if (!field.IsLiteral && !field.IsInitOnly) groundState[field] = field.GetValue(null);
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ModHelper), "IsModRunning"), prefix: Method("Running"));
            harmony.Patch(AccessTools.Method(typeof(ModHelper), "GetConfig"), transpiler: Method("ConfigBody"));
            harmony.Patch(AccessTools.Method(typeof(ModLog), "Log"), prefix: Method("Info"));
            harmony.Patch(AccessTools.Method(typeof(ModLog), "Warning"), prefix: Method("Warning"));
            ContainerWindowScaleBridge.Reset();
            running = false;
            Check(!ContainerWindowScaleBridge.ControlsGround && configReads == 0,
                "absent/stopped partner never loads its config or claims the ground");
            ConfigEntry<bool> partnerOption = Configure(true);
            running = true;
            Check(ContainerWindowScaleBridge.ControlsGround && infos == 1, "active native Ground scaling delegates layout");
            for (int i = 0; i < 20; ++i) ContainerWindowScaleBridge.ControlsGround.ToString();
            Check(infos == 1, "repeated Validate checks cannot spam handoff logs");
            partnerOption.Set(false);
            Check(!ContainerWindowScaleBridge.ControlsGround, "partner's Ground opt-out restores our layout policy");
            partnerOption.Set(true);
            Check(ContainerWindowScaleBridge.ControlsGround, "runtime config changes are read without a stale cache");
            running = false; int reads = configReads;
            Check(!ContainerWindowScaleBridge.ControlsGround && configReads == reads, "stopping partner bypasses retained config");
            running = true; Configure(null); ContainerWindowScaleBridge.Reset();
            Check(ContainerWindowScaleBridge.ControlsGround && warnings == 1, "missing key yields safely during initialization");
            Check(ContainerWindowScaleBridge.ControlsGround && warnings == 1, "missing-key warning is bounded");
            partnerOption = config.Define(ContainerWindowScaleBridge.GroundKey, false, "Ground", null, null, false);
            Detach(partnerOption);
            Check(!ContainerWindowScaleBridge.ControlsGround, "late setting registration is retried and observed");
            Configure(null);
            config.Define(ContainerWindowScaleBridge.GroundKey, "unknown", "Ground", null, null, null, false);
            Check(ContainerWindowScaleBridge.ControlsGround, "changed option type cannot override an unknown window controller");
            unavailable = true;
            Check(ContainerWindowScaleBridge.ControlsGround, "config read failure leaves the partner in control");
            unavailable = false; partnerOption = Configure(true);

            var entry = (EmporiumEntry)FormatterServices.GetUninitializedObject(typeof(EmporiumEntry));
            var ownOption = (ConfigEntry<bool>)Activator.CreateInstance(typeof(ConfigEntry<bool>),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { true }, null);
            setting.SetValue(null, ownOption); Core.Enabled = true;
            AccessTools.Field(typeof(GroundExpand), "applied").SetValue(null, true);
            AccessTools.Field(typeof(GroundExpand), "appliedOwner").SetValue(null, entry);
            GroundExpand.SetVisible(true);
            GroundExpand.Relayout(entry);
            Check(true, "owned expanded ground yields before touching Unity when partner controls it");
            int status = statusReads;
            ownOption.Set(false); GroundExpand.Relayout(entry);
            Check(statusReads == status, "Ground off bypasses even compatibility probing");
            ownOption.Set(true); Core.Enabled = false; GroundExpand.Relayout(entry);
            Check(statusReads == status, "disabled mod does not probe or touch partner layout");
            Core.Enabled = true;
            foreach (bool partnerFirst in new[] { true, false })
            foreach (Type patch in new[] { typeof(AfterhourGroundLayoutPatch), typeof(AfterhourGroundShowPatch) })
                Ordering(entry, patch, partnerFirst);

            partnerOption.Set(false);
            Check(!ContainerWindowScaleBridge.ControlsGround, "partner Ground disabled after handoff returns control");
            ContainerWindowScaleBridge.Reset(); int before = infos;
            partnerOption.Set(true);
            Check(ContainerWindowScaleBridge.ControlsGround && infos == before + 1, "reset allows a fresh handoff next session");
            Console.WriteLine("PASS: " + checks + " Container Window Scale checks (native option, handoff, both hook orders, 50/100/150% layout preservation)");
            return checks;
        }
        finally
        {
            harmony.UnpatchAll(Owner);
            Core.Enabled = enabled; setting.SetValue(null, oldSetting);
            foreach (var pair in groundState) pair.Key.SetValue(null, pair.Value);
            ContainerWindowScaleBridge.Reset(); config = null;
        }
    }

    private static void Ordering(EmporiumEntry entry, Type patch, bool partnerFirst)
    {
        hook = patch;
        var own = new Harmony(Owner + ".layout");
        var partner = new Harmony(ContainerWindowScaleBridge.HarmonyId);
        MethodInfo target = AccessTools.Method(typeof(ContainerWindowScaleTests), "LayoutProbe");
        var method = new HarmonyMethod(AccessTools.Method(patch, "Postfix"));
        Check(Array.IndexOf(method.after, ContainerWindowScaleBridge.HarmonyId) >= 0, "real hook declares partner ordering");
        method.method = AccessTools.Method(typeof(ContainerWindowScaleTests), "OwnLayout");
        Action installOwn = delegate { own.Patch(target, postfix: method); };
        Action installPartner = delegate { partner.Patch(target, postfix: Method("PartnerLayout")); };
        try
        {
            if (partnerFirst) { installPartner(); installOwn(); } else { installOwn(); installPartner(); }
            foreach (float p in new[] { 50f, 100f, 150f })
            {
                percent = p; LayoutProbe(entry);
                Check(order.Count == 3 && order[1] == "partner" && order[2] == "own", "Validate/Show ordering is independent of install order");
                Check(position == 417f && scale == p / 100f, "actual own hook preserves partner's dragged position and scale");
            }
        }
        finally { own.UnpatchAll(own.Id); partner.UnpatchAll(partner.Id); }
    }

    private static ConfigEntry<bool> Configure(bool? value)
    {
        config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.Public
            | BindingFlags.NonPublic, null, new object[] { ContainerWindowScaleBridge.ModId }, null);
        if (!value.HasValue) return null;
        var option = config.Define(ContainerWindowScaleBridge.GroundKey, value.Value, "Ground", null, null, false);
        Detach(option); return option;
    }
    private static void Detach(ConfigEntry entry) { AccessTools.Field(typeof(ConfigEntry), "<Config>k__BackingField").SetValue(entry, null); }
    private static HarmonyMethod Method(string name) { return new HarmonyMethod(typeof(ContainerWindowScaleTests), name); }
    private static bool Running(string __0, ref bool __result)
    {
        ++statusReads; __result = __0 == ContainerWindowScaleBridge.ModId && running; return false;
    }
    private static ModConfig GetConfig(string id)
    {
        ++configReads;
        if (id != ContainerWindowScaleBridge.ModId || unavailable) throw new InvalidOperationException("test config unavailable");
        return config;
    }
    private static IEnumerable<CodeInstruction> ConfigBody(IEnumerable<CodeInstruction> instructions)
    {
        yield return new CodeInstruction(OpCodes.Ldarg_0);
        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ContainerWindowScaleTests), "GetConfig"));
        yield return new CodeInstruction(OpCodes.Ret);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LayoutProbe(EmporiumEntry entry) { order.Clear(); order.Add("native"); position = 0f; scale = 1f; }
    private static void PartnerLayout() { order.Add("partner"); position = 417f; scale = percent / 100f; }
    private static void OwnLayout(EmporiumEntry __0)
    {
        order.Add("own"); AccessTools.Method(hook, "Postfix").Invoke(null, new object[] { __0 });
    }
    private static bool Info() { ++infos; return false; }
    private static bool Warning() { ++warnings; return false; }
    private static void Check(bool condition, string message) { ++checks; if (!condition) throw new Exception(message); }
}
