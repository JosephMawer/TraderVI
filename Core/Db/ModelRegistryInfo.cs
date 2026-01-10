using System;

namespace Core.Db
{
    public sealed class ModelRegistryInfo
    {
        public Guid ModelId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string TaskType { get; init; } = string.Empty;
        public string ModelKind { get; init; } = string.Empty;
        public string? Family { get; init; }
        public string TimeFrame { get; init; } = string.Empty;
        public int LookbackBars { get; init; }
        public int HorizonBars { get; init; }
        public string InputSchema { get; init; } = string.Empty;
        public string? FeatureSet { get; init; }
        public string ZipPath { get; init; } = string.Empty;
        public double ThresholdBuy { get; init; }
        public double ThresholdSell { get; init; }
        public bool IsEnabled { get; init; }
        public DateTime CreatedUtc { get; init; }
        public DateTime? TrainedFromUtc { get; init; }
        public DateTime? TrainedToUtc { get; init; }
        public string? Notes { get; init; }
    }
}