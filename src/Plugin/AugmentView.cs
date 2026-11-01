using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using EpicLoot;
using EpicLoot_UnityLib;
using EpicLootAutoAugment.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EpicLootAutoAugment;

internal sealed class AugmentView : IDisposable
{
    private const float FooterHeight = 190f, SearchHeight = 32f;
    private readonly AugmentUI panel;
    private readonly ScrollRect scroll;
    private readonly RectTransform scrollTransform, originalContent;
    private readonly Vector2 originalMin, originalMax;
    private readonly bool originalTextActive;
    private readonly List<GameObject> owned = new();
    private readonly List<(MagicItemEffectDefinition Definition, Toggle Toggle)> rows = new();
    private RectTransform content = null!;
    private TMP_Text selectedLabel = null!, status = null!, preview = null!;
    private TMP_InputField filter = null!, minimum = null!;
    private Toggle minimumEnabled = null!;
    private Button run = null!;
    private MagicItemEffectDefinition? target;
    private ItemRarity rarity;
    private bool running, rebuilding;
    private RectTransform? externalScrollbar;
    private Vector2 scrollbarMin, scrollbarMax;

    internal string Status { set { if (status) status.text = value; } }
    internal AugmentView(AugmentUI augment, Action toggleRun)
    {
        panel = augment;
        scroll = panel.AvailableEffectsText.GetComponentInParent<ScrollRect>(true)
            ?? throw new InvalidOperationException("Available Effects scroll view not found.");
        originalContent = scroll.content;
        scrollTransform = (RectTransform)scroll.transform;
        originalMin = scrollTransform.offsetMin; originalMax = scrollTransform.offsetMax;
        originalTextActive = panel.AvailableEffectsText.gameObject.activeSelf;
        try { Build(toggleRun); }
        catch { Dispose(); throw; }
    }
    private void Build(Action toggleRun)
    {
        if (!panel.AvailableItems.FilterByText || !panel.EnchantmentListPrefab.GetComponent<Toggle>() || !panel.MainButton)
            throw new InvalidOperationException("EpicLoot UI templates unavailable.");
        // Keep EpicLoot's existing viewport/mask/background and scrollbar, replacing only its content.
        content = Rect("AutoAugmentEffects", originalContent.parent);
        owned.Add(content.gameObject);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1); content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true; layout.childForceExpandWidth = true;
        layout.childControlHeight = true; layout.childForceExpandHeight = false;
        layout.spacing = 1f; layout.padding = new RectOffset(3, 5, 3, 3);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        panel.AvailableEffectsText.gameObject.SetActive(false);
        scroll.content = content;
        scrollTransform.offsetMin = originalMin + new Vector2(0, FooterHeight + 6);
        scrollTransform.offsetMax = originalMax - new Vector2(0, SearchHeight + 4);
        if (scroll.verticalScrollbar && !scroll.verticalScrollbar.transform.IsChildOf(scrollTransform))
        {
            externalScrollbar = (RectTransform)scroll.verticalScrollbar.transform;
            scrollbarMin = externalScrollbar.offsetMin; scrollbarMax = externalScrollbar.offsetMax;
            externalScrollbar.offsetMin += new Vector2(0, FooterHeight + 6);
            externalScrollbar.offsetMax -= new Vector2(0, SearchHeight + 4);
        }
        var footer = Rect("AutoAugmentControls", scrollTransform.parent); owned.Add(footer.gameObject);
        footer.anchorMin = scrollTransform.anchorMin;
        footer.anchorMax = new Vector2(scrollTransform.anchorMax.x, scrollTransform.anchorMin.y);
        footer.offsetMin = originalMin;
        footer.offsetMax = new Vector2(originalMax.x, originalMin.y + FooterHeight);

        var searchRoot = Rect("AutoAugmentSearch", scrollTransform.parent); owned.Add(searchRoot.gameObject);
        searchRoot.anchorMin = new Vector2(scrollTransform.anchorMin.x, scrollTransform.anchorMax.y);
        searchRoot.anchorMax = scrollTransform.anchorMax;
        searchRoot.offsetMin = new Vector2(originalMin.x + 6, originalMax.y - SearchHeight);
        searchRoot.offsetMax = new Vector2(originalMax.x - 6, originalMax.y);
        filter = Input(searchRoot, Texts.Get("filter")); Fill((RectTransform)filter.transform, 0, 0, 0, 0);
        filter.onValueChanged.AddListener(_ => ApplyFilter());

        selectedLabel = Label(footer, "Target"); Top(selectedLabel.rectTransform, 6, 6, 2, 42);
        selectedLabel.color = panel.AvailableEffectsHeader.color;
        minimumEnabled = Object.Instantiate(panel.EnchantmentListPrefab, footer).GetComponent<Toggle>();
        minimumEnabled.name = "MinimumEnabled"; minimumEnabled.gameObject.SetActive(true);
        minimumEnabled.group = null; minimumEnabled.onValueChanged = new Toggle.ToggleEvent();
        minimumEnabled.SetIsOnWithoutNotify(false);
        var toggleLabel = minimumEnabled.GetComponentInChildren<TMP_Text>(true);
        toggleLabel.text = Texts.Get("minimum"); toggleLabel.fontSize = panel.AvailableEffectsText.fontSize;
        toggleLabel.color = panel.AvailableEffectsHeader.color;
        Top((RectTransform)minimumEnabled.transform, 6, 0, 48, 30);
        ((RectTransform)minimumEnabled.transform).anchorMax = new Vector2(.62f, 1);
        minimumEnabled.onValueChanged.AddListener(_ => UpdateMinimum());

