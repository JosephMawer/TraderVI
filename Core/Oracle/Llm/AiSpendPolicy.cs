#nullable enable
using System;

namespace Core.Oracle.Llm;

/// <summary>Reviewed upper bounds, not live FX quotes or a provider billing guarantee.</summary>
public sealed record AiSpendPolicy(
    decimal CadPerUsdCeiling,
    decimal TaxAndFeeMultiplier,
    DateTimeOffset ValidUntilUtc,
    decimal MonthlyLimitCad = 90m,
    int MaxInputTokens = 32_000,
    int MaxOutputTokens = 4_096,
    decimal InputUsdPerMillion = 12.50m,
    decimal OutputUsdPerMillion = 50m)
{
    public const string Model = "gpt-6-astra";

    public void Validate(DateTimeOffset now)
    {
        if (MonthlyLimitCad <= 0 || MonthlyLimitCad >= 100 || CadPerUsdCeiling < 1 ||
            TaxAndFeeMultiplier < 1 || InputUsdPerMillion < 12.50m || OutputUsdPerMillion < 50m ||
            MaxInputTokens <= 0 || MaxInputTokens > 272_000 ||
            MaxOutputTokens <= 0 || MaxOutputTokens > 128_000 || ValidUntilUtc <= now)
            throw new InvalidOperationException("AI spending policy is missing, expired or outside approved bounds.");
    }

    // Charge all input at the conservative cache-write rate; do not spend expected cache savings.
    public decimal CostUsd(int inputTokens, int outputTokens)
    {
        if (inputTokens < 0 || outputTokens < 0) throw new ArgumentOutOfRangeException(nameof(inputTokens));
        return checked((inputTokens * InputUsdPerMillion + outputTokens * OutputUsdPerMillion) / 1_000_000m);
    }

    public decimal CostCad(int inputTokens, int outputTokens) =>
        decimal.Ceiling(checked(CostUsd(inputTokens, outputTokens) * CadPerUsdCeiling * TaxAndFeeMultiplier) * 10_000m) / 10_000m;
}
