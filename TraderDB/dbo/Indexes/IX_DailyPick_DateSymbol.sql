CREATE UNIQUE INDEX [IX_DailyPick_DateSymbol]
	ON [dbo].[DailyPick] ([PickDate], [Symbol], [Lens]);
