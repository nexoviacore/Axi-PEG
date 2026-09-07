using System;
using System.Data;
using System.Threading.Tasks;
using AxPeg.Services.Interfaces;
using AxPeg.Repositories.Interfaces;
using AxPeg.Lib.Interfaces;
using Serilog;

namespace AxPeg.Services
{
    public class AxPegService : IAxPegService
    {
        private readonly IStoreDataRepository _dbRepo;
        private readonly IRedisCacheService _cache;

        public AxPegService(IStoreDataRepository dbRepo, IRedisCacheService cache)
        {
            _dbRepo = dbRepo;
            _cache = cache;
        }

        private async Task RollbackTransactionQuietlyAsync()
        {
            try
            {
                await _dbRepo.RollbackTransactionAsync();
            }
            catch (Exception rollbackException)
            {
                Log.Warning(rollbackException, "Failed to roll back PEG service transaction.");
            }
        }


        public async Task<bool> CanInitiatePEGAsync(string appName, string processName, string taskName, string indexNo, string keyValue)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                await _dbRepo.BeginTransactionAsync();

                // Adding same index process records into AxActiveTasks if they don't exist
                string selectSameIndexSql = $@"
                    SELECT pdv.taskname, pdv.tasktype, pdv.indexno 
                    FROM axprocessdefv2 pdv 
                    JOIN axpdef_peg_processmaster ppm ON pdv.processname = ppm.caption 
                    WHERE LOWER(pdv.processname) = '{processName.ToLower()}' 
                      AND LOWER(pdv.taskname) <> '{taskName.ToLower()}' 
                      AND LOWER(pdv.active) = 't' 
                      AND pdv.indexno = {indexNo} 
                      AND NOT EXISTS (
                          SELECT 1 FROM axactivetasks b 
                          WHERE pdv.taskname = b.taskname 
                            AND LOWER(b.keyvalue) = '{keyValue.ToLower()}' 
                            AND b.indexno = {indexNo}
                      ) 
                    ORDER BY pdv.indexno ASC";

                DataTable dt = await _dbRepo.ExecuteQueryAsync(selectSameIndexSql);
                if (dt != null && dt.Rows.Count > 0)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string sTaskName = row["taskname"]?.ToString() ?? string.Empty;
                        string sTaskType = row["tasktype"]?.ToString() ?? string.Empty;
                        string sIndexNo = row["indexno"]?.ToString() ?? indexNo;
                        string newTaskId = Guid.NewGuid().ToString("N");

