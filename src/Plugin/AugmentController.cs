using System;
using System.Collections.Generic;
using System.Linq;
using EpicLoot;
using EpicLoot.Crafting;
using EpicLoot.CraftingV2;
using EpicLoot_UnityLib;
using EpicLootAutoAugment.Core;
using UnityEngine;
using Item = ItemDrop.ItemData;

namespace EpicLootAutoAugment;

public sealed class AugmentController : MonoBehaviour
{
    private AugmentUI panel = null!;
    private AugmentView? view;
    private readonly Session session = new();
    private Item? selected;
    private int index = -1;
    private Component? table;
    private AugmentChoiceDialog? offeredDialog;
    private List<MagicItemEffect>? offers;
    private int offeredFrame, settledFrame;
    private float deadline;
    private string sourceText = "";
    private bool initialized, failed;
    internal bool Issuing { get; private set; }
    internal bool Running => session.Running;
    internal Item? CurrentItem => panel.AvailableItems.GetSingleSelectedItem<InventoryItemListElement>()?.Item1.GetItem();
    internal int CurrentIndex => (int)Plugin.Index.GetValue(panel);
    private bool Progress => (bool)Plugin.Progress.GetValue(panel);
    private bool Locked => (bool)Plugin.Locked.GetValue(panel);
    private GameObject? Dialog => Plugin.Dialog.GetValue(panel) as GameObject;

