#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Trader.Risk;

namespace Core.Oracle;

/// <summary>Qualified engine facts for one exact choice. Collection/rule classification remains with engine adapters.</summary>
public sealed record AiStrategyDecisionContext(string PortfolioId, string StrategyVersion, DateTimeOffset ObservedUtc,
    DateTimeOffset ExpiresUtc, bool CanPropose, ImmutableArray<AiPortfolioRule> Rules, ImmutableArray<AiMarketEvidence> Evidence);

/// <summary>Creates review inputs only. No model call, assignment or order authority.</summary>
public static class AiPortfolioPacketBuilder
{
    public static AiPortfolioPacket Build(AiPortfolioCatalog catalog, AiPortfolioRiskJournal managedJournal,
        AiRiskObservationResult observation, string? activeChoiceId, ImmutableArray<AiStrategyDecisionContext> contexts,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(managedJournal);
        ArgumentNullException.ThrowIfNull(observation);
        if (catalog.Entries.IsDefault || catalog.AsOfUtc > now || catalog.ExpiresUtc <= now ||
            catalog.Entries.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Entries.Length ||
            contexts.IsDefault || contexts.Any(c => c is null) || contexts.Select(c => c.PortfolioId).Distinct().Count() != contexts.Length ||
            contexts.Any(c => !catalog.Entries.Any(e => e.Id == c.PortfolioId)))
            throw new InvalidDataException("The current catalogue and unambiguous engine contexts are required.");
        foreach (var entry in catalog.Entries) entry.Validate();
        var current = managedJournal.ReadCurrent();
        if (!observation.Accepted || observation.Snapshot != current || current.SourceBlock is not null || current.State is not { } state ||
            state.PortfolioId != managedJournal.Scope.PortfolioId || state.LastValuation.Source is not { } source ||
            state.LastValuation.At > now || state.LastValuation.ValidUntil <= now ||
            source.Sha256 != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.EvidenceJson))))
            throw new InvalidDataException("Use a fresh accepted observation from this managed account's current risk journal.");
        bool paper = managedJournal.Scope.Mode == AiRiskAccountMode.Paper;
        if (paper ? managedJournal.Scope.AccountScopeId != managedJournal.Scope.PortfolioId ||
                source.Contract is not (DelphiLiveAiPortfolioRiskMonitor.ValuationContract or SystemShadowAiPortfolioRiskMonitor.ValuationContract or AiPaperAccountValuation.Contract)
            : source.Contract != BrokerAiPortfolioRiskMonitor.ValuationContract)
            throw new InvalidDataException("The managed book must use its reviewed paper/live source contract.");
        using var savedBook = JsonDocument.Parse(source.EvidenceJson);
        var evidence = ImmutableArray.CreateBuilder<AiMarketEvidence>();
        evidence.Add(new("managed-account", state.LastValuation.At, JsonSerializer.Serialize(new
        {
            Role = "Actual managed account; candidate paper balances are never available funds",
            managedJournal.Scope, current.Revision, state.LastValuation.Value, state.OriginalCapital,
            state.RoundId, state.RoundNumber, state.RoundPeak, state.ExitTrigger, state.Phase,
            observation.Assessment, source.Contract, source.Sha256, SavedBook = savedBook.RootElement
        })));
        var candidates = ImmutableArray.CreateBuilder<AiPortfolioCandidate>();
        var expires = catalog.ExpiresUtc < state.LastValuation.ValidUntil ? catalog.ExpiresUtc : state.LastValuation.ValidUntil;
        foreach (var choice in catalog.Entries.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            var reasons = choice.UnavailableReasons.ToBuilder();
            var rules = ImmutableArray.CreateBuilder<AiPortfolioRule>();
            rules.Add(new("app-protection", true, "Preserve risk history and existing stops; stops may only tighten. Execution must recheck current account, orders, funds and protection."));
            // This review supplies the missing AI attention; it cannot remove any other saved block.
            reasons.AddRange(observation.Assessment.NewBuyBlocks.Where(b => b != "FreshAiReviewRequired"));
            var context = contexts.SingleOrDefault(c => c.PortfolioId == choice.Id);
            if (context is null || context.StrategyVersion != choice.Version || context.ObservedUtc > now || context.ExpiresUtc <= now ||
                context.ExpiresUtc <= context.ObservedUtc || context.Rules.IsDefault || context.Evidence.IsDefaultOrEmpty)
                reasons.Add("Fresh matching engine decision evidence is unavailable");
            else
            {
                if (context.Evidence.Any(e => e is null || e.ObservedUtc > context.ObservedUtc))
                    throw new InvalidDataException("Engine evidence cannot be newer than its declared observation.");
                if (!context.CanPropose) reasons.Add("The engine context does not permit a portfolio proposal");
                rules.AddRange(context.Rules);
                // Namespace evidence identities so different engines may use their own local IDs safely.
                evidence.AddRange(context.Evidence.Select(e => e with { Id = choice.Id + ":" + e.Id }));
                if (context.ExpiresUtc < expires) expires = context.ExpiresUtc;
            }
            evidence.Add(new("strategy:" + choice.Id, catalog.AsOfUtc, JsonSerializer.Serialize(new
            {
                Role = "Candidate strategy definition; paper source is separate from managed funds",
                choice.PaperAccountId, choice.Strategy, choice.Comparison, UnavailableReasons = reasons.ToImmutable()
            })));
            var summary = reasons.Count == 0 ? choice.Name : choice.Name + ". Unavailable: " + string.Join("; ", reasons.Distinct());
            candidates.Add(new(choice.Id, choice.Version, choice.EngineFamily, reasons.Count == 0, summary, rules.ToArray()));
        }
        var packet = new AiPortfolioPacket(Guid.NewGuid(), 1, now, expires, activeChoiceId, candidates.ToArray(), evidence.ToArray(), []);
        AiPortfolioReviewer.ValidatePacket(packet, now);
        return packet;
    }
}
