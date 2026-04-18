using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db
{
    public class SymbolsRepository : SQLBase
    {
        public SymbolsRepository() : base("[Symbols]", "[Symbol]") { }

        public async Task AddSymbol(string name, string symbol)
        {
            var query = $"INSERT INTO {DbName} ({Fields}) VALUES (@name, @symbol)";
            await Insert(query,
             [
                new SqlParameter("@name", SqlDbType.NVarChar, 100) { Value = name },
                new SqlParameter("@symbol", SqlDbType.VarChar, 10) { Value = symbol }
             ]);
        }

        /// <summary>
        /// Returns all active symbols (stocks + ETFs).
        /// Use <see cref="GetEquitiesAsync"/> for the stock-only trading universe.
        /// </summary>
        public async Task<List<SymbolInfo>> GetSymbols()
        {
            string query = $"SELECT [Symbol], [SecurityType] FROM {DbName} WHERE [IsActive] = 1";

            return await ExecuteReaderAsync(query, reader => new SymbolInfo
            {
                Symbol = reader.GetString(0),
                SecurityType = reader.GetString(1)
            });
        }

        /// <summary>
        /// Returns only active equities (excludes ETFs).
        /// Use this for Delphi pick universe and ML training.
        /// </summary>
        public async Task<List<SymbolInfo>> GetEquitiesAsync()
        {
            string query = $"SELECT [Symbol], [SecurityType] FROM {DbName} WHERE [IsActive] = 1 AND [SecurityType] = 'Stock'";

            return await ExecuteReaderAsync(query, reader => new SymbolInfo
            {
                Symbol = reader.GetString(0),
                SecurityType = reader.GetString(1)
            });
        }

        /// <summary>
        /// Returns active symbols filtered by security type.
        /// </summary>
        public async Task<List<SymbolInfo>> GetBySecurityTypeAsync(string securityType)
        {
            string query = $"SELECT [Symbol], [SecurityType] FROM {DbName} WHERE [IsActive] = 1 AND [SecurityType] = @type";

            return await ExecuteReaderAsync(query,
                [new SqlParameter("@type", SqlDbType.NVarChar, 20) { Value = securityType }],
                reader => new SymbolInfo
                {
                    Symbol = reader.GetString(0),
                    SecurityType = reader.GetString(1)
                });
        }

        /// <summary>
        /// Updates the security type for a symbol (e.g. 'Stock' → 'ETF').
        /// </summary>
        public async Task SetSecurityTypeAsync(string symbol, string securityType)
        {
            string query = $"UPDATE {DbName} SET [SecurityType] = @type WHERE [Symbol] = @symbol";
            await Update(query,
            [
                new SqlParameter("@type", SqlDbType.NVarChar, 20) { Value = securityType },
                new SqlParameter("@symbol", SqlDbType.VarChar, 10) { Value = symbol }
            ]);
        }
    }
}