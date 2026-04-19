CREATE TABLE [dbo].[LeadershipData]
(
	[Date]              DATE          NOT NULL,
	[NewHighs]          INT           NOT NULL,
	[NewLows]           INT           NOT NULL,
	[IssuesTraded]      INT           NOT NULL,
	[ActiveAdvancers]   INT           NOT NULL,
	[ActiveDecliners]   INT           NOT NULL,
	[ActiveN]           INT           NOT NULL,
	[Tsx60Close]        DECIMAL(10,2) NULL,
	[EqualWeightClose]  DECIMAL(10,2) NULL,

	CONSTRAINT [PK_LeadershipData] PRIMARY KEY CLUSTERED ([Date])
);
