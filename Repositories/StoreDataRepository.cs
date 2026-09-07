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

        // Database Transaction Procedures
        public async Task BeginTransactionAsync()
        {
            try
            {
                await ExecuteNonQueryAsync("BEGIN TRANSACTION;");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not execute BEGIN TRANSACTION; standard query. Trying standard BEGIN;");
                try
                {
                    await ExecuteNonQueryAsync("BEGIN;");
                }
                catch (Exception exInner)
                {
                    throw new DatabaseException("Failed to begin transaction.", exInner);
                }
            }
        }

        public async Task CommitTransactionAsync()
        {
            try
            {
                await ExecuteNonQueryAsync("COMMIT TRANSACTION;");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not execute COMMIT TRANSACTION; standard query. Trying standard COMMIT;");
                try
                {
                    await ExecuteNonQueryAsync("COMMIT;");
                }
                catch (Exception exInner)
                {
                    throw new DatabaseException("Failed to commit transaction.", exInner);
                }
            }
        }

        public async Task RollbackTransactionAsync()
        {
            try
            {
                await ExecuteNonQueryAsync("ROLLBACK TRANSACTION;");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not execute ROLLBACK TRANSACTION; standard query. Trying standard ROLLBACK;");
                try
                {
                    await ExecuteNonQueryAsync("ROLLBACK;");
                }
                catch (Exception exInner)
                {
                    throw new DatabaseException("Failed to rollback transaction.", exInner);
                }
            }
        }

        // Database Sequence Generators
        public async Task<string> GetPrefixFieldValueAsync(string tableName, string seqTable, string fieldName, string sval, string transId, bool onlyGet, int digits, string userName, double newRecId)
        {
            return await GetPrefixFieldValueCoreAsync(tableName, seqTable, fieldName, sval, transId, onlyGet, digits, userName, newRecId, manageTransaction: true);
        }

        private async Task<string> GetPrefixFieldValueCoreAsync(string tableName, string seqTable, string fieldName, string sval, string transId, bool onlyGet, int digits, string userName, double newRecId, bool manageTransaction)
        {
            if (!onlyGet && manageTransaction)
            {
                await BeginTransactionAsync();
            }

            try
            {
            string seqTableName = $"{tableName}{seqTable}";
            string[] parts = sval.Split(',');
            string s = parts[0];
            string fval = s.StartsWith(":") ? s.Substring(1) : s;

            string pval = await GetFieldDataFromDBAsync(tableName, fval, $"{tableName}id = {newRecId}", string.Empty);
            if (string.IsNullOrEmpty(pval))
            {
                pval = fval;
            }

            string aTransId = parts.Length > 3 && !string.IsNullOrEmpty(parts[3]) ? parts[3] : transId;
            string aFName = parts.Length > 4 && !string.IsNullOrEmpty(parts[4]) ? parts[4] : fieldName;

            string sql = $"SELECT lastno, noofdigits FROM {seqTableName} WHERE LOWER(transtype) = '{aTransId.ToLower()}' AND LOWER(fieldname) = '{aFName.ToLower()}' AND LOWER(prefix) = '{pval.ToLower()}'";
            var table = await ExecuteQueryAsync(sql);

            string lastNo = "1";
            if (table == null || table.Rows.Count == 0)
            {
                if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                {
                    lastNo = parts[1];
                }
                if (parts.Length > 2 && !string.IsNullOrEmpty(parts[2]))
                {
                    int.TryParse(parts[2], out digits);
                }

                if (!onlyGet)
                {
                    double seqId = await GetTableIdAsync(seqTable);
                    if (seqId == 0)
                    {
                        seqId = new Random().Next(100000, 999999);
                    }

                    int nextVal = int.Parse(lastNo) + 1;
                    string insertSql = $"INSERT INTO {seqTableName} (sequenceid, prefix, transtype, fieldname, activesequence, description, prefixfield, lastno, noofdigits) " +
                                       $"VALUES ({seqId}, '{pval}', '{aTransId}', '{aFName}', 'F', '~dynamic prefix', '', {nextVal}, {digits})";
                    await ExecuteNonQueryAsync(insertSql);
                }
            }
            else
            {
                var row = table.Rows[0];
                lastNo = row["lastno"]?.ToString() ?? "1";
                if (int.TryParse(row["noofdigits"]?.ToString(), out int dbDigits))
                {
                    digits = dbDigits;
                }

                if (!onlyGet)
                {
                    int nextVal = int.Parse(lastNo) + 1;
                    string updateSql = $"UPDATE {seqTableName} SET lastno = {nextVal} WHERE LOWER(transtype) = '{aTransId.ToLower()}' AND LOWER(fieldname) = '{aFName.ToLower()}' AND LOWER(prefix) = '{pval.ToLower()}'";
                    await ExecuteNonQueryAsync(updateSql);
                }
            }

            string formattedNo = lastNo.PadLeft(digits, '0');
            string result = pval + formattedNo;

            if (!onlyGet && manageTransaction)
            {
                await CommitTransactionAsync();
            }

            return result;
            }
            catch
            {
                if (!onlyGet && manageTransaction)
                {
                    await RollbackTransactionAsync();
                }
                throw;
            }
        }

        public async Task<string> GetLastNoAsync(string tableName, string seqTable, string fieldName, string transId, bool onlyGet, string userName, double newRecId)
        {
            if (!onlyGet)
            {
                await BeginTransactionAsync();
            }

            try
            {
            string seqTableName = $"{tableName}{seqTable}";
            string prefix = await GetUserActivePrefixAsync(tableName, transId, fieldName, userName);

            string sql = $"SELECT lastno, prefixfield, prefix, noofdigits FROM {seqTableName} WHERE LOWER(transtype) = '{transId.ToLower()}' AND LOWER(fieldname) = '{fieldName.ToLower()}'";
            if (string.IsNullOrEmpty(prefix))
            {
                sql += " AND activesequence = 'T'";
            }
            else
            {
                sql += $" AND LOWER(prefix) = '{prefix.ToLower()}'";
            }

            var table = await ExecuteQueryAsync(sql);
            if (table == null || table.Rows.Count == 0)
            {
                if (!onlyGet)
                {
                    await CommitTransactionAsync();
                }
                return string.Empty;
            }

            var row = table.Rows[0];
            string lastNo = row["lastno"]?.ToString() ?? "1";
            string prefixField = (row["prefixfield"]?.ToString() ?? string.Empty).Trim();
            string rowPrefix = (row["prefix"]?.ToString() ?? string.Empty).Trim();
            int.TryParse(row["noofdigits"]?.ToString(), out int digits);

            string sPrefix = rowPrefix;
            if (!string.IsNullOrEmpty(prefixField))
            {
                if (prefixField.StartsWith(":"))
                {
                    string dynamicPrefixResult = await GetPrefixFieldValueCoreAsync(tableName, seqTable, fieldName, prefixField, transId, onlyGet, digits, userName, newRecId, manageTransaction: false);
                    if (!onlyGet)
                    {
                        await CommitTransactionAsync();
                    }
                    return dynamicPrefixResult;
                }
                sPrefix = prefixField;
            }

            string formattedNo = lastNo.PadLeft(digits, '0');
            string result = sPrefix + formattedNo;

            if (!onlyGet)
            {
                int nextVal = int.Parse(lastNo) + 1;
                string whereCond = $"LOWER(transtype) = '{transId.ToLower()}' AND LOWER(fieldname) = '{fieldName.ToLower()}'";
                if (string.IsNullOrEmpty(prefix))
                {
                    whereCond += " AND activesequence = 'T'";
                }
                else
                {
                    whereCond += $" AND LOWER(prefix) = '{prefix.ToLower()}'";
                }

                string updateSQL = $"UPDATE {seqTableName} SET lastno = {nextVal} WHERE {whereCond}";
                await ExecuteNonQueryAsync(updateSQL);
            }

            if (!onlyGet)
            {
                await CommitTransactionAsync();
            }
            return result;
            }
            catch
            {
                if (!onlyGet)
                {
                    await RollbackTransactionAsync();
                }
                throw;
            }
        }

        public async Task<string> GetUserActivePrefixAsync(string tableName, string transId, string fieldName, string userName)
        {
            string sql = $"SELECT prefix FROM {tableName}USERSEQUENCE WHERE LOWER(transtype) = '{transId.ToLower()}' AND LOWER(fieldname) = '{fieldName.ToLower()}' AND LOWER(uname) = '{userName.ToLower()}'";
            var table = await ExecuteQueryAsync(sql);
            if (table == null || table.Rows.Count == 0)
            {
                return string.Empty;
            }
            return table.Rows[0]["prefix"]?.ToString()?.Trim() ?? string.Empty;
        }

        public async Task<string> GetActivePrefixAsync(string tableName, string seqTable, string transId, string fieldName, string userName)
        {
            string prefix = await GetUserActivePrefixAsync(tableName, transId, fieldName, userName);
            string sql = $"SELECT prefixfield FROM {tableName}{seqTable} WHERE LOWER(transtype) = '{transId.ToLower()}' AND LOWER(fieldname) = '{fieldName.ToLower()}'";
            if (string.IsNullOrEmpty(prefix))
            {
                sql += " AND activesequence = 'T'";
            }
            else
            {
                sql += $" AND prefix = '{prefix}'";
            }

            var table = await ExecuteQueryAsync(sql);
            if (table == null || table.Rows.Count == 0)
            {
                return string.Empty;
            }
            return table.Rows[0]["prefixfield"]?.ToString()?.Trim() ?? string.Empty;
        }

        public async Task SetPrefixAsync(string tableName, string seqTable, string transId, string fieldName, string prefix, string userName)
        {
            string seqTableName = $"{tableName}{seqTable}";
            string userSeqTableName = $"{tableName}USERSEQUENCE";

            await BeginTransactionAsync();
            try
            {
                string updateActiveToFalse = $"UPDATE {seqTableName} SET activesequence = 'F' WHERE LOWER(transtype) = '{transId.ToLower()}' AND LOWER(fieldname) = '{fieldName.ToLower()}' AND activesequence = 'T'";
                await ExecuteNonQueryAsync(updateActiveToFalse);

                string updatePrefixToActive = $"UPDATE {seqTableName} SET activesequence = 'T' WHERE LOWER(transtype) = '{transId.ToLower()}' AND LOWER(fieldname) = '{fieldName.ToLower()}' AND prefix = '{prefix}'";
                await ExecuteNonQueryAsync(updatePrefixToActive);

                int count = await GetDataRowCountAsync(userSeqTableName, "prefix", $"LOWER(transtype) = '{transId.ToLower()}' AND LOWER(fieldname) = '{fieldName.ToLower()}' AND LOWER(uname) = '{userName.ToLower()}'", string.Empty);

                if (count > 0)
                {
                    string updateSQL = $"UPDATE {userSeqTableName} SET prefix = '{prefix}' WHERE LOWER(transtype) = '{transId.ToLower()}' AND LOWER(fieldname) = '{fieldName.ToLower()}' AND LOWER(uname) = '{userName.ToLower()}'";
                    await ExecuteNonQueryAsync(updateSQL);
                }
                else
                {
                    string insertSQL = $"INSERT INTO {userSeqTableName} (transtype, fieldname, uname, prefix) VALUES ('{transId}', '{fieldName}', '{userName}', '{prefix}')";
                    await ExecuteNonQueryAsync(insertSQL);
                }

                await CommitTransactionAsync();
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
        }

        // History Recording & Change Tracking
        public async Task<int> GetNoofTimesModifiedAsync(string tableName, string transId, double recordId)
        {
            string sql = $"SELECT MAX(modno) FROM {tableName}{transId}history WHERE recordid = {recordId}";
            var table = await ExecuteQueryAsync(sql);
            if (table == null || table.Rows.Count == 0 || table.Rows[0][0] == DBNull.Value)
            {
                return 0;
            }
            int.TryParse(table.Rows[0][0]?.ToString(), out int modNo);
            return modNo;
        }

        public async Task SaveHistoryToDBAsync(string tableName, string transId, double recordId, string userName, string fieldName, int modNo, int frameNo, int parentRow, double tableRecId, double idValue, double oldIdValue, string newValue, string oldValue, string delflag, bool transDeleted, bool isNewTrans, bool isCancelTrans, string cancelRemarks)
        {
            string histTableName = $"{tableName}{transId}history";
            string sql;

            if (isNewTrans)
            {
                sql = $"INSERT INTO {histTableName} (modifieddate, recordid, username, tablerecid, newtrans, canceltrans, cancelremarks) " +
                      $"VALUES (GETDATE(), {recordId}, '{userName}', {recordId}, 't', 'f', '')";
            }
            else
            {
                string transDelStr = transDeleted ? "t" : "f";
                sql = $"INSERT INTO {histTableName} (modifieddate, recordid, username, fieldname, modno, frameno, parentrow, tablerecid, idvalue, oldidvalue, newtrans, canceltrans, cancelremarks, newvalue, oldvalue, delflag, transdeleted) " +
                      $"VALUES (GETDATE(), {recordId}, '{userName}', '{fieldName}', {modNo + 1}, {frameNo}, {parentRow}, {tableRecId}, {idValue}, {oldIdValue}, 'f', '{(isCancelTrans ? "t" : "f")}', '{cancelRemarks.Replace("'", "''")}', '{newValue.Replace("'", "''")}', '{oldValue.Replace("'", "''")}', '{delflag}', '{transDelStr}')";
            }

            await ExecuteNonQueryAsync(sql);
        }

        public async Task SaveHistoryToTableAsync(string tableName, string transId, double recordId, string changedValueText, int modNo, bool transDeleted)
        {
            string histTableName = $"{tableName}{transId}history";
            int count = await GetDataRowCountAsync(histTableName, "recordid", $"recordid = {recordId}", string.Empty);
            string transDelStr = transDeleted ? "t" : "f";

            if (count > 0)
            {
                string existingVal = await GetFieldDataFromDBAsync(histTableName, "ChangedValue", $"recordid = {recordId}", string.Empty);
                string newVal = existingVal + changedValueText;
                string updateSql = $"UPDATE {histTableName} SET modno = {modNo}, transdeleted = '{transDelStr}', ChangedValue = '{newVal.Replace("'", "''")}' WHERE recordid = {recordId}";
                await ExecuteNonQueryAsync(updateSql);
            }
            else
            {
                string sql = $"INSERT INTO {histTableName} (recordid, modno, transdeleted, ChangedValue) VALUES ({recordId}, {modNo}, '{transDelStr}', '{changedValueText.Replace("'", "''")}')";
                await ExecuteNonQueryAsync(sql);
            }
        }

        public async Task SaveCancelRemarksToHistoryAsync(string tableName, string transId, double recordId, string userName, string cancelRemarks, string? changedValueText = null)
        {
            string histTableName = $"{tableName}{transId}history";
            string sql = $"INSERT INTO {histTableName} (modifieddate, recordid, username, newtrans, canceltrans, cancelremarks, ChangedValue) " +
                          $"VALUES (GETDATE(), {recordId}, '{userName}', 'f', 't', '{cancelRemarks.Replace("'", "''")}', '{changedValueText?.Replace("'", "''") ?? ""}')";
            await ExecuteNonQueryAsync(sql);
        }

        // Outbound & Site Synchronization
        public async Task InsertIntoOutboundTableAsync(string tableName, string transId, double recordId, string userName)
        {
            string outboundTable = $"{tableName}outbound";
            string checkSql = $"SELECT outboundid FROM {outboundTable} WHERE transid = '{transId}' AND recordid = {recordId} AND senton IS NULL";
            var table = await ExecuteQueryAsync(checkSql);

            if (table != null && table.Rows.Count > 0)
            {
                string outboundId = table.Rows[0]["outboundid"]?.ToString() ?? "0";
                string updateSql = $"UPDATE {outboundTable} SET username = '{userName}', modifiedon = GETDATE() WHERE outboundid = {outboundId}";
                await ExecuteNonQueryAsync(updateSql);
            }
            else
            {
                string insertSql = $"INSERT INTO {outboundTable} (transid, recordid, oaction, username, modifiedon, senton) VALUES ('{transId}', {recordId}, NULL, '{userName}', NULL, NULL)";
                try
                {
                    await ExecuteNonQueryAsync(insertSql);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to insert into outbound table. Trying Oracle sequence sequence format.");
                    string oracleInsert = $"INSERT INTO {outboundTable} (outboundid, transid, recordid, oaction, username, modifiedon, senton) VALUES ({tableName}outbound_seq.nextval, '{transId}', {recordId}, NULL, '{userName}', NULL, NULL)";
                    await ExecuteNonQueryAsync(oracleInsert);
                }
            }
        }

        public async Task<bool> CheckInsertToOutboundAsync(string tableName, string transId)
        {
            string sql = $"SELECT COUNT(*) FROM {tableName}axpexchange WHERE transid = '{transId}' AND adxinout = 'o'";
            var table = await ExecuteQueryAsync(sql);
            if (table != null && table.Rows.Count > 0)
            {
                int.TryParse(table.Rows[0][0]?.ToString(), out int count);
                return count > 0;
            }
            return false;
        }
    }
}