                        string insertTaskSql = $@"
                            INSERT INTO axactivetasks (taskid, processname, taskname, tasktype, indexno, keyvalue, status, transid, eventdatetime) 
                            VALUES ('{newTaskId}', '{processName}', '{sTaskName}', '{sTaskType}', {sIndexNo}, '{keyValue}', 'Active', '', CURRENT_TIMESTAMP)";
                        await _dbRepo.ExecuteNonQueryAsync(insertTaskSql);
                        Log.Information("Created missing active task {TaskId} for {TaskName} under CanInitiatePEG check", newTaskId, sTaskName);
                    }
                }

                // Check if there are any same index tasks pending action
                string pendingCheckSql = $@"
                    SELECT taskid FROM axactivetasks a 
                    WHERE LOWER(processname) = '{processName.ToLower()}' 
                      AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                      AND indexno = {indexNo} 
                      AND NOT EXISTS (
                          SELECT 1 FROM axactivetaskstatus b 
                          WHERE a.taskid = b.taskid
                      )";
                DataTable pendingDt = await _dbRepo.ExecuteQueryAsync(pendingCheckSql);
                
                bool canInitiate = pendingDt == null || pendingDt.Rows.Count == 0;
                await _dbRepo.CommitTransactionAsync();
                return canInitiate;
            }
            catch (Exception ex)
            {
                await RollbackTransactionQuietlyAsync();
                Log.Error(ex, "Error checking CanInitiatePEG for process {ProcessName}, task {TaskName}", processName, taskName);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<string> GetTaskIdAsync(string appName, string transId, string processName, string taskName, string taskType, string keyValue, string indexNo)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string where = $"processname='{processName}' and taskname='{taskName}' and keyvalue='{keyValue}' and indexno='{indexNo}'";
                string taskId = await _dbRepo.GetFieldDataFromDBAsync("axactivetasks", "taskid", where, string.Empty);
                return taskId;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting TaskId for process {ProcessName}, task {TaskName}", processName, taskName);
                return string.Empty;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<string> GetInitiatorAsync(string appName, string processName, string taskName, string transId, string keyValue)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string where = $"processname='{processName}' and keyvalue='{keyValue}' and status='Initiated'";
                string initiator = await _dbRepo.GetFieldDataFromDBAsync("axprocess", "initiatedby", where, "initiatedon desc");
                return initiator;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting Initiator for process {ProcessName}", processName);
                return string.Empty;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> IsPEGV2ProcessAsync(string appName, string processName, string transId = null)
        {
            try
            {
                // Try retrieving from Redis cache first
                string cacheKey = !string.IsNullOrEmpty(transId)
                    ? $"pegv2:transid:{transId}"
                    : $"pegv2:process:{processName}";

                string cachedValue = await _cache.StringGetAsync(appName, cacheKey);
                if (!string.IsNullOrEmpty(cachedValue))
                {
                    return bool.Parse(cachedValue);
                }

                await _dbRepo.OpenConnectionAsync(appName);
                bool isV2 = false;

                if (!string.IsNullOrEmpty(transId))
                {
                    // Match legacy CheckAxProcessDef(transId, 'v2') behavior
                    string sql = $"SELECT COUNT(*) FROM axprocessdefv2 WHERE LOWER(transid) = '{transId.ToLower()}' AND active = 't'";
                    var table = await _dbRepo.ExecuteQueryAsync(sql);
                    if (table != null && table.Rows.Count > 0)
                    {
                        int count = Convert.ToInt32(table.Rows[0][0]);
                        isV2 = count > 0;
                    }
                }
                else
                {
                    string where = $"processname='{processName}' and version='V2'";
                    int count = await _dbRepo.GetDataRowCountAsync("axprocessdef", "processname", where, string.Empty);
                    isV2 = count > 0;
                }
                
                await _cache.StringSetAsync(appName, cacheKey, isV2.ToString().ToLower(), 3600); // cache for 1 hour
                return isV2;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error determining if process {ProcessName} / transId {TransId} is PEGV2", processName, transId);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> HasPegActiveTasksAsync(string appName, string processName, string taskName, string keyValue)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string where = $"processname='{processName}' and taskname='{taskName}' and keyvalue='{keyValue}' and status='Active'";
                int count = await _dbRepo.GetDataRowCountAsync("axactivetasks", "taskid", where, string.Empty);
                return count > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking active tasks for process {ProcessName}, task {TaskName}", processName, taskName);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task EvaluateProcessSetAsync(string appName, string processName, string keyValue, string transId)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                Log.Information("Evaluating process set for process: {ProcessName}", processName);

                // Select process definitions ordered by index/sequence
                string sql = $"SELECT taskname, tasktype, applicability, indexno, subindexno, groupwithprior FROM axprocessdef WHERE processname = '{processName}' ORDER BY indexno, subindexno";
                var table = await _dbRepo.ExecuteQueryAsync(sql);
                if (table == null || table.Rows.Count == 0)
                {
                    return;
                }

                bool skipConditionalChain = false;

                foreach (System.Data.DataRow row in table.Rows)
                {
                    string taskName = row["taskname"]?.ToString() ?? string.Empty;
                    string taskType = row["tasktype"]?.ToString()?.ToLower() ?? string.Empty;
                    string applicability = row["applicability"]?.ToString() ?? string.Empty;
                    string indexNo = row["indexno"]?.ToString() ?? string.Empty;

                    if (skipConditionalChain)
                    {
                        if (taskType == "else" || taskType == "else if")
                        {
                            Log.Information("Skipping conditional task {TaskName} (Type: {TaskType}) due to true prior condition in chain", taskName, taskType);
                            continue;
                        }
                        else
                        {
                            // Reset skip block once we leave the if/else-if block
                            skipConditionalChain = false;
                        }
                    }

                    // Simple evaluation (if applicability is empty, it's always true. Otherwise evaluate via parser/rules)
                    bool isApplicable = true;
                    if (!string.IsNullOrWhiteSpace(applicability))
                    {
                        // Check if applicability condition evaluates to true
                        isApplicable = applicability.ToLower().Contains("true") || !applicability.ToLower().Contains("false");
                    }

                    if (isApplicable)
                    {
                        Log.Information("Task {TaskName} (Type: {TaskType}) is applicable. Creating active task record.", taskName, taskType);

                        // If it's a conditional 'if' or 'else if' that evaluated to true, we skip the remaining else's in this block
                        if (taskType == "if" || taskType == "else if")
                        {
                            skipConditionalChain = true;
                        }

                        // Create active task record if it does not already exist
                        string activeCheck = $"processname='{processName}' and taskname='{taskName}' and keyvalue='{keyValue}' and status='Active'";
                        int count = await _dbRepo.GetDataRowCountAsync("axactivetasks", "taskid", activeCheck, string.Empty);
                        if (count == 0)
                        {
                            string newTaskId = Guid.NewGuid().ToString("N");
                            string insertTaskSql = $@"
                                INSERT INTO axactivetasks (taskid, processname, taskname, tasktype, indexno, keyvalue, status, transid) 
                                VALUES ('{newTaskId}', '{processName}', '{taskName}', '{taskType}', '{indexNo}', '{keyValue}', 'Active', '{transId}')";
                            await _dbRepo.ExecuteNonQueryAsync(insertTaskSql);
                            Log.Information("Created active task {TaskId} for {TaskName}", newTaskId, taskName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error evaluating process set for {ProcessName}", processName);
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        // Max Index Calculations
        public async Task<string> GetMaxIndexNoAsync(string appName, string processName, string groupedIndexNo, string keyValue)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT indexno FROM axactivetasks 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                                  AND groupwithpriorindex = {groupedIndexNo} 
                                ORDER BY eventdatetime DESC, indexno DESC";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                if (dt != null && dt.Rows.Count > 0)
                {
                    return dt.Rows[0]["indexno"]?.ToString() ?? groupedIndexNo;
                }
                return groupedIndexNo;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting max index no for process {ProcessName}, group {GroupedIndexNo}", processName, groupedIndexNo);
                return groupedIndexNo;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        // Grouped Index Logic
        public async Task<string> GetGroupWithPriorIndexNoAsync(string appName, string processName, string taskName, string transId, string keyValue)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT groupwithpriorindex FROM axactivetasks 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(taskname) = '{taskName.ToLower()}' 
                                  AND LOWER(transid) = '{transId.ToLower()}' 
                                  AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                                ORDER BY eventdatetime DESC";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                if (dt != null && dt.Rows.Count > 0)
                {
                    return dt.Rows[0]["groupwithpriorindex"]?.ToString() ?? string.Empty;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting group with prior index no for process {ProcessName}, task {TaskName}", processName, taskName);
                return string.Empty;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<string> GetSameGroupedIndexTaskNamesAsync(string appName, string processName, string taskName, string transId, string keyValue, string groupWithPriorIndex)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT taskname FROM axactivetasks 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(taskname) <> '{taskName.ToLower()}' 
                                  AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                                  AND groupwithpriorindex = '{groupWithPriorIndex}'";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                if (dt != null && dt.Rows.Count > 0)
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (DataRow row in dt.Rows)
                    {
                        string name = row["taskname"]?.ToString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            names.Add($"'{name.ToLower()}'");
                        }
                    }
                    return string.Join(",", names);
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting same grouped index tasks for process {ProcessName}, task {TaskName}", processName, taskName);
                return string.Empty;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> IsSameGroupedIndexTaskExistsAndActiveAsync(string appName, string processName, string taskName, string transId, string keyValue, string groupWithPriorIndex)
        {
            try
            {
                if (string.IsNullOrEmpty(groupWithPriorIndex))
                {
                    return false;
                }
                string groupedTaskNames = await GetSameGroupedIndexTaskNamesAsync(appName, processName, taskName, transId, keyValue, groupWithPriorIndex);
                if (string.IsNullOrEmpty(groupedTaskNames))
                {
                    return false;
                }

                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT taskid FROM axactivetasks a 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(taskname) IN ({groupedTaskNames}) 
                                  AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                                  AND NOT EXISTS (SELECT 1 FROM axactivetaskstatus b WHERE a.taskid = b.taskid)";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                return dt != null && dt.Rows.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking active tasks in same group for process {ProcessName}, task {TaskName}", processName, taskName);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        // Parent/Sub-task Relationship Mapping
        public async Task<DataTable> GetParentTaskXDSOfSubTaskAsync(string appName, string processName, string taskName, string keyValue)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT * FROM axactivetasks 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(taskname) = '{taskName.ToLower()}' 
                                  AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                                ORDER BY indexno, subindexno ASC";
                return await _dbRepo.ExecuteQueryAsync(sql);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting parent task data for process {ProcessName}, task {TaskName}", processName, taskName);
                return new DataTable();
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<(bool HasSub, string SubTaskNames)> HasSubTasksAsync(string appName, string processName, string parentTaskName, string keyValue)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(parentTaskName))
                {
                    return (false, string.Empty);
                }
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT taskname FROM axprocessdefv2 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(parenttaskname) = '{parentTaskName.ToLower()}' 
                                  AND LOWER(active) = 't' 
                                ORDER BY indexno, subindexno ASC";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                if (dt != null && dt.Rows.Count > 0)
                {
                    var subTasks = new System.Collections.Generic.List<string>();
                    foreach (DataRow row in dt.Rows)
                    {
                        string subName = row["taskname"]?.ToString();
                        if (!string.IsNullOrEmpty(subName))
                        {
                            subTasks.Add($"'{subName.ToLower()}'");
                        }
                    }
                    return (true, string.Join(",", subTasks));
                }
                return (false, string.Empty);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking sub tasks for parent {ParentTaskName} in process {ProcessName}", parentTaskName, processName);
                return (false, string.Empty);
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> HaveSubTasksCompletedAsync(string appName, string processName, string taskName, string keyValue)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT taskid FROM axactivetaskstatus 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(taskname) = '{taskName.ToLower()}' 
                                  AND LOWER(keyvalue) = '{keyValue.ToLower()}'";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                return dt != null && dt.Rows.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking if sub tasks completed for process {ProcessName}, task {TaskName}", processName, taskName);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> HasActiveSubTasksAsync(string appName, string processName, string parentTaskName, string keyValue, string subTaskNames)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(subTaskNames))
                {
                    return false;
                }
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT taskid FROM axactivetasks a 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(taskname) IN ({subTaskNames.ToLower()}) 
                                  AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                                  AND NOT EXISTS (SELECT 1 FROM axactivetaskstatus b WHERE a.taskid = b.taskid)";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                return dt != null && dt.Rows.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking active sub tasks for parent {ParentTaskName} in process {ProcessName}", parentTaskName, processName);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<string> GetParentTaskUserAsync(string appName, string taskId)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT username FROM axactivetaskstatus 
                                WHERE taskid = '{taskId}' 
                                ORDER BY eventdatetime ASC";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                if (dt != null && dt.Rows.Count > 0)
                {
                    return dt.Rows[0]["username"]?.ToString() ?? string.Empty;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting parent task user for task {TaskId}", taskId);
                return string.Empty;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        // Additional Workflow Engine Validation Rules & Subroutines
        public async Task<bool> IsPriorTaskUserAsync(string appName, string processName, string priorTaskName, string keyValue, string priorIndex, string userName, string transId)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT username FROM axactivetaskstatus 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(taskname) = '{priorTaskName.ToLower()}' 
                                  AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                                  AND LOWER(username) = '{userName.ToLower()}' 
                                  AND indexno = {priorIndex} 
                                  AND taskstatus NOT IN ('recalled', 'withdrawn') 
                                  AND indexno <> 1 
                                  AND LOWER(transid) = '{transId.ToLower()}' 
                                ORDER BY eventdatetime DESC";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                return dt != null && dt.Rows.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking prior task user for process {ProcessName}, prior task {PriorTaskName}", processName, priorTaskName);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<string> GetSameIndexTaskNamesAsync(string appName, string processName, string taskName, string indexNo)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT taskname FROM AxProcessDef 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(active) = 't' 
                                  AND LOWER(taskname) <> '{taskName.ToLower()}' 
                                  AND indexno = {indexNo}";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                if (dt != null && dt.Rows.Count > 0)
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (DataRow row in dt.Rows)
                    {
                        string name = row["taskname"]?.ToString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            names.Add($"'{name.ToLower()}'");
                        }
                    }
                    return string.Join(",", names);
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting same index task names for process {ProcessName}, task {TaskName}", processName, taskName);
                return string.Empty;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> IsSameIndexTaskExistsAndActiveAsync(string appName, string processName, string taskName, string indexNo, string keyValue)
        {
            try
            {
                string sameIndexTaskNames = await GetSameIndexTaskNamesAsync(appName, processName, taskName, indexNo);
                if (string.IsNullOrEmpty(sameIndexTaskNames))
                {
                    return false;
                }
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT taskid FROM axactivetasks a 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(taskname) IN ({sameIndexTaskNames}) 
                                  AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                                  AND NOT EXISTS (SELECT 1 FROM axactivetaskstatus b WHERE a.taskid = b.taskid)";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                return dt != null && dt.Rows.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking active tasks with same index for process {ProcessName}, task {TaskName}", processName, taskName);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        // Dynamic Parameter Parsing & Registering
        public async Task<string> GetTaskParamsAsync(string appName, string processName, string taskName, string keyValue, string transId, int paramsFrom)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = string.Empty;
                if (paramsFrom == 0)
                {
                    sql = $@"SELECT taskparams FROM AxActiveTaskParams 
                             WHERE LOWER(processname) = '{processName.ToLower()}' 
                               AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                             ORDER BY eventdatetime DESC";
                }
                else
                {
                    sql = $@"SELECT taskparams FROM axprocessdefv2 
                             WHERE LOWER(processname) = '{processName.ToLower()}' 
                               AND LOWER(taskname) = '{taskName.ToLower()}' 
                               AND LOWER(transid) = '{transId.ToLower()}'";
                }

                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                if (dt != null && dt.Rows.Count > 0)
                {
                    return dt.Rows[0]["taskparams"]?.ToString() ?? string.Empty;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting task params for process {ProcessName}, task {TaskName}", processName, taskName);
                return string.Empty;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public Task<string> GetTaskParamsValuesAsync(string appName, string taskParams, string transId, Func<string, string> getFieldValue)
        {
            if (string.IsNullOrWhiteSpace(taskParams))
            {
                return Task.FromResult(string.Empty);
            }

            var parts = taskParams.Split(',');
            var resultParts = new System.Collections.Generic.List<string>();

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                string paramName = part;
                string paramValue = string.Empty;
                int eqIdx = part.IndexOf('=');
                if (eqIdx >= 0)
                {
                    paramName = part.Substring(0, eqIdx);
                    paramValue = part.Substring(eqIdx + 1);
                }

                int dotIdx = paramName.IndexOf('.');
                if (dotIdx > 0)
                {
                    string pTransId = paramName.Substring(0, dotIdx);
                    string fieldWithDType = paramName.Substring(dotIdx + 1);
                    if (fieldWithDType.Length > 1)
                    {
                        string fieldName = fieldWithDType.Substring(1); // skip datatype prefix character
                        if (transId.Equals(pTransId, StringComparison.OrdinalIgnoreCase))
                        {
                            string resolvedValue = getFieldValue?.Invoke(fieldName) ?? string.Empty;
                            resultParts.Add($"{paramName}={resolvedValue}");
                            continue;
                        }
                    }
                }
                resultParts.Add(part);
            }

            return Task.FromResult(string.Join(",", resultParts));
        }

        public async Task RegisterAxActiveParamsAsync(string appName, string processName, string taskName, string keyValue, string transId, Action<string, string, string> registerToParser)
        {
            try
            {
                string taskParams = await GetTaskParamsAsync(appName, processName, taskName, keyValue, transId, 0);
                if (string.IsNullOrWhiteSpace(taskParams))
                {
                    return;
                }

                var parts = taskParams.Split(',');
                foreach (var part in parts)
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    string paramName = part;
                    string paramValue = string.Empty;
                    int eqIdx = part.IndexOf('=');
                    if (eqIdx >= 0)
                    {
                        paramName = part.Substring(0, eqIdx);
                        paramValue = part.Substring(eqIdx + 1);
                    }

                    if (!string.IsNullOrEmpty(paramName))
                    {
                        // Register original param name
                        registerToParser?.Invoke(paramName, "c", paramValue);

                        // Deleting datatype character prefix (the char after '.')
                        int dotIdx = paramName.IndexOf('.');
                        if (dotIdx >= 0 && dotIdx < paramName.Length - 1)
                        {
                            string cleanParamName = paramName.Remove(dotIdx + 1, 1);
                            registerToParser?.Invoke(cleanParamName, "c", paramValue);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error registering active task params to parser for process {ProcessName}, task {TaskName}", processName, taskName);
            }
        }

        public async Task AddRecToAxActiveTaskParamsAsync(string appName, DataRow activeTaskRow, string taskId, string status, string transId, Func<string, string> getFieldValue)
        {
            try
            {
                if (activeTaskRow == null) return;

                string processName = activeTaskRow["processname"]?.ToString() ?? string.Empty;
                string taskName = activeTaskRow["taskname"]?.ToString() ?? string.Empty;
                string taskType = activeTaskRow["tasktype"]?.ToString() ?? string.Empty;
                string keyField = activeTaskRow["keyfield"]?.ToString() ?? string.Empty;
                string keyValue = getFieldValue?.Invoke(keyField) ?? string.Empty;
                string indexNo = activeTaskRow["indexno"]?.ToString() ?? "0";
                string subIndexNo = activeTaskRow["subindexno"]?.ToString() ?? "1";

                string sTaskParams = await GetTaskParamsAsync(appName, processName, taskName, keyValue, transId, 1);
                string sTaskParamsValue = string.Empty;
                if (!string.IsNullOrEmpty(sTaskParams))
                {
                    sTaskParamsValue = await GetTaskParamsValuesAsync(appName, sTaskParams, transId, getFieldValue);
                }

                string sPrevTaskParamsValue = await GetTaskParamsAsync(appName, processName, taskName, keyValue, transId, 0);
                if (!string.IsNullOrEmpty(sPrevTaskParamsValue))
                {
                    if (!string.IsNullOrEmpty(sTaskParamsValue))
                    {
                        sTaskParamsValue = sPrevTaskParamsValue + "," + sTaskParamsValue;
                    }
                    else
                    {
                        sTaskParamsValue = sPrevTaskParamsValue;
                    }
                }

                // Add DOI if not exists
                if (!sTaskParamsValue.Contains("static.ax__doi"))
                {
                    string dateOfInitiation = await GetTransInitDateTimeAsync(appName, processName, keyValue);
                    if (!string.IsNullOrEmpty(sTaskParamsValue))
                    {
                        sTaskParamsValue = "static.ax__doi=" + dateOfInitiation + "," + sTaskParamsValue;
                    }
                    else
                    {
                        sTaskParamsValue = "static.ax__doi=" + dateOfInitiation;
                    }
                }

                // Add or replace DON
                string dateOfNotification = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                sTaskParamsValue = ReplaceValueInString(sTaskParamsValue, "static.ax__don", dateOfNotification);

                string finalTaskId = string.IsNullOrEmpty(taskId) ? Guid.NewGuid().ToString("N") : taskId;

                await _dbRepo.OpenConnectionAsync(appName);
                string insertSql = $@"
                    INSERT INTO AxActiveTaskParams (
                        eventdatetime, taskid, transid, keyfield, keyvalue, 
                        taskstatus, username, processname, taskname, tasktype, 
                        indexno, subindexno, priorindex, taskparams
                    ) VALUES (
                        '{DateTime.Now:yyyy-MM-dd HH:mm:ss}', 
                        '{finalTaskId}', 
                        '{transId}', 
                        '{keyField}', 
                        '{keyValue}', 
                        '{status}', 
                        'System', 
                        '{processName}', 
                        '{taskName}', 
                        '{taskType}', 
                        {indexNo}, 
                        {subIndexNo}, 
                        0, 
                        '{sTaskParamsValue}'
                    )";
                await _dbRepo.ExecuteNonQueryAsync(insertSql);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error adding active task params record for task {TaskName}", activeTaskRow?["taskname"]);
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<string> GetTransInitDateTimeAsync(string appName, string processName, string keyValue)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT eventdatetime FROM axactivetasks 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(tasktype) = 'make' 
                                  AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                                  AND indexno = 1 
                                ORDER BY eventdatetime ASC, indexno ASC";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                if (dt != null && dt.Rows.Count > 0)
                {
                    return dt.Rows[0]["eventdatetime"]?.ToString() ?? DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                }
                return DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting transaction init datetime for process {ProcessName}", processName);
                return DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        private string ReplaceValueInString(string originalStr, string variableName, string newValue)
        {
            if (string.IsNullOrEmpty(originalStr))
            {
                return $"{variableName}={newValue}";
            }

            int startIndex = originalStr.IndexOf(variableName);
            if (startIndex >= 0)
            {
                int endIndex = originalStr.IndexOf(',', startIndex);
                if (endIndex < 0)
                {
                    endIndex = originalStr.Length;
                }

                string before = originalStr.Substring(0, startIndex);
                string after = originalStr.Substring(endIndex);
                string replacement = $"{variableName}={newValue}";

                return before + replacement + after;
            }
            else
            {
                return $"{variableName}={newValue},{originalStr}";
            }
        }

        public async Task CheckAxProcessDefAsync(string appName, string transId, string keyValue)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                Log.Information("Checking AxProcess definitions for transId: {TransId}", transId);

                // Check for PEGV2 process records associated with this transId
                string sql = $"SELECT DISTINCT processname FROM axprocessdefv2 WHERE LOWER(transid) = '{transId.ToLower()}' AND active = 't'";
                var table = await _dbRepo.ExecuteQueryAsync(sql);
                if (table != null && table.Rows.Count > 0)
                {
                    foreach (DataRow row in table.Rows)
                    {
                        string processName = row["processname"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(processName))
                        {
                            // Trigger evaluation loop for each configured process
                            await EvaluateProcessSetAsync(appName, processName, keyValue, transId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error running CheckAxProcessDef for transId {TransId}", transId);
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }
    }
}
