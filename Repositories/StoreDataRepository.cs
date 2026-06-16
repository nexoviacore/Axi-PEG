using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AxExtend.Interface;
using AxPeg.Repositories.Interfaces;
using AxPeg.Exceptions;
using Serilog;

namespace AxPeg.Repositories
{
    public class StoreDataRepository : IStoreDataRepository
    {
        private readonly IAxExtend _axExtend;

        public StoreDataRepository(IAxExtend axExtend)
        {
            _axExtend = axExtend;
        }

        public async Task<bool> OpenConnectionAsync(string appName)
        {
            try
            {
                return await _axExtend.OpenDBConnectionAsync(appName);
            }
            catch (Exception ex)
            {
                throw new DatabaseException($"Failed to open database connection for app: {appName}.", ex);
            }
        }

        public async Task CloseConnectionAsync()
        {
            try
            {
                await _axExtend.CloseDBConnectionAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to close database connection cleanly.");
            }
        }

        public async Task<DataTable> ExecuteQueryAsync(string sql)
        {
            try
            {
                var db = await _axExtend.GetDB();
                if (db == null)
                {
                    throw new DatabaseException("Database connector is not available. Ensure connection is opened.");
                }

                var result = await db.ExecuteSQLAsync(sql);
                if (!string.IsNullOrEmpty(result.error))
                {
                    throw new DatabaseException($"Database query execution failed: {result.error}");
                }

                return result.data;
            }
            catch (Exception ex) when (!(ex is DatabaseException))
            {
                throw new DatabaseException($"Error executing query: {sql}.", ex);
            }
        }

        public async Task<int> ExecuteNonQueryAsync(string sql)
        {
            try
            {
                var db = await _axExtend.GetDB();
                if (db == null)
                {
                    throw new DatabaseException("Database connector is not available. Ensure connection is opened.");
                }

                var result = await db.ExecuteNonQueryAsync(sql);
                if (!string.IsNullOrEmpty(result.error))
                {
                    throw new DatabaseException($"Database non-query execution failed: {result.error}");
                }

                return result.count;
            }
            catch (Exception ex) when (!(ex is DatabaseException))
            {
                throw new DatabaseException($"Error executing non-query: {sql}.", ex);
            }
        }

        public async Task<string> GetFieldDataFromDBAsync(string tableName, string fieldName, string whereCond, string orderBy, bool fetchAll = false)
        {
            string sql = $"SELECT {fieldName} FROM {tableName}";
            if (!string.IsNullOrWhiteSpace(whereCond))
            {
                sql += $" WHERE {whereCond}";
            }
            if (!string.IsNullOrWhiteSpace(orderBy))
            {
                sql += $" ORDER BY {orderBy}";
            }

            var table = await ExecuteQueryAsync(sql);
            if (table == null || table.Rows.Count == 0)
            {
                return string.Empty;
            }

            if (fetchAll)
            {
                var values = table.AsEnumerable()
                    .Select(row => row[fieldName]?.ToString())
                    .Where(val => !string.IsNullOrEmpty(val));
                return string.Join(",", values);
            }

            return table.Rows[0][fieldName]?.ToString() ?? string.Empty;
        }

        public async Task<int> GetDataRowCountAsync(string tableName, string fieldName, string whereCond, string orderBy)
        {
            string sql = $"SELECT COUNT({fieldName}) FROM {tableName}";
            if (!string.IsNullOrWhiteSpace(whereCond))
            {
                sql += $" WHERE {whereCond}";
            }
            if (!string.IsNullOrWhiteSpace(orderBy))
            {
                sql += $" ORDER BY {orderBy}";
            }

            var table = await ExecuteQueryAsync(sql);
            if (table == null || table.Rows.Count == 0)
            {
                return 0;
            }

            int.TryParse(table.Rows[0][0]?.ToString(), out int count);
            return count;
        }

        public async Task<string> GetRecIDAsync(string tableName, string whereCond)
        {
            return await GetFieldDataFromDBAsync(tableName, "recid", whereCond, string.Empty);
        }

        public async Task<double> GetTableIdAsync(string tableName)
        {
            string val = await GetFieldDataFromDBAsync("axsequece", "nextval", $"tablename = '{tableName}'", string.Empty);
            if (double.TryParse(val, out double tableId))
            {
                return tableId;
            }
            return 0;
        }

        public string ConvertSDtoRapidSaveInputJSON(System.Collections.Generic.List<Dtos.FieldRecord> fieldList, string appName, string userName, string transId)
        {
            try
            {
                var rapidsaveObject = new System.Text.Json.Nodes.JsonObject();
                var dataObject = new System.Text.Json.Nodes.JsonObject();
                var data1Object = new System.Text.Json.Nodes.JsonObject();

                dataObject.Add("axpapp", appName);
                dataObject.Add("username", userName);
                dataObject.Add("trace", "");
                dataObject.Add("transid", transId);
                dataObject.Add("keyfield", "");
                dataObject.Add("primaryfield", "");

                // Sort the field list: primarily by FrameNo, then by RowNo
                var sortedList = fieldList
                    .OrderBy(f => f.FrameNo)
                    .ThenBy(f => f.RowNo)
                    .ToList();

                bool isFirstTime = true;
                System.Text.Json.Nodes.JsonObject? currentDc = null;
                System.Text.Json.Nodes.JsonObject? currentRow = null;
                int oldFrameNo = -1;
                int oldRowNo = -1;

                foreach (var fld in sortedList)
                {
                    if (fld.FrameNo != oldFrameNo)
                    {
                        currentDc = new System.Text.Json.Nodes.JsonObject();
                        data1Object.Add("dc" + fld.FrameNo, currentDc);
                        oldFrameNo = fld.FrameNo;
                        oldRowNo = -1; // Reset row tracking for new frame
                    }

                    if (fld.RowNo != oldRowNo)
                    {
                        currentRow = new System.Text.Json.Nodes.JsonObject();
                        currentDc?.Add("row" + fld.RowNo, currentRow);
                        oldRowNo = fld.RowNo;
                    }

                    currentRow?.Add(fld.FieldName, fld.Value);

                    if (isFirstTime)
                    {
                        if (fld.RecordId == 0)
                        {
                            data1Object.Add("mode", "new");
                            data1Object.Add("keyvalue", "");
                            data1Object.Add("recordid", "0");
                        }
                        else
                        {
                            data1Object.Add("mode", "edit");
                            data1Object.Add("keyvalue", "");
                            data1Object.Add("recordid", fld.RecordId.ToString());
                        }
                        isFirstTime = false;
                    }
                }

                dataObject.Add("data1", data1Object);
                rapidsaveObject.Add("rapidsave", dataObject);

                return rapidsaveObject.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error converting FieldList to RapidSave JSON string");
                return string.Empty;
            }
        }
    }
}
