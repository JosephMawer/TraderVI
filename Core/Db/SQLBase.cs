using Core.Utilities;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
//using Microsoft.Data.SqlClient;
//using Microsoft.Data.SqlClient;
using System.Threading.Tasks;

namespace Core.Db
{
    /// <summary>
    /// Asynchronous data access base class that only supports parameterized queries
    /// </summary>
    public class SQLBase
    {

        //Server=localhost;Database=master;Trusted_Connection=True;

        /// <summary>
        /// The connection string for an instance
        /// </summary>
        internal string ConnectionString = "Server=localhost;Database=TraderDB;Trusted_Connection=True;TrustServerCertificate=True;";
            //Utils.GetConnectionString;//@"Data Source=c:\noso\stocks.db;Version=3;";//"Data Source=.;Initial Catalog=Db;Integrated Security=True;";


        public static string Database = "Server=localhost;Database=MyDatabase;Trusted_Connection=True;";
            //Utils.GetConnectionString;//@"Data Source=c:\noso\stocks.db;Version=3;";

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="ConnectionString"></param>
        public SQLBase()
        {

        }

        //TODO: GRAB ALL SCRIPTS FROM DB SO THAT WE CAN CREATE ALL TABLES ON INITIALIZATION
        public bool SetupInitialTables()
        {
            // run each script


            throw new NotImplementedException();
        }

        public SQLBase(string Schema, string Fields)
        {
            this.Schema = Schema;
            this.Fields = Fields;
            Projection = $"SELECT {this.Fields} FROM {this.Schema}";
        }

        public static string DateTimeSQLite(DateTime datetime)
        {
            const string dateTimeFormat = "{0}-{1}-{2} {3}:{4}:{5}";
            return string.Format(dateTimeFormat, datetime.Year, datetime.Month.ToString("D2"), datetime.Day.ToString("D2"), "00", "00", "00");
        }

        /// <summary>
        /// The database schema  
        /// </summary>
        private protected readonly string Schema;

        /// <summary>
        /// A list of fields that matches the SoftwareCode table
        /// </summary>
        private protected readonly string Fields;

        /// <summary>
        /// The projection
        /// </summary>
        private protected readonly string Projection;


        /// <summary>
        /// Checks database to see if a record exists
        /// </summary>
        /// <param name="query"></param>
        /// <param name="parameters">A list of parameters</param>
        /// <returns>True if there is exactly one unique record, otherwise returns false</returns>
        protected async Task<bool> FindRecordAsync(string query, List<SqlParameter> parameters)
        {
            using (SqlConnection con = new SqlConnection(ConnectionString))
            {
                await con.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    if (parameters != null)
                        cmd.Parameters.AddRange(parameters.ToArray());

                    return ((int)await cmd.ExecuteScalarAsync() == 1) ? true : false;
                }
            }
        }

        ///// <summary>
        ///// Gets a datatable from the query
        ///// </summary>
        ///// <param name="query"></param>
        ///// <returns></returns>
        //protected async Task<DataTable> GetSafeDataTable(string query)
        //{
        //    var dt = new DataTable();
        //    using (SqlConnection con = new SqlConnection(ConnectionString))
        //    {
        //        await con.OpenAsync();
        //        using (SqlCommand cmd = new SqlCommand(query, con))
        //        {
        //            using (var ds = new SqlDataAdapter(cmd))
        //            {
        //                ds.Fill(dt);
        //            }
        //        }
        //    }

        //    return dt;
        //}

        /// <summary>
        /// Inserts a record into the database
        /// </summary>
        /// <param name="query">The query string</param>
        /// <param name="parameters">Parameters as a list of <see cref="SqlParameter"/></param>
        /// <returns>The ID of the row inserted</returns>
        public async Task Insert(string query, List<SqlParameter> parameters)
            => await ExecuteNonQueryAsync(query, parameters);

        /// <summary>
        /// Updates a record
        /// </summary>
        /// <param name="qeury">The query string</param>
        /// <param name="parameters">Parameters as a list of <see cref="SqlParameter"/></param>
        /// <returns></returns>
        protected async Task Update(string qeury, List<SqlParameter> parameters)
            => await ExecuteNonQueryAsync(qeury, parameters);