    internal void Initialize(AugmentUI augment)
    {
        panel = augment;
        try { Texts.Register(); view = new AugmentView(panel, ToggleRun); initialized = true; }
        catch { failed = true; view?.Dispose(); throw; }
    }
    internal void Refresh()
    {
        if (!initialized || failed) return;
        var item = CurrentItem;
        int slot = CurrentIndex;
        bool changed = item != selected || slot != index;
        if (changed)
        {
            Stop(Texts.Get("selection_changed"));
            selected = item; index = slot; view!.ResetTarget();
        }
        string text = panel.AvailableEffectsText.text;
        if (!changed && text == sourceText) return;
        sourceText = text;
        var definitions = item != null && Plugin.PoolItem == item && Plugin.PoolIndex == slot
            ? Plugin.Pool : new List<MagicItemEffectDefinition>();
        view!.Rebuild(definitions, text, item?.GetRarity() ?? ItemRarity.Magic);
    }
    private void ToggleRun() => Plugin.Safe(() =>
    {
        if (Running) { Stop(Texts.Get("stopped")); return; }
        Refresh();
        if (!view!.TryTarget(out var target) || selected == null || index < 0) return;
        if (Progress || Locked || Dialog || StorageAdapter.Busy) { view.Status = Texts.Get("busy"); return; }
        var magic = selected.GetMagicItem();
        if (magic == null || index >= magic.Effects.Count) return;
        var effect = magic.Effects[index];
        if (target.Matches(effect.EffectType, target.Minimum.HasValue ? DisplayValue(effect) : 0)) { view.Status = Texts.Get("already"); return; }
        table = EnchantingTableUI.instance.SourceTable;
        session.Start(target); offers = null; offeredDialog = null;
        view.SetRunning(true); view.Status = Texts.Get("searching", 0);
        deadline = Time.realtimeSinceStartup + 30f;
    });
    // EpicLoot display providers may convert a raw value; compare the primary displayed number.
    internal static float DisplayValue(MagicItemEffect effect) => EffectValues.Display(effect.EffectType, effect.EffectValue);
    internal void Offered(AugmentChoiceDialog dialog, Item item, int slot, List<MagicItemEffect> effects)
    {
        if (!Running || item != selected || slot != index) return;
        if (!session.Receive()) { Stop(Texts.Get("error")); return; }
        offeredDialog = dialog; offers = effects.ToList(); offeredFrame = Time.frameCount;
    }
    private void LateUpdate()
    {
        if (!initialized || failed) return;
        Plugin.Safe(Tick);
    }
    private void Tick()
    {
        view!.UpdateControls(Running, !Progress && !Locked && !Dialog);
        if (!Running) return;
        if (ZInput.GetKeyDown(KeyCode.Escape, true) || ZInput.GetButtonDown("Inventory") || ZInput.GetButtonDown("JoyButtonB"))
        { Stop(Texts.Get("stopped")); return; }
        if (!panel.isActiveAndEnabled || !EnchantingTableUI.instance || !EnchantingTableUI.instance.gameObject.activeInHierarchy ||
            EnchantingTableUI.instance.SourceTable != table || !Player.m_localPlayer || Player.m_localPlayer.IsDead() ||
            (Player.m_localPlayer.transform.position - table!.transform.position).sqrMagnitude > 100f)
        { Stop(Texts.Get("unavailable")); return; }
        if (CurrentItem != selected || CurrentIndex != index) { Refresh(); return; }
        if (Time.realtimeSinceStartup > deadline) { Stop(Texts.Get("timeout")); return; }
        if (session.Phase == RunPhase.ChoicesReady)
        {
            // Show is observed during payment. Do not invoke a choice until its listeners and the
            // storage transaction are ready, on a later frame after the original call unwound.
            if (Time.frameCount <= offeredFrame) return;
            if (!offeredDialog || !offeredDialog.gameObject.activeInHierarchy || Dialog != offeredDialog.gameObject || offers == null)
            { Stop(Texts.Get("stopped")); return; }
            var buttons = offeredDialog.EffectChoiceButtons;
            if (offers.Count != buttons.Count(x => x.gameObject.activeSelf)) { Stop(Texts.Get("error")); return; }
            var selectable = offers.Select((effect, i) => new { effect, i })
                .Where(x => x.i < buttons.Count && buttons[x.i].gameObject.activeSelf && buttons[x.i].interactable)
                .Select(x => new KeyValuePair<string, float>(x.effect.EffectType,
                    session.Target!.Minimum.HasValue && x.effect.EffectType == session.Target.EffectId ? DisplayValue(x.effect) : 0));
            var decision = session.Evaluate(selectable);
            if (decision == OfferDecision.Match)
            { Stop(Texts.Get("found", session.Attempts)); return; }
            if (decision == OfferDecision.Keep)
            {
                if (buttons.Count == 0 || !buttons[0].interactable) { Stop(Texts.Get("cannot_keep")); return; }
                // The native keep callback owns all item mutations and persistence.
                buttons[0].onClick.Invoke();
                settledFrame = Time.frameCount; deadline = Time.realtimeSinceStartup + 30f;
                view.Status = Texts.Get("searching", session.Attempts);
            }
            return;
        }
        if (session.Phase == RunPhase.WaitingForSettlement)
        {
            if (Dialog || Progress || Locked || StorageAdapter.Busy || Time.frameCount <= settledFrame + 2) return;
            offeredDialog = null; offers = null; session.Settled();
        }
        if (session.Phase != RunPhase.Ready || Progress || Locked || Dialog || StorageAdapter.Busy) return;
        // Refresh the current cost/affordability, rather than trusting the last attempt's button.
        panel.OnAugmentIndexChanged();
        if (!Running) return;
        if (!view.TryTarget(out var liveTarget) || liveTarget.EffectId != session.Target!.EffectId || liveTarget.Minimum != session.Target.Minimum)
        { Stop(Texts.Get("selection_changed")); return; }
        if (!panel.MainButton.interactable) { Stop(Texts.Get("materials", session.Attempts)); return; }
        if (!session.Request()) return;
        deadline = Time.realtimeSinceStartup + 30f;
        try { Issuing = true; panel.MainButton.onClick.Invoke(); }
        finally { Issuing = false; }
    }
    internal void Stop(string reason)
    {
        bool wasRunning = Running;
        session.Stop();
        if (view != null) { view.SetRunning(false); view.Status = reason; }
        // Only cancel an unpaid countdown. Never cancel/roll back an in-flight storage payment.
        if (wasRunning && panel && Progress) panel.Cancel();
    }
    internal void Fail()
    {
        if (failed) return;
        failed = true;
        session.Stop();
        try { if (panel && Progress) panel.Cancel(); }
        finally { view?.Dispose(); view = null; }
        if (Player.m_localPlayer) Player.m_localPlayer.Message(MessageHud.MessageType.Center, Texts.Get("error"));
    }
    private void OnDisable() => Stop(Texts.Get("stopped"));
    private void OnDestroy() { Stop(Texts.Get("stopped")); view?.Dispose(); }
}
