using System;
using System.Collections.Generic;
using EpicLootAutoAugment.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EpicLootAutoAugment.Tests;

[TestClass]
public sealed class SessionTests
{
    private static KeyValuePair<string, float>[] Offers(string id, float value = 0) => new[] { new KeyValuePair<string, float>(id, value) };
    [TestMethod] public void MatchStopsBeforeAnotherPaymentOrKeep()
    {
        var s = new Session(); s.Start(new Target("Executioner"));
        Assert.IsTrue(s.Request()); Assert.IsTrue(s.Receive());
        Assert.AreEqual(OfferDecision.Match, s.Evaluate(Offers("Executioner", 205)));
        s.Settled(); Assert.IsFalse(s.Request());
        Assert.AreEqual(OfferDecision.Ignore, s.Evaluate(Offers("Other")));
        Assert.AreEqual(1, s.Attempts);
    }
    [TestMethod] public void MissCannotRequestAgainUntilSettlement()
    {
        var s = new Session(); s.Start(new Target("Executioner")); s.Request();
        Assert.IsFalse(s.Request()); Assert.IsTrue(s.Receive()); Assert.IsFalse(s.Receive());
        Assert.AreEqual(OfferDecision.Keep, s.Evaluate(Offers("Other")));
        Assert.IsFalse(s.Request()); Assert.AreEqual(OfferDecision.Ignore, s.Evaluate(Offers("Other")));
        s.Settled(); Assert.IsTrue(s.Request()); Assert.AreEqual(1, s.Attempts);
    }
    [TestMethod] public void StopDuringPaymentDoesNotRetryWhenLateResultArrives()
    {
        var s = new Session(); s.Start(new Target("Executioner")); s.Request(); s.Stop();
        Assert.IsFalse(s.Receive()); s.Settled(); Assert.IsFalse(s.Request());
    }
    [TestMethod] public void MinimumMatchesExactThresholdAndRejectsLowerRoll()
    { var target = new Target("AttackSpeed", 20); Assert.IsFalse(target.Matches("AttackSpeed", 19.99f)); Assert.IsTrue(target.Matches("AttackSpeed", 20)); Assert.IsFalse(target.Matches("AttackSpeedLowHealth", 24)); }
    [TestMethod] public void ValuelessTargetDoesNotRequireNumber()
    { Assert.IsTrue(new Target("Indestructible").Matches("Indestructible", float.NaN)); }
    [TestMethod] public void NumericMatchRejectsNonFiniteRolls()
    { Assert.IsFalse(new Target("AttackSpeed", 20).Matches("AttackSpeed", float.NaN)); Assert.IsFalse(new Target("AttackSpeed", 20).Matches("AttackSpeed", float.PositiveInfinity)); }
    [TestMethod] public void DisabledMinimumAcceptsLowRoll()
    { Assert.IsTrue(new Target("AttackSpeed").Matches("AttackSpeed", 18)); }
    [TestMethod] public void AnySelectableMatchingOfferStops()
    {
        var s = new Session(); s.Start(new Target("Executioner", 240)); s.Request(); s.Receive();
        Assert.AreEqual(OfferDecision.Match, s.Evaluate(new[] { new KeyValuePair<string,float>("Keep", 0), new("Executioner", 239), new("Executioner", 245) }));
    }
    [TestMethod] public void DecimalCommaAndPointAreEquivalent()
    { Assert.IsTrue(Target.TryMinimum(" 20,5 ", 18, 24, out float a)); Assert.IsTrue(Target.TryMinimum("20.5", 18, 24, out float b)); Assert.AreEqual(a, b); }
    [TestMethod] public void ImpossibleAndMalformedMinimumRejected()
    { foreach (var value in new[] { "", "NaN", "Infinity", "25", "17", "20,1.2", "2e1", "20%" }) Assert.IsFalse(Target.TryMinimum(value, 18, 24, out _), value); }
    [TestMethod] public void MillisecondsAndReductionMagnitudeUseSameThresholdRule()
    { Assert.IsTrue(new Target("ParryWindow", 97).Matches("ParryWindow", 97)); Assert.IsTrue(new Target("ReduceStamina", 20).Matches("ReduceStamina", 24)); }
    [TestMethod] public void FreshRunResetsAttempts()
    { var s = new Session(); s.Start(new Target("A")); s.Request(); s.Receive(); s.Stop(); s.Start(new Target("B")); Assert.AreEqual(0, s.Attempts); Assert.IsTrue(s.Request()); }
}
