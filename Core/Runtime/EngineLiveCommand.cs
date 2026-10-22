#nullable enable
using Core.Trader.DelphiLive;
using System;

namespace Core.Runtime;

public enum EngineLiveCommandKind { Activate, Discovery, Untouched, Promotion, MeasurementDefect, CapitalReview, CorporateAction }
public sealed record EngineLiveCommand(EngineLiveCommandKind Kind, string Reason,
    decimal Capital = 0, string Currency = "CAD", Guid? Id = null,
    DelphiLiveHypothesisFamily Family = default, decimal[]? Values = null,
    string? Symbol = null, DateOnly? From = null, DateOnly? Through = null);
