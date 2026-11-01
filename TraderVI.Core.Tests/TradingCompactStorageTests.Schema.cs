#nullable enable
using Core.Db;
using Core.Trader.Storage;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    private static string Schema(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CompactStorageSql", name));

    [Fact]
    public void SqlDefinitionsMatchEveryMappedScalarColumnAndPreparedMigration()
    {
        string migration = Schema("20261030_054_AddCompactTradingCurrentState.sql").Replace("\r\n", "\n");
        string expiryMigration = Schema("20261031_055_AllowExpiredCompactOrderReceipt.sql").Replace("\r\n", "\n");
        foreach (var table in TradingTable.All)
        {
            string sql = Schema(table.Name + ".sql").Replace("\r\n", "\n");
            // 055 changes only the status lists in the order table and its immutable-outcome trigger.
            if (table.RowType == typeof(TradingOrder))
            {
                var changedChecks = sql.Split('\n').Where(line => line.Contains("N'Expired'")).ToArray();
                Assert.Equal(2, changedChecks.Length);
                foreach (string check in changedChecks)
                    Assert.Contains("WITH CHECK ADD " + check.Trim().TrimEnd(',') + ";", expiryMigration);
            }
            Assert.Contains(sql.Trim().Replace(",N'Expired'", ""), migration);
            Assert.DoesNotContain("nvarchar(max)", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(table.Columns.Select(c => c.Name).Order(), table.RowType.GetProperties().Select(p => p.Name).Order());
            foreach (var column in table.Columns)
            {
                string declaration = @"\[" + column.Name + @"\]\s+" + Regex.Escape(column.SqlType) +
                    @"(?:\s+COLLATE\s+\w+)?\s+" + (column.Nullable ? "NULL" : "NOT NULL") + @"\s*,";
                Assert.Matches(declaration, sql);
                var type = Nullable.GetUnderlyingType(table.RowType.GetProperty(column.Name)!.PropertyType) ?? table.RowType.GetProperty(column.Name)!.PropertyType;
                Assert.Contains(type, new[] { typeof(Guid), typeof(long), typeof(int), typeof(decimal), typeof(bool), typeof(string), typeof(DateTime) });
            }
        }
        foreach (string file in new[] { "TradingCurrentState.sql", "TradingApprovedSettings_Immutable.sql" })
            Assert.Contains(Schema(file).Replace("\r\n", "\n").Trim(), migration);
        string trigger = Schema("TradingOrder_IntentImmutable.sql").Replace("\r\n", "\n").Trim();
        Assert.Contains(trigger.Replace("CREATE TRIGGER", "ALTER TRIGGER"), expiryMigration);
        Assert.Contains(trigger.Replace(",N'Expired'", ""), migration);
    }

    [Fact]
    public void CurrentSqlReadsOnlyAccountScopedActiveRowsWithMatchingIndexes()
    {
        string sql = SqlTradingStorageDatabase.CurrentReadSql;
        Assert.Equal(7, sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length);
        Assert.DoesNotContain("SELECT *", sql, StringComparison.OrdinalIgnoreCase);
        string indexes = Schema("TradingCurrentState.sql");
        foreach (var table in TradingTable.All.Where(t => t.ActiveColumn is not null))
        {
            Assert.Contains($"FROM dbo.[{table.Name}] WITH (INDEX([IX_{table.Name}_Active])) WHERE [BindingId]=@BindingId AND [{table.ActiveColumn}]=1;", sql);
            Assert.Contains($"ON [dbo].[{table.Name}] ([BindingId]", indexes);
            Assert.Contains($"WHERE [{table.ActiveColumn}]=1", indexes);
        }
        Assert.Contains("a.PermissionId=s.PermissionId", sql);
        Assert.Contains("([BindingId],[DecisionId])", indexes);
        Assert.Contains("([BindingId],[BrokerOrderId])", indexes);
    }

    [Fact]
    public async Task MissingMarkerRejectsEvenAnOtherwiseValidWrite()
    {
        var (db, state) = await Started();
        int writes = db.CommittedWrites;
        db.Rows.Remove((typeof(TradingCheckpoint), Binding, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with
        { Account = state.Account with { ObservedCash = 100 } }));
        Assert.Equal(writes, db.CommittedWrites);
    }

    [Fact]
    public async Task DifferentRequestsCannotClaimTheSameBrokerOrder()
    {
        var (db, state) = await Started();
        await db.Store.SaveAsync(Change(state) with { Orders = [Order() with { BrokerOrderId = "broker-1" }] });
        state = (await db.Store.ReadAsync(Binding))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with
        { Orders = [Order(2) with { BrokerOrderId = "broker-1" }] }));
        Assert.Single((await db.Store.ReadAsync(Binding))!.Orders);
    }

    [Fact]
    public async Task CurrentReadsAndWritesCannotCrossAccounts()
    {
        var (db, state) = await Started();
        Guid other = Guid.NewGuid();
        Assert.Null(await db.Store.ReadAsync(other));
        await db.Store.SaveAsync(Initial() with { Account = Account with { BindingId = other }, Settings = [Settings with { BindingId = other }] });
        await db.Store.SaveAsync(Change(state) with { Positions = [Position()] });
        Assert.Single((await db.Store.ReadAsync(Binding))!.Positions);
        Assert.Empty((await db.Store.ReadAsync(other))!.Positions);
    }
}
