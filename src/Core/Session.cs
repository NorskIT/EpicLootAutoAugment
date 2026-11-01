using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EpicLootAutoAugment.Core;

public sealed class Target
{
    public string EffectId { get; }
    public float? Minimum { get; }
    public Target(string effectId, float? minimum = null)
    {
        if (string.IsNullOrWhiteSpace(effectId)) throw new ArgumentException(nameof(effectId));
        if (minimum.HasValue && !Finite(minimum.Value)) throw new ArgumentException(nameof(minimum));
        EffectId = effectId; Minimum = minimum;
    }
    public bool Matches(string id, float value) => id == EffectId &&
        (!Minimum.HasValue || (Finite(value) && value >= Minimum.Value));
    public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    public static bool TryMinimum(string text, float min, float max, out float value) =>
        float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value) && Finite(value) && value >= min && value <= max;
}

public enum RunPhase { Stopped, Ready, WaitingForChoices, ChoicesReady, WaitingForSettlement }
public enum OfferDecision { Ignore, Keep, Match }

// No game or payment code here. A request cannot be repeated while its outcome is unknown.
public sealed class Session
{
    public RunPhase Phase { get; private set; }
    public bool Running => Phase != RunPhase.Stopped;
    public Target? Target { get; private set; }
    public int Attempts { get; private set; }
    public void Start(Target target) { Target = target; Attempts = 0; Phase = RunPhase.Ready; }
    public bool Request()
    {
        if (Phase != RunPhase.Ready) return false;
        Phase = RunPhase.WaitingForChoices; return true;
    }
    public bool Receive()
    {
        if (Phase != RunPhase.WaitingForChoices) return false;
        Attempts++; Phase = RunPhase.ChoicesReady; return true;
    }
    public OfferDecision Evaluate(IEnumerable<KeyValuePair<string, float>> selectableOffers)
    {
        if (Phase != RunPhase.ChoicesReady) return OfferDecision.Ignore;
        if (selectableOffers.Any(x => Target!.Matches(x.Key, x.Value)))
        { Stop(); return OfferDecision.Match; }
        Phase = RunPhase.WaitingForSettlement; return OfferDecision.Keep;
    }
    public void Settled() { if (Phase == RunPhase.WaitingForSettlement) Phase = RunPhase.Ready; }
    public void Stop() => Phase = RunPhase.Stopped;
}
