using System;
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

        public async Task<bool> CanInitiatePEGAsync(string appName, string processName, string taskName, string indexNo, string keyValue)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);

                // Emulate check from uAxPEG: MakeTypeRecordExistsInAxActiveTasks
                string whereCond = $"processname = '{processName}' AND taskname = '{taskName}' AND indexno = '{indexNo}' AND keyvalue = '{keyValue}' AND status = 'Active'";
                int count = await _dbRepo.GetDataRowCountAsync("axactivetasks", "taskid", whereCond, string.Empty);

                return count == 0;
            }
            catch (Exception ex)
            {
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

        public async Task<bool> IsPEGV2ProcessAsync(string appName, string processName)
        {
            try
            {
                // Try retrieving from Redis cache first
                string cacheKey = $"pegv2:process:{processName}";
                string cachedValue = await _cache.StringGetAsync(appName, cacheKey);
                if (!string.IsNullOrEmpty(cachedValue))
                {
                    return bool.Parse(cachedValue);
                }

                await _dbRepo.OpenConnectionAsync(appName);
                string where = $"processname='{processName}' and version='V2'";
                int count = await _dbRepo.GetDataRowCountAsync("axprocessdef", "processname", where, string.Empty);
                
                bool isV2 = count > 0;
                await _cache.StringSetAsync(appName, cacheKey, isV2.ToString().ToLower(), 3600); // cache for 1 hour
                return isV2;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error determining if process {ProcessName} is PEGV2", processName);
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
                        // Simple evaluation fallback, normally integrated with an expression evaluator or parser:
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
    }
}