        minimum = Input(footer, "0"); minimum.name = "MinimumValue";
        Top((RectTransform)minimum.transform, 0, 6, 48, 30);
        ((RectTransform)minimum.transform).anchorMin = new Vector2(.64f, 1);
        minimum.characterLimit = 12;
        minimum.onValueChanged.AddListener(_ => UpdateMinimum());

        preview = Label(footer, "MinimumPreview"); Top(preview.rectTransform, 6, 6, 82, 38);
        status = Label(footer, "Status"); Top(status.rectTransform, 6, 6, 122, 26);
        status.fontSize = panel.AvailableEffectsText.fontSize;
        run = Object.Instantiate(panel.MainButton, footer); run.name = "AutoAugmentButton";
        run.onClick = new Button.ButtonClickedEvent(); run.onClick.AddListener(() => toggleRun());
        foreach (Transform child in run.transform)
            if (child.name.StartsWith("Hint", StringComparison.Ordinal) || child.name == "ButtonFocus") child.gameObject.SetActive(false);
        Top((RectTransform)run.transform, 6, 6, 150, 38);
        SetButtonText(Texts.Get("start"));
        run.gameObject.SetActive(true);
        ResetTarget();
    }
    private TMP_Text Label(Transform parent, string name)
    {
        var label = Object.Instantiate(panel.AvailableEffectsHeader, parent);
        label.name = name; label.gameObject.SetActive(true); label.text = "";
        label.fontSize = panel.AvailableEffectsText.fontSize; label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.TopLeft; label.raycastTarget = false;
        return label;
    }
    private TMP_InputField Input(Transform parent, string placeholder)
    {
        var input = Object.Instantiate(panel.AvailableItems.FilterByText, parent);
        input.gameObject.SetActive(true); input.onValueChanged = new TMP_InputField.OnChangeEvent();
        input.onEndEdit = new TMP_InputField.SubmitEvent(); input.onSubmit = new TMP_InputField.SubmitEvent();
        input.contentType = TMP_InputField.ContentType.Standard; input.lineType = TMP_InputField.LineType.SingleLine;
        input.SetTextWithoutNotify(""); input.interactable = true;
        if (input.placeholder is TMP_Text text) text.text = placeholder;
        return input;
    }
    internal void Rebuild(List<MagicItemEffectDefinition> definitions, string nativeText, ItemRarity itemRarity)
    {
        rarity = itemRarity;
        // Preserve EpicLoot's exact labels, including rarity decorations and conditional health text.
        string prefix = "", body = nativeText;
        if (body.StartsWith("<color=", StringComparison.Ordinal) && body.EndsWith("</color>", StringComparison.Ordinal))
        { int end = body.IndexOf('>') + 1; prefix = body.Substring(0, end); body = body.Substring(end, body.Length - end - 8); }
        var labels = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length != definitions.Count)
            throw new InvalidOperationException("Available effect labels do not match the captured effect pool.");
        foreach (var row in rows) { row.Toggle.gameObject.SetActive(false); Object.Destroy(row.Toggle.gameObject); }
        rows.Clear(); rebuilding = true;
        try
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                var row = Object.Instantiate(panel.EnchantmentListPrefab, content).GetComponent<Toggle>();
                row.gameObject.name = "Effect_" + definition.Type; row.gameObject.SetActive(true);
                // A transparent root graphic makes the full wrapped row clickable, not only its checkbox.
                var hitArea = row.gameObject.GetComponent<Image>() ?? row.gameObject.AddComponent<Image>();
                if (!hitArea.sprite) hitArea.color = Color.clear;
                hitArea.raycastTarget = true;
                row.group = null; row.onValueChanged = new Toggle.ToggleEvent();
                var label = row.GetComponentInChildren<TMP_Text>(true);
                label.text = prefix + labels[i].TrimStart('‣', ' ') + (prefix.Length > 0 ? "</color>" : "");
                label.fontSize = panel.AvailableEffectsText.fontSize; label.enableAutoSizing = false;
                label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Overflow;
                // Native row fitting computes wrapped height from the current viewport width.
                EffectListRow.Fit(row.gameObject, label);
                row.SetIsOnWithoutNotify(target?.Type == definition.Type);
                row.interactable = !running && definition.SelectionWeight > 0;
                row.onValueChanged.AddListener(on =>
                {
                    if (rebuilding || running) return;
                    if (on) Select(definition); else if (target?.Type == definition.Type) row.SetIsOnWithoutNotify(true);
                });
                rows.Add((definition, row));
            }
        }
        finally { rebuilding = false; }
        if (target != null)
        {
            target = definitions.FirstOrDefault(x => x.Type == target.Type && x.SelectionWeight > 0);
            if (target == null) ResetTarget(); else UpdateMinimum();
        }
        ApplyFilter();
    }
    private void ApplyFilter()
    {
        string query = filter.text.Trim();
        foreach (var row in rows)
        {
            string label = Regex.Replace(row.Toggle.GetComponentInChildren<TMP_Text>(true).text, "<[^>]*>", "");
            row.Toggle.gameObject.SetActive(query.Length == 0 || label.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }
        LayoutRebuilder.MarkLayoutForRebuild(content); scroll.verticalNormalizedPosition = 1f;
    }
    private void Select(MagicItemEffectDefinition definition)
    {
        target = definition;
        foreach (var row in rows) row.Toggle.SetIsOnWithoutNotify(row.Definition.Type == definition.Type);
        minimumEnabled.SetIsOnWithoutNotify(false);
        var values = definition.GetValuesForRarity(rarity);
        minimum.SetTextWithoutNotify(values == null ? "" : EffectValues.Display(definition.Type, values.MinValue).ToString("0.###", CultureInfo.CurrentCulture));
        Status = ""; UpdateMinimum();
    }
    internal void ResetTarget()
    {
        target = null;
        if (!minimumEnabled) return;
        minimumEnabled.SetIsOnWithoutNotify(false); minimum.SetTextWithoutNotify("");
        filter.SetTextWithoutNotify(""); Status = "";
        foreach (var row in rows) row.Toggle.SetIsOnWithoutNotify(false);
        UpdateMinimum();
    }
    private void UpdateMinimum()
    {
        var values = target?.GetValuesForRarity(rarity);
        selectedLabel.text = target == null ? Texts.Get("choose") : MagicItem.GetEffectTextRange(target, values!);
        minimumEnabled.interactable = !running && values != null;
        minimum.interactable = !running && values != null && minimumEnabled.isOn;
        preview.text = target != null && values == null ? Texts.Get("valueless") : "";
        if (values != null && minimumEnabled.isOn)
        {
            float a = EffectValues.Display(target!.Type, values.MinValue), b = EffectValues.Display(target.Type, values.MaxValue);
            bool valid = Target.TryMinimum(minimum.text, Math.Min(a, b), Math.Max(a, b), out var entered);
            // Use EpicLoot's own formatting for the unit, sign and conditional suffix.
            float raw = a == b ? values.MinValue : values.MinValue + (entered - a) / (b - a) * (values.MaxValue - values.MinValue);
            preview.text = valid ? Texts.Get("minimum_preview", MagicItem.GetEffectText(target, raw)) : Texts.Get("invalid");
        }
    }
    internal bool TryTarget(out Target result)
    {
        result = null!;
        if (target == null || target.SelectionWeight <= 0) return false;
        float? threshold = null;
        var values = target.GetValuesForRarity(rarity);
        if (minimumEnabled.isOn && values != null)
        {
            float a = EffectValues.Display(target.Type, values.MinValue), b = EffectValues.Display(target.Type, values.MaxValue);
            if (!Target.TryMinimum(minimum.text, Math.Min(a, b), Math.Max(a, b), out var number)) return false;
            threshold = number;
        }
        result = new Target(target.Type, threshold); return true;
    }
    internal void SetRunning(bool value)
    {
        running = value;
        foreach (var row in rows) row.Toggle.interactable = !value && row.Definition.SelectionWeight > 0;
        filter.interactable = !value; UpdateMinimum();
        SetButtonText(Texts.Get(value ? "stop" : "start"));
    }
    internal void UpdateControls(bool active, bool idle)
    { run.interactable = active || (idle && TryTarget(out _)); }
    private void SetButtonText(string text)
    {
        var tmp = run.GetComponentInChildren<TMP_Text>(true);
        if (tmp) tmp.text = text;
        else { var legacy = run.GetComponentInChildren<Text>(true); if (legacy) legacy.text = text; }
    }
    private static RectTransform Rect(string name, Transform parent)
    { var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform; rect.SetParent(parent, false); return rect; }
    private static void Fill(RectTransform rect, float left, float right, float bottom, float top)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top); }
    private static void Top(RectTransform rect, float left, float right, float top, float height)
    { rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(left, -top - height); rect.offsetMax = new Vector2(-right, -top); }
    public void Dispose()
    {
        if (scroll) scroll.content = originalContent;
        if (scrollTransform) { scrollTransform.offsetMin = originalMin; scrollTransform.offsetMax = originalMax; }
        if (externalScrollbar) { externalScrollbar.offsetMin = scrollbarMin; externalScrollbar.offsetMax = scrollbarMax; }
        if (panel && panel.AvailableEffectsText) panel.AvailableEffectsText.gameObject.SetActive(originalTextActive);
        foreach (var go in owned) if (go) Object.Destroy(go);
        owned.Clear();
    }
}