        /// <summary>
        /// Deletes a record from the database
        /// </summary>
        /// <param name="query">The query string</param>
        /// <param name="parameters">Parameters as a list of <see cref="SqlParameter"/></param>
        /// <returns></returns>
        protected async Task Delete(string query, List<SqlParameter> parameters)
            => await ExecuteNonQueryAsync(query, parameters);

        /// <summary>
        /// Executes a query against the database
        /// </summary>
        /// <param name="query">The query string</param>
        /// <param name="parameters">Parameters as a list of <see cref="SqlParameter"/></param>
        /// <returns>Returns the first column of the first row</returns>
        public async Task<T> ExecuteScalarAsync<T>(string query)
        {
            using (var con = new SqlConnection(ConnectionString))
            {
                await con.OpenAsync();
                using (var cmd = new SqlCommand(query, con))
                {
                    return (T)await cmd.ExecuteScalarAsync();
                }
            }
        }


        /// <summary>
        /// Executes a query against the database
        /// </summary>
        /// <param name="query">The query string</param>
        /// <param name="parameters">Parameters as a list of <see cref="SqlParameter"/></param>
        /// <returns>Returns the first column of the first row, typically the primary key</returns>
        private async Task ExecuteNonQueryAsync(string query, List<SqlParameter> parameters)
        {
            using (var con = new SqlConnection(ConnectionString))
            {
                await con.OpenAsync();
                using (var cmd = new SqlCommand(query, con))
                {
                    cmd.Parameters?.AddRange(parameters.ToArray());

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        ///// <summary>
        ///// Allows user to submit an SQL qeury 
        ///// </summary>
        ///// <param name="sql"></param>
        ///// <returns></returns>
        //public async Task<DataTable> GetDataTableFromSQL(string sql)
        //{
        //    DataTable dt = new DataTable();
        //    using (var con = new SqlConnection(ConnectionString))
        //    {
        //        await con.OpenAsync();
        //        using (var cmd = new SqlCommand(sql, con))
        //        {
        //            using (var adapter = new sqlitea(cmd))
        //            {
        //                adapter.Fill(dt);
        //                return dt;
        //            }
        //        }
        //    }
        //}
    }


    /// <summary>
    /// Asynchronous data access base class that only supports parameterized queries
    /// </summary>
    //public class SQLBase
    //{

    //    //Server=localhost;Database=master;Trusted_Connection=True;

    //    /// <summary>
    //    /// The connection string for an instance
    //    /// </summary>
    //    internal string ConnectionString = "Data Source=.;Initial Catalog=Db;Integrated Security=True;";

    //    /// <summary>
    //    /// Default constructor
    //    /// </summary>
    //    /// <param name="ConnectionString"></param>
    //    public SQLBase()
    //    {

    //    }

    //    //TODO: GRAB ALL SCRIPTS FROM DB SO THAT WE CAN CREATE ALL TABLES ON INITIALIZATION
    //    public bool SetupInitialTables()
    //    {
    //        // run each script
    //        throw new NotImplementedException();
    //    }

    //    public SQLBase(string Schema, string Fields)
    //    {
    //        this.Schema = Schema;
    //        this.Fields = Fields;
    //        Projection = $"SELECT {this.Fields} FROM {this.Schema}";
    //    }

    //    /// <summary>
    //    /// The database schema  
    //    /// </summary>
    //    private protected readonly string Schema;

    //    /// <summary>
    //    /// A list of fields that matches the SoftwareCode table
    //    /// </summary>
    //    private protected readonly string Fields;

    //    /// <summary>
    //    /// The projection
    //    /// </summary>
    //    private protected readonly string Projection;


    //    /// <summary>
    //    /// Checks database to see if a record exists
    //    /// </summary>
    //    /// <param name="query"></param>
    //    /// <param name="parameters">A list of parameters</param>
    //    /// <returns>True if there is exactly one unique record, otherwise returns false</returns>
    //    protected async Task<bool> FindRecordAsync(string query, List<SqlParameter> parameters)
    //    {
    //        using (SqlConnection con = new SqlConnection(ConnectionString))
    //        {
    //            await con.OpenAsync();
    //            using (SqlCommand cmd = new SqlCommand(query, con))
    //            {
    //                if (parameters != null)
    //                    cmd.Parameters.AddRange(parameters.ToArray());

    //                return ((int)await cmd.ExecuteScalarAsync() == 1) ? true : false;
    //            }
    //        }
    //    }

    //    /// <summary>
    //    /// Gets a datatable from the query
    //    /// </summary>
    //    /// <param name="query"></param>
    //    /// <returns></returns>
    //    protected async Task<DataTable> GetSafeDataTable(string query)
    //    {
    //        var dt = new DataTable();
    //        using (SqlConnection con = new SqlConnection(ConnectionString))
    //        {
    //            await con.OpenAsync();
    //            using (SqlCommand cmd = new SqlCommand(query, con))
    //            {
    //                using (var ds = new SqlDataAdapter(cmd))
    //                {
    //                    ds.Fill(dt);
    //                }
    //            }
    //        }

    //        return dt;
    //    }

    //    /// <summary>
    //    /// Inserts a record into the database
    //    /// </summary>
    //    /// <param name="query">The query string</param>
    //    /// <param name="parameters">Parameters as a list of <see cref="SqlParameter"/></param>
    //    /// <returns>The ID of the row inserted</returns>
    //    public async Task Insert(string query, List<SqlParameter> parameters)
    //        => await ExecuteNonQueryAsync(query, parameters);

    //    /// <summary>
    //    /// Updates a record
    //    /// </summary>
    //    /// <param name="qeury">The query string</param>
    //    /// <param name="parameters">Parameters as a list of <see cref="SqlParameter"/></param>
    //    /// <returns></returns>
    //    protected async Task Update(string qeury, List<SqlParameter> parameters)
    //        => await ExecuteNonQueryAsync(qeury, parameters);

    //    /// <summary>
    //    /// Deletes a record from the database
    //    /// </summary>
    //    /// <param name="query">The query string</param>
    //    /// <param name="parameters">Parameters as a list of <see cref="SqlParameter"/></param>
    //    /// <returns></returns>
    //    protected async Task Delete(string query, List<SqlParameter> parameters)
    //        => await ExecuteNonQueryAsync(query, parameters);

    //    /// <summary>
    //    /// Executes a query against the database
    //    /// </summary>
    //    /// <param name="query">The query string</param>
    //    /// <param name="parameters">Parameters as a list of <see cref="SqlParameter"/></param>
    //    /// <returns>Returns the first column of the first row</returns>
    //    public async Task<T> ExecuteScalarAsync<T>(string query)
    //    {
    //        using (var con = new SqlConnection(ConnectionString))
    //        {
    //            await con.OpenAsync();
    //            using (var cmd = new SqlCommand(query, con)) {
    //                return (T)await cmd.ExecuteScalarAsync();
    //            }
    //        }
    //    }


    //    /// <summary>
    //    /// Executes a query against the database
    //    /// </summary>
    //    /// <param name="query">The query string</param>
    //    /// <param name="parameters">Parameters as a list of <see cref="SqlParameter"/></param>
    //    /// <returns>Returns the first column of the first row, typically the primary key</returns>
    //    private async Task ExecuteNonQueryAsync(string query, List<SqlParameter> parameters)
    //    {
    //        using (var con = new SqlConnection(ConnectionString))
    //        {
    //            await con.OpenAsync();
    //            using (var cmd = new SqlCommand(query, con))
    //            {
    //                cmd.Parameters?.AddRange(parameters.ToArray());

    //                await cmd.ExecuteNonQueryAsync();
    //            }
    //        }
    //    }

    //    /// <summary>
    //    /// Allows user to submit an SQL qeury 
    //    /// </summary>
    //    /// <param name="sql"></param>
    //    /// <returns></returns>
    //    public async Task<DataTable> GetDataTableFromSQL(string sql)
    //    {
    //        DataTable dt = new DataTable();
    //        using (var con = new SqlConnection(ConnectionString))
    //        {
    //            await con.OpenAsync();
    //            using (var cmd = new SqlCommand(sql, con))
    //            {
    //                using (var adapter = new SqlDataAdapter(cmd))
    //                {
    //                    adapter.Fill(dt);
    //                    return dt;
    //                }
    //            }
    //        }
    //    }
    //}
}
