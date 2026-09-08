using Core.Runtime;
using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TradingControlsTests
{
    [Theory]
    [InlineData(false,false,0,0,false)]
    [InlineData(false,true,0,0,false)]
    [InlineData(true,false,0,0,false)]
    [InlineData(true,true,1,0,false)]
    [InlineData(true,true,0,1,false)]
    [InlineData(true,true,1,1,false)]
    [InlineData(true,true,0,0,true)]
    public void RuleEditingRequiresInstalledControlsPausedAndNoHoldingsOrOrders(bool installed,bool paused,int holdings,int orders,bool expected)
    {
        var state=new TradingSettingsAccess(installed,paused,holdings,orders);
        state.CanEdit.ShouldBe(expected);
        state.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(EngineStrategySettings.Live)]
    [InlineData(EngineStrategySettings.Shadow)]
    [InlineData(EngineStrategySettings.Tracked)]
    public void EveryEditableRuleHasBehavioralHelpAndASection(string family)
    {
        foreach(var field in EngineStrategySettings.Fields(family,EngineStrategySettings.Defaults(family)))
        {
            field.Help.Length.ShouldBeGreaterThan(40);
            EngineSettingDescriptions.Section(field.Key).ShouldNotBeNullOrWhiteSpace();
        }
        Should.Throw<ArgumentException>(()=>EngineSettingDescriptions.Help("UnknownRule"));
    }
}
