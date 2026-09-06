#nullable enable

using System;
using System.Collections.Generic;

namespace Core.Trader.DelphiLive;

public enum DelphiLiveMomentumState
{
    Strong,
    StrongWithConflict,
    PositiveNudge,
    PositiveNudgeWithConflict,
    MixedConflict,
    Neutral,
    NegativeNudgeWithConflict,
    NegativeNudge,
    WeakWithConflict,
    Weak,
    VeryWeak
}

public enum DelphiLiveStrongTier
{
    None,
    FourOfFour,
    CleanThree
}

public enum DelphiLiveNeutralDetail
{
    None,
    SupportTilt,
    Conflict,
    WeakTilt
}

public sealed record DelphiLiveMomentumJudgment(
    DelphiLiveMomentumState State,
    DelphiLiveStrongTier StrongTier,
    DelphiLiveNeutralDetail NeutralDetail,
    int SupportiveVotes,
    int WeakeningVotes,
    int PositiveLeaningCount,
    int NegativeLeaningCount)
{
    public bool IsEntryEligibleStrong =>
        State is DelphiLiveMomentumState.Strong or
            DelphiLiveMomentumState.StrongWithConflict;

    public bool IsStrongWeakening =>
        State is DelphiLiveMomentumState.WeakWithConflict or
            DelphiLiveMomentumState.Weak or
            DelphiLiveMomentumState.VeryWeak;
}

public static class DelphiLiveFamilyCombiner
{
    public static DelphiLiveMomentumJudgment Combine(
        IReadOnlyCollection<DelphiLiveFamilyJudgment> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        if (families.Count != 4)
            throw new ArgumentException("Exactly four named family judgments are required.", nameof(families));

        var seen = new HashSet<DelphiLiveSignalFamily>();
        int supportive = 0;
        int weakening = 0;
        int positiveLeaning = 0;
        int negativeLeaning = 0;
        foreach (DelphiLiveFamilyJudgment family in families)
        {
            ArgumentNullException.ThrowIfNull(family);
            if (!seen.Add(family.Family))
                throw new ArgumentException("Each Delphi Live family must appear exactly once.", nameof(families));
            switch (family.State)
            {
                case DelphiLiveFamilyState.Supportive:
                    supportive++;
                    break;
                case DelphiLiveFamilyState.Weakening:
                    weakening++;
                    break;
                case DelphiLiveFamilyState.PositiveLeaning:
                    positiveLeaning++;
                    break;
                case DelphiLiveFamilyState.NegativeLeaning:
                    negativeLeaning++;
                    break;
            }
        }

        if (seen.Count != Enum.GetValues<DelphiLiveSignalFamily>().Length)
            throw new ArgumentException("Every named Delphi Live family is required.", nameof(families));

        (DelphiLiveMomentumState state,
            DelphiLiveStrongTier tier,
            DelphiLiveNeutralDetail detail) = (supportive, weakening) switch
        {
            (4, 0) => (
                DelphiLiveMomentumState.Strong,
                DelphiLiveStrongTier.FourOfFour,
                DelphiLiveNeutralDetail.None),
            (3, 0) => (
                DelphiLiveMomentumState.Strong,
                DelphiLiveStrongTier.CleanThree,
                DelphiLiveNeutralDetail.None),
            (3, 1) => (
                DelphiLiveMomentumState.StrongWithConflict,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.None),
            (2, 0) => (
                DelphiLiveMomentumState.PositiveNudge,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.None),
            (2, 1) => (
                DelphiLiveMomentumState.PositiveNudgeWithConflict,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.None),
            (2, 2) => (
                DelphiLiveMomentumState.MixedConflict,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.None),
            (1, 0) => (
                DelphiLiveMomentumState.Neutral,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.SupportTilt),
            (0, 0) => (
                DelphiLiveMomentumState.Neutral,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.None),
            (1, 1) => (
                DelphiLiveMomentumState.Neutral,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.Conflict),
            (0, 1) => (
                DelphiLiveMomentumState.Neutral,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.WeakTilt),
            (1, 2) => (
                DelphiLiveMomentumState.NegativeNudgeWithConflict,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.None),
            (0, 2) => (
                DelphiLiveMomentumState.NegativeNudge,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.None),
            (1, 3) => (
                DelphiLiveMomentumState.WeakWithConflict,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.None),
            (0, 3) => (
                DelphiLiveMomentumState.Weak,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.None),
            (0, 4) => (
                DelphiLiveMomentumState.VeryWeak,
                DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.None),
            _ => throw new InvalidOperationException(
                $"Unsupported four-family vote combination S={supportive}, W={weakening}.")
        };

        return new DelphiLiveMomentumJudgment(
            state,
            tier,
            detail,
            supportive,
            weakening,
            positiveLeaning,
            negativeLeaning);
    }
}
