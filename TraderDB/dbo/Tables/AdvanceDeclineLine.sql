CREATE TABLE [dbo].[AdvanceDeclineLine]
(
	[Date]                    DATE NOT NULL,
	[Advancers]               INT  NOT NULL,
	[Decliners]               INT  NOT NULL,
	[Unchanged]               INT  NOT NULL,
	[DailyPlurality]          INT  NOT NULL,
	[CumulativeDifferential]  INT  NOT NULL,
	[XiuClose]                REAL NULL,

	CONSTRAINT [PK_AdvanceDeclineLine] PRIMARY KEY CLUSTERED ([Date])
);
