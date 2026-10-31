CREATE INDEX [IX_TradingPositionCurrent_Active] ON [dbo].[TradingPositionCurrent] ([BindingId],[PositionId]) WHERE [IsOpen]=1;
GO
CREATE INDEX [IX_TradingSymbolCurrent_Active] ON [dbo].[TradingSymbolCurrent] ([BindingId],[Symbol]) WHERE [IsRelevant]=1;
GO
CREATE INDEX [IX_TradingOrder_Active] ON [dbo].[TradingOrder] ([BindingId],[RequestId]) WHERE [NeedsWork]=1;
GO
CREATE INDEX [IX_TradingRestrictionCurrent_Active] ON [dbo].[TradingRestrictionCurrent] ([BindingId],[RestrictionId]) WHERE [IsActive]=1;
GO
CREATE UNIQUE INDEX [UX_TradingOrder_Decision] ON [dbo].[TradingOrder] ([BindingId],[DecisionId]);
GO
CREATE UNIQUE INDEX [UX_TradingOrder_Broker] ON [dbo].[TradingOrder] ([BindingId],[BrokerOrderId]) WHERE [BrokerOrderId] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_TradingRestrictionCurrent_Cause] ON [dbo].[TradingRestrictionCurrent] ([BindingId],[ScopeKey],[ActionCode],[ReasonCode]) WHERE [IsActive]=1;
GO
