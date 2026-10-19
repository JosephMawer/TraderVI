#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Trader.DelphiLive;

namespace Core.Trader.Promotion;

public sealed record BrokerOpeningHolding(string SecurityId, string Symbol, int Quantity);
public sealed record BrokerOpeningSessionPlan(Guid PlanId, string AccountId, DateOnly Session,
    Guid PaperPortfolioId, Guid PolicyVersionId, Guid DailyStrategyVersionId, string ImplementationIdentity,
    ImmutableArray<BrokerOpeningHolding> Holdings, DateTimeOffset VerifiedAt, DateTimeOffset AuthorizedAt,
    string AuthorizedBy)
{
    public bool MatchesAccount(WealthsimpleTfsaAccount account) => account.Id == AccountId;
    public Guid BaselineId => StableId("baseline");
    public Guid SellRequestId(BrokerOpeningHolding holding) => StableId("sell:" + holding.SecurityId);
    public Guid ScopeId => StableId("no-transfer-scope");
    private Guid StableId(string suffix) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{PlanId:D}:{suffix}")).AsSpan(0,16));
    public void Validate()
    {
        if (PlanId == Guid.Empty || Session == default || string.IsNullOrWhiteSpace(AccountId) || PaperPortfolioId == Guid.Empty ||
            PolicyVersionId == Guid.Empty || DailyStrategyVersionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(ImplementationIdentity) || Holdings.IsDefault || Holdings.Length != 2 ||
            Holdings.Any(h=>string.IsNullOrWhiteSpace(h.SecurityId)||string.IsNullOrWhiteSpace(h.Symbol)||h.Quantity<1) ||
            Holdings.Select(h=>h.SecurityId).Distinct(StringComparer.Ordinal).Count()!=2 ||
            Holdings.Select(h=>h.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=2 ||
            VerifiedAt==default || AuthorizedAt<VerifiedAt || string.IsNullOrWhiteSpace(AuthorizedBy))
            throw new InvalidOperationException("The opening plan must pin the verified two holdings, exact Live source and explicit operator scope.");
    }
    public static BrokerOpeningSessionPlan FromSnapshot(WealthsimpleTfsaSnapshot snapshot, DateOnly session,
        Guid portfolio, Guid policy, Guid daily, string implementation, string authorizedBy, DateTimeOffset now)
    {
        if (snapshot.RetrievedAt > now || now - snapshot.RetrievedAt > TimeSpan.FromSeconds(30))
            throw new InvalidOperationException("Review fresh actual holdings before preparing an opening plan.");
        var holdings=snapshot.Holdings.Where(h=>h.Quantity>0).Select(h=>ManualSellSelection.FromHolding(h,snapshot,0)
            ?? throw new InvalidOperationException("Every opening holding needs its verified CAD broker instrument."))
            .Select(h=>h.IsWholeHolding ? new BrokerOpeningHolding(h.SecurityId,h.Symbol,(int)h.Quantity)
                : throw new InvalidOperationException("The opening plan supports whole-share holdings only.")).ToImmutableArray();
        var result=new BrokerOpeningSessionPlan(Guid.NewGuid(),snapshot.Account.Id,session,portfolio,policy,daily,
            implementation,holdings,snapshot.RetrievedAt,now,authorizedBy);
        result.Validate();return result;
    }
    public override string ToString()=>"Authorized opening plan (financial data omitted)";
}

public sealed record BrokerOpeningStep(string Kind, string Message, BrokerOpeningHolding? Holding=null, Guid? RequestId=null);

/// <summary>Read-only plan progression. Actual sales use the existing protected manual-order workflow.</summary>
public static class BrokerOpeningSessionPolicy
{
    public static BrokerOpeningStep Next(BrokerOpeningSessionPlan plan, WealthsimpleTfsaSnapshot? snapshot,
        WealthsimplePendingOrders? pending, ImmutableArray<ManualSellRecord> sells, ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        plan.Validate();var bounds=calendar.GetSessionBounds(plan.Session);
        foreach(var holding in plan.Holdings)
        {
            var id=plan.SellRequestId(holding);var saved=sells.SingleOrDefault(s=>s.Intent.RequestId==id);
            if(saved is null)continue;
            if(saved.Intent.AccountId!=plan.AccountId||saved.Intent.SecurityId!=holding.SecurityId||
                saved.Intent.Symbol!=holding.Symbol||saved.Intent.Quantity!=holding.Quantity)
                return new("Blocked","The saved opening order differs from the authorized holding.");
            if(!saved.IsResolved)return new("Recover","Recover the original opening sell before any further order.",holding,id);
            if(saved.Latest?.IsFilled!=true||saved.Latest.FilledQuantity!=holding.Quantity)
                return new("Blocked","An opening sale did not fill completely. Review it before continuing.");
        }
        if(now<bounds.OpenUtc)return new("Waiting","Wait for the regular-session opening before reviewing any sale.");
        if(now>=bounds.CloseUtc)return new("Blocked","The opening session ended; no new opening order is authorized.");
        if(snapshot is null||pending is null||!plan.MatchesAccount(snapshot.Account)||
            !ReferenceEquals(snapshot.Account,pending.Account)||snapshot.RetrievedAt>now||pending.RetrievedAt>now||
            now-snapshot.RetrievedAt>TimeSpan.FromSeconds(30)||now-pending.RetrievedAt>TimeSpan.FromSeconds(30))
            return new("ReadAccount","Read fresh matching holdings and pending orders before continuing.");
        if(pending.Orders.Count!=0)return new("Blocked","Pending broker orders must reconcile before opening sales or purchases.");
        var remaining=plan.Holdings.Where(h=>!sells.Any(s=>s.Intent.RequestId==plan.SellRequestId(h))).ToArray();
        var actual=snapshot.Holdings.Where(h=>h.Quantity>0).ToArray();
        if(actual.Length!=remaining.Length||remaining.Any(h=>!actual.Any(a=>a.SecurityId==h.SecurityId&&a.Symbol==h.Symbol&&a.Quantity==h.Quantity)))
            return new("Blocked","The current holdings changed from the reviewed opening plan. No expanded liquidation is permitted.");
        return remaining.Length==0?new("PrepareAccount","Both sales and their inventories are verified; reconcile cash and prepare the Live session.")
            :new("Sell","Sell the next exact authorized holding through the existing order workflow.",remaining[0],plan.SellRequestId(remaining[0]));
    }
}

/// <summary>One immutable, encrypted authorization; progress is recovered from original order IDs and SQL.</summary>
public sealed class BrokerOpeningPlanFileStore(string path,IManualBuyProtector protector)
{
    public BrokerOpeningSessionPlan? Read()
    {
        if (!File.Exists(path)) return null;
        var plan = JsonSerializer.Deserialize<BrokerOpeningSessionPlan>(protector.Unprotect(File.ReadAllBytes(path)))
            ?? throw new InvalidDataException("The protected opening plan is unreadable.");
        plan.Validate();
        return plan;
    }
    public void SaveNew(BrokerOpeningSessionPlan plan)
    {
        plan.Validate();
        if(File.Exists(path))throw new InvalidOperationException("An opening plan already exists. Review it rather than replacing authority.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".pending";
        var bytes=protector.Protect(JsonSerializer.SerializeToUtf8Bytes(plan));
        using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {stream.Write(bytes);stream.Flush(true);}
        File.Move(temporary,path,overwrite:false);
    }
}
