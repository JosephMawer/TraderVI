#nullable enable
using Core.WSTrade.Accounts;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    internal Task<WealthsimpleReadResult<BrokerAccountHistory>> ReadRegularTradingHistoryAsync(WealthsimpleTfsaAccount account,
        CancellationToken ct = default) => ReadHistoryAsync(account, ct, ReadOrderGroupAsync);

    // Only field aliases and variable names are generated. Broker-provided IDs remain variables.
    private Task<JsonDocument> ReadOrderGroupAsync(string[] ids, CancellationToken ct)
    {
        var variables = new Dictionary<string, string> { ["branchId"] = "TR" };
        var parameters = new List<string> { "$branchId: String!" };
        var fields = new List<string>();
        for (int i = 0; i < ids.Length; i++)
        {
            string key = "id" + i;
            variables.Add(key, ids[i]);
            parameters.Add("$" + key + ": String!");
            fields.Add($"order{i}: orderServiceExtendedOrderByExternalId(branchId: $branchId, externalId: ${key}) {{ {OrderDetailFields} }}");
        }
        return QueryAsync("TraderVIRegularOrderGroup", $"query TraderVIRegularOrderGroup({string.Join(", ", parameters)}) {{ {string.Join("\n", fields)} }}", variables, ct);
    }
}
