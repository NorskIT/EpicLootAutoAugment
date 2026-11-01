using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using EpicLoot;
using EpicLoot.Crafting;
using EpicLoot_UnityLib;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;
using Item = ItemDrop.ItemData;

namespace EpicLootAutoAugment;

[BepInPlugin(Id, "EpicLoot Auto Augment", Version)]
[BepInDependency("randyknapp.mods.epicloot", "0.15.5")]
[BepInDependency(Jotunn.Main.ModGuid)]
[BepInDependency("norskit.epiclootcontaineraccess", BepInDependency.DependencyFlags.SoftDependency)]
[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "norskit.epiclootautoaugment", Version = "0.1.0";
    internal static Plugin Instance = null!;
    private Harmony? harmony;
    private readonly List<AugmentController> controllers = new();
    internal static readonly FieldInfo Index = AccessTools.Field(typeof(AugmentUI), "_augmentIndex");
    internal static readonly FieldInfo Dialog = AccessTools.Field(typeof(AugmentUI), "_choiceDialog");
    internal static readonly FieldInfo Progress = AccessTools.Field(typeof(EnchantingTableUIPanelBase), "_inProgress");
    internal static readonly FieldInfo Locked = AccessTools.Field(typeof(EnchantingTableUIPanelBase), "_locked");
    [ThreadStatic] private static Stack<List<MagicItemEffectDefinition>>? pools;
    [ThreadStatic] private static Stack<AugmentChoiceDialog>? dialogs;
    internal static Item? PoolItem;
    internal static int PoolIndex;
    internal static List<MagicItemEffectDefinition> Pool = new();

    private void Awake() => Instance = this;
    private void Start()
    {
        try
        {
            // Internal UI integration: fail closed on a different EpicLoot version.
            if (Chainloader.PluginInfos["randyknapp.mods.epicloot"].Metadata.Version != new System.Version(0, 15, 5))
                throw new NotSupportedException("This test build supports EpicLoot 0.15.5.");
            if (Index == null || Dialog == null || Progress == null || Locked == null)
                throw new MissingMemberException("EpicLoot augment lifecycle fields changed.");
            harmony = new Harmony(Id);
            Hook(typeof(AugmentUI), "OnAugmentIndexChanged", null, nameof(Refresh));
            Hook(typeof(EnchantingTableUIPanelBase), "OnMainButtonClicked", nameof(ManualAction), null);
            Hook(typeof(EpicLoot.CraftingV2.EnchantingUIController), "GetAvailableAugmentEffects", nameof(PoolBegin), nameof(PoolEnd), nameof(PoolFinally));
            Hook(typeof(MagicItemEffectDefinitions), "GetAvailableEffects", null, nameof(CapturePool));
            Hook(typeof(AugmentChoiceDialog), "Show", nameof(ShowBegin), null, nameof(ShowFinally));
            Hook(typeof(LootRoller), "RollAugmentEffects", null, nameof(CaptureRoll));
            Hook(typeof(AugmentChoiceDialog), "Update", nameof(DialogInput), null);
            Logger.LogInfo("Auto Augment ready. Client only; no server registration or messages.");
        }
        catch (Exception e) { harmony?.UnpatchSelf(); Logger.LogError(e); }
    }
    private void Hook(Type type, string name, string? prefix, string? postfix, string? finalizer = null, bool optional = false)
    {
        var method = AccessTools.DeclaredMethod(type, name);
        if (method == null) { if (optional) return; throw new MissingMethodException(type.FullName, name); }
        HarmonyMethod? H(string? n) => n == null ? null : new HarmonyMethod(typeof(Plugin), n) { priority = Priority.Last };
        harmony!.Patch(method, prefix: H(prefix), postfix: H(postfix), finalizer: H(finalizer));
    }
    internal static void Safe(Action action)
    {
        try { action(); }
        catch (Exception e)
        {
            Instance.Logger.LogError(e);
            foreach (var c in Instance.controllers.Where(x => x)) c.Fail();
        }
    }
    private static void Refresh(AugmentUI __instance) => Safe(() =>
    {
        var controller = __instance.GetComponent<AugmentController>();
        if (!controller)
        {
            controller = __instance.gameObject.AddComponent<AugmentController>();
            Instance.controllers.Add(controller);
            controller.Initialize(__instance);
        }
        controller.Refresh();
    });
    private static bool ManualAction(EnchantingTableUIPanelBase __instance)
    {
        var c = __instance.GetComponent<AugmentController>();
        if (!c || !c.Running || c.Issuing) return true;
        c.Stop(Texts.Get("stopped")); return false;
    }
    private static void PoolBegin() => (pools ??= new()).Push(new());
    private static void CapturePool(List<MagicItemEffectDefinition> __result)
    { if (pools?.Count > 0) { pools.Peek().Clear(); pools.Peek().AddRange(__result); } }
    private static void PoolEnd(Item __0, int __1)
    { PoolItem = __0; PoolIndex = __1; Pool = pools!.Peek().ToList(); }
    private static void PoolFinally() { if (pools?.Count > 0) pools.Pop(); }
    private static void ShowBegin(AugmentChoiceDialog __instance) => (dialogs ??= new()).Push(__instance);
    private static void ShowFinally() { if (dialogs?.Count > 0) dialogs.Pop(); }
    private static void CaptureRoll(Item __0, int __2, List<MagicItemEffect> __result)
    {
        if (dialogs?.Count > 0) Safe(() =>
        {
            foreach (var c in Instance.controllers.Where(x => x && x.Running))
                c.Offered(dialogs.Peek(), __0, __2, __result);
        });
    }
    private static void DialogInput(AugmentChoiceDialog __instance)
    {
        if (ZInput.GetButtonDown("Inventory") || ZInput.GetButtonDown("JoyButtonB") || ZInput.GetKeyDown(KeyCode.Escape, true))
            foreach (var c in Instance.controllers.Where(x => x && x.Running)) c.Stop(Texts.Get("stopped"));
    }
    private void OnDestroy()
    {
        foreach (var c in controllers.Where(x => x)) { c.Stop(Texts.Get("stopped")); Destroy(c); }
        harmony?.UnpatchSelf();
    }
}

internal static class StorageAdapter
{
    internal static bool Busy
    {
        get
        {
            if (!Chainloader.PluginInfos.TryGetValue("norskit.epiclootcontaineraccess", out var info)) return false;
            var actions = AccessTools.Field(info.Instance.GetType(), "Actions")?.GetValue(info.Instance)
                ?? throw new NotSupportedException("Cannot inspect Container Access actions.");
            var property = AccessTools.Property(actions.GetType(), "Busy")
                ?? throw new NotSupportedException("Cannot inspect Container Access busy state.");
            return (bool)property.GetValue(actions, null);
        }
    }
}
