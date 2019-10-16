using System;

namespace Core
{
    /// <summary>
    /// Default members for the index summary
    /// </summary>
    public interface IIndexSummary
    {
        DateTime Date { get; set; }
        string Name { get; set; }
        float Last { get; set; }
        float Change { get; set; }
        float PercentChange { get; set; }
    }
}
