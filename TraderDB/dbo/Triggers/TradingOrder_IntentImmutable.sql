CREATE TRIGGER [dbo].[TradingOrder_IntentImmutable] ON [dbo].[TradingOrder]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT BindingId,RequestId,DecisionId,PermissionId,PositionId,SecurityId,Symbol,Side,Quantity,ReferencePrice,CreatedUtc FROM deleted
        EXCEPT
        SELECT BindingId,RequestId,DecisionId,PermissionId,PositionId,SecurityId,Symbol,Side,Quantity,ReferencePrice,CreatedUtc FROM inserted)
        THROW 54602,'Original order intent cannot change.',1;
    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.BindingId=d.BindingId AND i.RequestId=d.RequestId
        WHERE (d.BrokerOrderId IS NOT NULL AND (i.BrokerOrderId IS NULL OR d.BrokerOrderId<>i.BrokerOrderId))
        OR i.LatestFilledQuantity<d.LatestFilledQuantity OR i.AppliedFilledQuantity<d.AppliedFilledQuantity
        OR i.LatestNetValue<d.LatestNetValue OR i.AppliedNetValue<d.AppliedNetValue
        OR (d.Status IN (N'Filled',N'Rejected',N'Cancelled',N'Expired') AND d.Status<>i.Status)
        OR (d.NeedsWork=0 AND (i.NeedsWork=1 OR i.LatestFilledQuantity<>d.LatestFilledQuantity
            OR i.AppliedFilledQuantity<>d.AppliedFilledQuantity OR i.LatestNetValue<>d.LatestNetValue
            OR i.AppliedNetValue<>d.AppliedNetValue OR i.ReservedQuantity<>d.ReservedQuantity OR i.ReservedCash<>d.ReservedCash)))
        THROW 54603,'Order outcome regressed or completed accounting was reopened.',1;
END;
GO
