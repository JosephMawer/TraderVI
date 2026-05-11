using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Oracle.Llm;

/// <summary>
/// Placeholder for the dotLLM (https://github.com/kkokosa/dotLLM) integration.
///
/// Deliberately left as a stub: dotLLM's natural home is Phase 5 (backtest
/// narration), where running many local inferences is cost-effective and the
/// quality bar is "any coherent reasoning" rather than "production critique".
///
/// To finish this:
/// 1. Add the dotLLM package or source reference to <c>Core.csproj</c>.
/// 2. Map <see cref="LlmRequest"/> to dotLLM's session/message API.
/// 3. Populate <see cref="LlmUsage"/> with token counts dotLLM reports
///    (cost = 0 since inference is local).
/// 4. Register in <see cref="LlmClientFactory"/> when <c>ORACLE_LLM_PROVIDER=dotllm</c>.
/// </summary>
public sealed class DotLlmClient : ILlmClient
{
    private readonly string _model;

    public string Provider => "dotllm";
    public string Model => _model;

    public DotLlmClient(string model)
    {
        _model = model;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "DotLlmClient is a Phase 5 placeholder. " +
            "Wire it up when local inference becomes useful (see class XML docs).");
    }
}
