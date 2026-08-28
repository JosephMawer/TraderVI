using Core.Trader;
using Shouldly;
using System;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class PaperTradeEntryWorkflowTests
{
    [Theory]
    [InlineData(0, 15.34)]
    [InlineData(-1, 15.34)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    public async Task OpenAsync_RejectsInvalidOperatorFillBeforeDatabaseAccess(
        int shares,
        decimal price)
    {
        var workflow = new PaperTradeEntryWorkflow();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => workflow.OpenAsync(Guid.NewGuid(), shares, price));
    }

    [Fact]
    public async Task OpenAsync_RequiresSavedPickBeforeDatabaseAccess()
    {
        var workflow = new PaperTradeEntryWorkflow();

        await Should.ThrowAsync<ArgumentException>(
            () => workflow.OpenAsync(Guid.Empty, 5, 15.34m));
    }
}
