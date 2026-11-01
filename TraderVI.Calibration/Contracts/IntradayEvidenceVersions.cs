namespace Core.TMX.Models.Domain;

public static class IntradayEvidenceVersions
{
    public const int Schema = 1;
    public const string Provider = "TMXMoney";
    public const string SourceContract = "TmxChartIntradayNoFreqV1";
    public const string Collector = "IntradayEvidenceCollectorV2";
    public const string Policy = "DelayedIntradaySwingV1";
}
