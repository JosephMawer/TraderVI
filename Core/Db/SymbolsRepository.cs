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
        /// Returns a list of constituents from TSX
        /// </summary>
        /// <param name="count">Optional: the number of constituents to return</param>
        /// <returns>The full list of constituents, if a count is provided it will return count many constituents</returns>
        public async Task<List<SymbolInfo>> GetSymbols()
        {            
            string query = $"SELECT {Fields} FROM {DbName}";

            return await ExecuteReaderAsync(query, reader => new SymbolInfo
            {
                //ShortName = reader.GetString(0),
                Symbol = reader.GetString(0)
            });
        }
    }
}