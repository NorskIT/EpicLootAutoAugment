using System;
using System.Globalization;
using EpicLoot;
using HarmonyLib;

namespace EpicLootAutoAugment;

internal static class EffectValues
{
    private static readonly System.Reflection.MethodInfo DisplayArgs = AccessTools.Method(typeof(MagicItem), "GetDisplayArgs");
    internal static float Display(string id, float raw)
    {
        var args = (object[])DisplayArgs.Invoke(null, new object[] { id, raw });
        return Convert.ToSingle(args[0], CultureInfo.InvariantCulture);
    }
}
