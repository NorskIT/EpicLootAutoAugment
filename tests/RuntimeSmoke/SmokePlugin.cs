using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using EpicLoot;
using EpicLoot_UnityLib;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using EpicLoot.Crafting;
using System.Net;

namespace EpicLootAutoAugment.RuntimeSmoke;

[BepInPlugin("norskit.autoaugment.smoke", "Auto Augment Isolated Test", "0.1.0")]
[BepInDependency(Plugin.Id)]
public sealed class SmokePlugin : BaseUnityPlugin
{
    private string directory = "";
    private StreamWriter log = null!;
    private readonly Harmony test = new("norskit.autoaugment.smoke");
    private static int draws;
    private static bool alwaysMiss = false;
    private static float matchValue;
    private static readonly Dictionary<string, int> paid = new();
    private static bool Focus(ref bool __result) { __result = true; return false; }
    private static bool Connect(ZNet __instance)
    {
        var coord = Environment.GetEnvironmentVariable("AUTOAUGMENT_NETWORK_COORD")!;
        var socket = ZSocket2.CreateSocket(); socket.Connect(IPAddress.Loopback, int.Parse(File.ReadAllText(Path.Combine(coord, "port"))));
        AccessTools.Method(typeof(ZNet), "Connect", new[] { typeof(ISocket) }).Invoke(__instance, new object[] { new ZSocket2(socket, "127.0.0.1") });
        return false;
    }
    // Deterministic fixture belongs ONLY to this isolated test DLL, never the distributable.
    private static bool Roll(ItemDrop.ItemData __0, MagicItem __1, int __2, ref List<MagicItemEffect> __result)
    {
        draws++;
        __result = new List<MagicItemEffect> { __1.Effects[__2], new MagicItemEffect(
            "ModifyAttackSpeed", alwaysMiss || draws == 1 ? matchValue - 1 : matchValue) };
        return false;
    }
    private static void BeforePayment(AugmentUI __instance)
    {
        if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("norskit.epiclootcontaineraccess", out var eca))
        {
            var actions = AccessTools.Field(eca.Instance.GetType(), "Actions").GetValue(eca.Instance);
            if (!(bool)AccessTools.Property(actions.GetType(), "Executing").GetValue(actions, null)) return;
        }
        var item = __instance.AvailableItems.GetSingleSelectedItem<InventoryItemListElement>().Item1.GetItem();
        var index = (int)AccessTools.Field(typeof(AugmentUI), "_augmentIndex").GetValue(__instance);
        var costs = (List<InventoryItemListElement>)AccessTools.Method(typeof(EpicLoot.CraftingV2.EnchantingUIController), "GetAugmentCost").Invoke(null, new object[] { item, index });
        foreach (var cost in costs)
        { string key = cost.GetItem().m_shared.m_name; paid[key] = (paid.TryGetValue(key, out int n) ? n : 0) + cost.GetItem().m_stack; }
    }
    private IEnumerator Start()
    {
        directory = Environment.GetEnvironmentVariable("AUTOAUGMENT_SMOKE_OUTPUT") ?? "";
        if (directory.Length == 0) yield break;
        Application.runInBackground = true;
        test.Patch(AccessTools.PropertyGetter(typeof(Application), "isFocused"), prefix: new HarmonyMethod(typeof(SmokePlugin), nameof(Focus)));
        yield return new WaitForSecondsRealtime(20);
        log = new StreamWriter(Path.Combine(directory, "cases.txt")) { AutoFlush = true };
        var run = Run();
        while (true)
        {
            bool next;
            try { next = run.MoveNext(); }
            catch (Exception e) { log.WriteLine("FAIL " + e); File.WriteAllText(Path.Combine(directory, "result.txt"), "FAIL\n" + e); break; }
            if (!next) { File.WriteAllText(Path.Combine(directory, "result.txt"), "PASS\nSee cases.txt. Disposable local world; no production files used."); break; }
            yield return run.Current;
        }
        log.Dispose(); Application.Quit();
        var coord = Environment.GetEnvironmentVariable("AUTOAUGMENT_NETWORK_COORD");
        if (!string.IsNullOrEmpty(coord)) File.WriteAllText(Path.Combine(coord, "client-done"), "done");
    }
    private void Check(bool ok, string description)
    { if (!ok) throw new Exception(description); log.WriteLine("PASS " + description); }
    private IEnumerator Run()
    {
        var patches = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(Plugin.Id)).ToArray();
        Check(patches.Length == 7, "Seven lifecycle hooks attached to actual EpicLoot 0.15.5");
        var profile = new PlayerProfile("autoaugment_isolated", FileHelpers.FileSource.Local);
        profile.SetName("Auto Augment test"); profile.m_firstSpawn = false; profile.Save();
        Game.SetProfile(profile.GetFilename(), profile.m_fileSource);
        ZNet.m_onlineBackend = OnlineBackendType.CustomSocket;
        string coord = Environment.GetEnvironmentVariable("AUTOAUGMENT_NETWORK_COORD") ?? "";
        if (coord.Length == 0)
        {
            ZNet.SetServer(true, false, false, "Auto Augment isolated", "", new World("AutoAugmentIsolated", "AutoAugmentIsolated"));
            ZNet.ResetServerHost();
        }
        else
        {
            float waitPort = Time.realtimeSinceStartup + 180;
            while (!File.Exists(Path.Combine(coord, "port")))
            { if (Time.realtimeSinceStartup > waitPort) throw new Exception("Host port timeout"); yield return new WaitForSecondsRealtime(.2f); }
            test.Patch(AccessTools.Method(typeof(ZNet), "ClientConnect"), prefix: new HarmonyMethod(typeof(SmokePlugin), nameof(Connect)));
            ZNet.SetServer(false, false, false, "", "", null);
            ZNet.SetServerHost("127.0.0.1", int.Parse(File.ReadAllText(Path.Combine(coord, "port"))), OnlineBackendType.CustomSocket);
        }
        AccessTools.Method(typeof(FejdStartup), "LoadMainScene").Invoke(FejdStartup.instance, null);
        float deadline = Time.realtimeSinceStartup + 180;
        while (!Player.m_localPlayer || Player.m_localPlayer.InCutscene())
        { if (Time.realtimeSinceStartup > deadline) throw new Exception("World load timeout"); yield return new WaitForSecondsRealtime(.2f); }
        yield return new WaitForSecondsRealtime(2);
        if (coord.Length > 0) Check(!ZNet.instance.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected, "Joined EpicLoot host without Auto Augment installed on host");
        var player = Player.m_localPlayer;
        player.SetGodMode(true); player.GetComponent<PlayerController>().enabled = false;
        player.GetComponent<Rigidbody>().useGravity = false; player.GetInventory().RemoveAll();
        var table = Instantiate(ZNetScene.instance.GetPrefab("piece_enchantingtable"), player.transform.position + Vector3.forward, Quaternion.identity).GetComponent<EnchantingTable>();
        yield return null;
        Container? materialChest = null;
        if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("norskit.epiclootcontaineraccess"))
        {
            materialChest = Instantiate(ZNetScene.instance.GetPrefab("piece_chest_wood"), player.transform.position + Vector3.right, Quaternion.identity).GetComponent<Container>();
            yield return null;
        }
        table.SetFeatureLevel(EnchantingFeature.Augment, 5);
        var prefab = ObjectDB.instance.GetItemPrefab("SwordIron");
        var sword = prefab.GetComponent<ItemDrop>().m_itemData.Clone(); sword.m_dropPrefab = prefab;
        EpicLoot.API.ApplyMagicItemJson(sword, "{\"Rarity\":3,\"Effects\":[{\"EffectType\":\"ModifyDamage\",\"EffectValue\":10},{\"EffectType\":\"ModifyAttackStaminaUse\",\"EffectValue\":10}]}");
        player.GetInventory().AddItem(sword);
        EnchantingTableUI.Show(table);
        var ui = EnchantingTableUI.instance;
        foreach (var p in ui.Panels) p.gameObject.SetActive(p is AugmentUI);
        var panel = ui.Panels.OfType<AugmentUI>().Single(); panel.Unlock();
        panel.AvailableItems.SetItems(new List<IListElement> { new InventoryItemListElement { Item = sword } });
        panel.AvailableItems.ForeachElement((_, row) => row.SelectQuantity(1, true));
        panel.SelectAugmentIndex(0);
        yield return null;
        var controller = panel.GetComponent<AugmentController>();
        Check(controller, "Controller mounted on native augment panel");
        var rowToggles = panel.GetComponentsInChildren<Toggle>(true).Where(t => t.name.StartsWith("Effect_")).ToArray();
        Check(rowToggles.Length > 5, "Available effects rendered as native selectable rows");
        var numeric = rowToggles.First(t => t.name == "Effect_ModifyAttackSpeed"); numeric.isOn = true;
        var minToggle = panel.GetComponentsInChildren<Toggle>(true).Single(t => t.name == "MinimumEnabled");
        var minInput = panel.GetComponentsInChildren<TMP_InputField>(true).Single(t => t.name == "MinimumValue");
        Check(minToggle.interactable && !minToggle.isOn, "Minimum is optional for numeric effects");
        minToggle.isOn = true;
        Check(minInput.interactable, "Checking minimum enables native numeric field");
        minInput.text = "999999";
        yield return null;
        var start = panel.GetComponentsInChildren<Button>(true).Single(b => b.name == "AutoAugmentButton");
        Check(!start.interactable, "Unreachable threshold prevents spending");
        minToggle.isOn = false;
        var valueless = rowToggles.FirstOrDefault(t => t.name == "Effect_Indestructible");
        Check(valueless != null, "Indestructible is available");
        valueless!.isOn = true;
        Check(!minToggle.interactable && !minInput.interactable, "Valueless effect disables numeric controls");
        numeric.isOn = true;
        yield return new WaitForSecondsRealtime(.5f);
        var screenshot = Capture(ui, "augment-1080.png", 1920, 1080, 1f);
        while (screenshot.MoveNext()) yield return screenshot.Current;
        start.onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
        var status = panel.GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "Status");
        Check(status.text.Contains("Not enough materials"), "No materials stops without opening a paid roll");
        Check(!((GameObject)AccessTools.Field(typeof(AugmentUI), "_choiceDialog").GetValue(panel)), "No dialog generated without payment");
        // Give exact ordinary costs for several attempts, using the same cost API as the native panel.
        var costs = (List<InventoryItemListElement>)AccessTools.Method(typeof(EpicLoot.CraftingV2.EnchantingUIController), "GetAugmentCost").Invoke(null, new object[] { sword, 0 });
        var paymentInventory = materialChest ? materialChest.GetInventory() : player.GetInventory();
        foreach (var cost in costs)
        {
            var item = cost.GetItem().Clone(); item.m_stack *= 50; paymentInventory.AddItem(item);
        }
        if (materialChest)
        {
            AccessTools.Method(typeof(Container), "Save").Invoke(materialChest, null);
            var eca = BepInEx.Bootstrap.Chainloader.PluginInfos["norskit.epiclootcontaineraccess"].Instance;
            var storage = AccessTools.Field(eca.GetType(), "Storage").GetValue(eca);
            AccessTools.Method(storage.GetType(), "Invalidate").Invoke(storage, null);
            yield return new WaitForSecondsRealtime(.5f);
        }
        var before = costs.ToDictionary(c => c.GetItem().m_shared.m_name, c => paymentInventory.CountItems(c.GetItem().m_shared.m_name));
        var speed = MagicItemEffectDefinitions.Get("ModifyAttackSpeed").GetValuesForRarity(sword.GetRarity());
        matchValue = speed.MaxValue; minToggle.isOn = true; minInput.text = matchValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        test.Patch(AccessTools.Method(typeof(LootRoller), "RollAugmentEffects"), prefix: new HarmonyMethod(typeof(SmokePlugin), nameof(Roll)));
        test.Patch(AccessTools.DeclaredMethod(typeof(AugmentUI), "DoMainAction"), prefix: new HarmonyMethod(typeof(SmokePlugin), nameof(BeforePayment)));
        panel.OnAugmentIndexChanged();
        start.onClick.Invoke();
        deadline = Time.realtimeSinceStartup + 12;
        while (!status.text.Contains("Effect found"))
        { if (Time.realtimeSinceStartup > deadline) throw new Exception("Search failed: " + status.text + "; draws=" + draws); yield return null; }
        Check(draws == 2, "Low roll discarded once, matching minimum stops on second paid roll");
        Check(status.text.Contains("2 attempts"), "Match status preserves attempt count");
        yield return new WaitForSecondsRealtime(1.5f);
        Check(draws == 2, "No extra roll after match");
        var dialog = ((GameObject)AccessTools.Field(typeof(AugmentUI), "_choiceDialog").GetValue(panel)).GetComponent<AugmentChoiceDialog>();
        Check(dialog.gameObject.activeSelf, "Native match dialog remains open");
        Check(sword.GetMagicItem().Effects[0].EffectType == "ModifyDamage", "Match is not automatically applied");
        foreach (var entry in before)
            Check(paymentInventory.CountItems(entry.Key) == entry.Value - paid[entry.Key], "Exact normal costs deducted once per roll" + (materialChest ? " from chest: " : ": ") + entry.Key);
        screenshot = Capture(ui, "augment-match.png", 1920, 1080, 1f);
        while (screenshot.MoveNext()) yield return screenshot.Current;
        dialog.EffectChoiceButtons[1].onClick.Invoke();
        yield return new WaitForSecondsRealtime(.3f);
        Check(sword.GetMagicItem().Effects[0].EffectType == "ModifyAttackSpeed", "Native manual selection applies matching effect");
        int count = draws;
        start.onClick.Invoke(); yield return null;
        Check(draws == count && status.text.Contains("already meets"), "Existing matching slot costs nothing");
        screenshot = Capture(ui, "augment-1440.png", 2560, 1440, 1f);
        while (screenshot.MoveNext()) yield return screenshot.Current;
        screenshot = Capture(ui, "augment-large-ui.png", 1920, 1080, 1.2f);
        while (screenshot.MoveNext()) yield return screenshot.Current;
        // Restore the fixture's old effect, then cancel an unpaid countdown.
        EpicLoot.API.ApplyMagicItemJson(sword, "{\"Rarity\":3,\"Effects\":[{\"EffectType\":\"ModifyDamage\",\"EffectValue\":10},{\"EffectType\":\"ModifyAttackStaminaUse\",\"EffectValue\":10}]}");
        panel.OnAugmentIndexChanged();
        count = draws; paid.Clear();
        start.onClick.Invoke(); yield return null;
        start.onClick.Invoke(); yield return new WaitForSecondsRealtime(1.1f);
        Check(draws == count && paid.Count == 0, "Stop during unpaid countdown prevents any payment or roll");
        // Exactly one attempt's materials, and a guaranteed miss, must end cleanly at exhaustion.
        costs = (List<InventoryItemListElement>)AccessTools.Method(typeof(EpicLoot.CraftingV2.EnchantingUIController), "GetAugmentCost").Invoke(null, new object[] { sword, 0 });
        foreach (var cost in costs)
        {
            string name = cost.GetItem().m_shared.m_name;
            foreach (var item in paymentInventory.GetAllItems().Where(x => x.m_shared.m_name == name).ToArray()) paymentInventory.RemoveItem(item);
            paymentInventory.AddItem(cost.GetItem().Clone());
        }
        if (materialChest)
        {
            AccessTools.Method(typeof(Container), "Save").Invoke(materialChest, null);
            var eca = BepInEx.Bootstrap.Chainloader.PluginInfos["norskit.epiclootcontaineraccess"].Instance;
            var storage = AccessTools.Field(eca.GetType(), "Storage").GetValue(eca);
            AccessTools.Method(storage.GetType(), "Invalidate").Invoke(storage, null);
            yield return new WaitForSecondsRealtime(.5f);
        }
        alwaysMiss = true; count = draws; panel.OnAugmentIndexChanged(); start.onClick.Invoke();
        deadline = Time.realtimeSinceStartup + 12;
        while (!status.text.Contains("Not enough materials"))
        { if (Time.realtimeSinceStartup > deadline) throw new Exception("Exhaustion failed: " + status.text); yield return null; }
        Check(draws == count + 1, "Exactly one remaining paid miss stops at material exhaustion");
        foreach (var cost in costs) Check(paymentInventory.CountItems(cost.GetItem().m_shared.m_name) == 0, "Last affordable roll uses exact remaining materials");
        Check(sword.GetMagicItem().Effects[0].EffectType == "ModifyDamage", "Miss/exhaustion preserves original effect");
        var conditional = panel.GetComponentsInChildren<Toggle>(true).First(t => t.name == "Effect_ModifyAttackSpeedLowHealth");
        conditional.isOn = true;
        var filter = panel.GetComponentsInChildren<TMP_InputField>(true).Single(t => t.name != "MinimumValue" && t.transform.parent.name == "AutoAugmentSearch");
        filter.text = "below";
        yield return null;
        Check(conditional.gameObject.activeSelf, "Effect filter retains matching conditional effect");
        var label = conditional.GetComponentInChildren<TMP_Text>(true); label.ForceMeshUpdate();
        Check(label.textInfo.lineCount > 1, "Long conditional row wraps across multiple lines");
        var rect = (RectTransform)conditional.transform;
        Check(rect.rect.height >= label.preferredHeight, "Wrapped row reserves sufficient height");
        var hitPoint = rect.TransformPoint(new Vector3(rect.rect.xMax - 3, rect.rect.center.y, 0));
        var canvas = conditional.GetComponentInParent<Canvas>().rootCanvas;
        var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
        { position = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, hitPoint) };
        var hits = new List<UnityEngine.EventSystems.RaycastResult>();
        UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Any(h => h.gameObject == conditional.gameObject), "Full row, including empty right edge, receives pointer raycasts");
        screenshot = Capture(ui, "augment-conditional.png", 1920, 1080, 1f);
        while (screenshot.MoveNext()) yield return screenshot.Current;
        Check(!Harmony.GetAllPatchedMethods().Any(m => m.DeclaringType == typeof(ZNet) && Harmony.GetPatchInfo(m).Owners.Contains(Plugin.Id)), "No ZNet hooks or server compatibility enforcement");
        log.WriteLine("Deterministic roll fixture exists only in excluded test DLL; native payment and callbacks were exercised.");
    }

    // Batch mode skips normal display presentation. Render the actual native Canvas with a test
    // camera into a texture; this is a runtime UI capture, not a reconstructed mockup.
    private IEnumerator Capture(EnchantingTableUI ui, string file, int width, int height, float scale)
    {
        var canvas = ui.GetComponentInParent<Canvas>().rootCanvas;
        var mode = canvas.renderMode; var originalCamera = canvas.worldCamera; float distance = canvas.planeDistance;
        var scaler = canvas.GetComponent<CanvasScaler>();
        var guiScaler = canvas.GetComponent<GuiScaler>(); bool guiEnabled = guiScaler && guiScaler.enabled;
        if (guiScaler) guiScaler.enabled = false;
        var scalerMode = scaler.uiScaleMode; float factor = scaler.scaleFactor;
        var cameraObject = new GameObject("AutoAugmentCaptureCamera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .07f, .055f, 1);
        camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
        var texture = new RenderTexture(width, height, 24); camera.targetTexture = texture;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = scale * height / 1080f;
        Canvas.ForceUpdateCanvases(); yield return null; Canvas.ForceUpdateCanvases();
        camera.Render();
        var active = RenderTexture.active; RenderTexture.active = texture;
        var bitmap = new Texture2D(width, height, TextureFormat.RGB24, false);
        bitmap.ReadPixels(new Rect(0,0,width,height),0,0); bitmap.Apply();
        File.WriteAllBytes(Path.Combine(directory, file), bitmap.EncodeToPNG());
        RenderTexture.active = active;
        canvas.renderMode = mode; canvas.worldCamera = originalCamera; canvas.planeDistance = distance;
        scaler.uiScaleMode = scalerMode; scaler.scaleFactor = factor;
        if (guiScaler) guiScaler.enabled = guiEnabled;
        Destroy(bitmap); Destroy(cameraObject); texture.Release(); Destroy(texture);
    }
}
