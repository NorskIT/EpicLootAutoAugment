using System.Collections.Generic;
using Jotunn.Managers;

namespace EpicLootAutoAugment;

internal static class Texts
{
    private static readonly Dictionary<string, string> English = new()
    {
        ["start"] = "Auto Augment", ["stop"] = "Stop", ["minimum"] = "Minimum value",
        ["choose"] = "Select an effect above", ["filter"] = "Filter effects...",
        ["valueless"] = "This effect has no numeric value.",
        ["invalid"] = "Enter a minimum within the shown range.",
        ["searching"] = "Searching · {0} attempts", ["found"] = "Effect found · {0} attempts",
        ["materials"] = "Not enough materials · {0} attempts", ["stopped"] = "Auto Augment stopped",
        ["selection_changed"] = "Selection changed · stopped", ["already"] = "This slot already meets your target.",
        ["busy"] = "Wait for the current augment to finish.", ["unavailable"] = "Table or item unavailable · stopped",
        ["timeout"] = "Action did not finish · stopped", ["error"] = "Auto Augment stopped. See the BepInEx log.",
        ["cannot_keep"] = "Keep is unavailable. Choose an effect manually.",
        ["minimum_preview"] = "Minimum: {0}"
    };
    internal static void Register()
    {
        var localization = LocalizationManager.Instance.GetLocalization();
        foreach (var pair in English)
        {
            string key = "autoaugment_" + pair.Key;
            localization.AddTranslation(key, pair.Value);
        }
    }
    internal static string Get(string key, params object[] args)
    {
        string text = Localization.instance != null ? Localization.instance.Localize("$autoaugment_" + key) : "";
        if (string.IsNullOrEmpty(text) || text.StartsWith("$") || text.StartsWith("[autoaugment_")) text = English[key];
        return args.Length == 0 ? text : string.Format(text, args);
    }
}
