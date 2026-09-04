CREATE TABLE [dbo].[LeadershipData]
(
	[Date]              DATE          NOT NULL,
	[NewHighs]          INT           NOT NULL,
	[NewLows]           INT           NOT NULL,
	[IssuesTraded]      INT           NOT NULL,
	[ActiveAdvancers]   INT           NULL,
	[ActiveDecliners]   INT           NULL,
	[ActiveN]           INT           NULL,
	[Tsx60Close]        DECIMAL(10,2) NULL,
	[EqualWeightClose]  DECIMAL(10,2) NULL,

	CONSTRAINT [PK_LeadershipData] PRIMARY KEY CLUSTERED ([Date]),
	CONSTRAINT [CK_LeadershipData_ActiveBreadthObservation] CHECK
	(
		(
			[ActiveAdvancers] IS NULL
			AND [ActiveDecliners] IS NULL
			AND [ActiveN] IS NULL
		)
		OR
		(
			[ActiveAdvancers] IS NOT NULL
			AND [ActiveDecliners] IS NOT NULL
			AND [ActiveN] IS NOT NULL
			AND [ActiveAdvancers] >= 0
			AND [ActiveDecliners] >= 0
			AND [ActiveN] > 0
			AND CONVERT(BIGINT, [ActiveAdvancers]) + [ActiveDecliners] <= [ActiveN]
		)
	),
	CONSTRAINT [CK_LeadershipData_NhnlObservation] CHECK
	(
		[NewHighs] >= 0
		AND [NewLows] >= 0
		AND [IssuesTraded] > 0
		AND [NewHighs] <= [IssuesTraded]
		AND [NewLows] <= [IssuesTraded]
	),
	CONSTRAINT [CK_LeadershipData_BenchmarkPrices] CHECK
	(
		([Tsx60Close] IS NULL OR [Tsx60Close] > 0)
		AND ([EqualWeightClose] IS NULL OR [EqualWeightClose] > 0)
	)
);
GO

CREATE INDEX [IX_LeadershipData_Date]
	ON [dbo].[LeadershipData] ([Date] DESC);
GO
