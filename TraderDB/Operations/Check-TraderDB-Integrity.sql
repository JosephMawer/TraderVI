/*
Purpose: Perform a read-only logical and physical integrity check of TraderDB.
Effect: DBCC CHECKDB is resource-intensive but does not repair or alter data
        because no repair option is specified.
*/

USE [master];
GO

SET NOCOUNT ON;
DBCC CHECKDB ([TraderDB]) WITH NO_INFOMSGS, ALL_ERRORMSGS;
GO
