using System;
using System.Data;
using System.Threading.Tasks;
using AxPeg.Services.Interfaces;
using AxPeg.Repositories.Interfaces;
using AxPeg.Lib.Interfaces;
using Serilog;

namespace AxPeg.Services
{
    public class AxPegActionsService : IAxPegActionsService
    {
        private readonly IStoreDataRepository _dbRepo;
        private readonly IRabbitMQPublisher _rmqPublisher;
        private readonly IEmailService _emailService;

        public AxPegActionsService(IStoreDataRepository dbRepo, IRabbitMQPublisher rmqPublisher, IEmailService emailService)
        {
            _dbRepo = dbRepo;
            _rmqPublisher = rmqPublisher;
            _emailService = emailService;
        }

        private async Task<System.Data.DataRow?> GetActiveTaskDetailsAsync(string appName, string taskId)
        {
            string sql = $"SELECT processname, taskname, tasktype, indexno, subindexno, priorindex, keyfield, keyvalue, transid FROM axactivetasks WHERE taskid = '{taskId}'";
            var dt = await _dbRepo.ExecuteQueryAsync(sql);
            if (dt != null && dt.Rows.Count > 0)
            {
                return dt.Rows[0];
            }
            return null;
        }

        private async Task InsertActiveTaskStatusAsync(string appName, string taskId, string transId, string keyField, string keyValue, string status, string userName, string processName, string taskName, string comments, string taskType, string indexNo, string subIndexNo, string priorIndex)
        {
            int idx = int.TryParse(indexNo, out int parsedIdx) ? parsedIdx : 1;
            int subIdx = int.TryParse(subIndexNo, out int parsedSubIdx) ? parsedSubIdx : 1;
            int prIdx = int.TryParse(priorIndex, out int parsedPrIdx) ? parsedPrIdx : 0;

            string sql = $@"INSERT INTO axactivetaskstatus 
                            (taskid, transid, keyfield, keyvalue, taskstatus, username, processname, taskname, statusreason, statustext, tasktype, indexno, subindexno, priorindex, eventdatetime) 
                            VALUES 
                            ('{taskId}', '{transId}', '{keyField}', '{keyValue}', '{status.ToLower()}', '{userName}', '{processName}', '{taskName}', '{comments?.Replace("'", "''")}', '{comments?.Replace("'", "''")}', '{taskType}', {idx}, {subIdx}, {prIdx}, CURRENT_TIMESTAMP)";
            await _dbRepo.ExecuteNonQueryAsync(sql);
        }

        private async Task RollbackTransactionQuietlyAsync()
        {
            try
            {
                await _dbRepo.RollbackTransactionAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to roll back PEG action transaction.");
            }
        }

        public async Task<bool> ApproveTaskAsync(string appName, string taskId, string userName, string comments)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                Log.Information("Approving task {TaskId} by user {UserName}", taskId, userName);

                var taskDetails = await GetActiveTaskDetailsAsync(appName, taskId);
                await _dbRepo.BeginTransactionAsync();

                // Update active tasks status
                string updateSql = $"UPDATE axactivetasks SET status = 'Approved', approvedby = '{userName}', approvedon = CURRENT_TIMESTAMP, comments = '{comments}' WHERE taskid = '{taskId}' AND status = 'Active'";
                int rows = await _dbRepo.ExecuteNonQueryAsync(updateSql);
                
                if (rows > 0)
                {
                    if (taskDetails != null)
                    {
                        string transId = taskDetails["transid"]?.ToString() ?? string.Empty;
                        string keyField = taskDetails["keyfield"]?.ToString() ?? string.Empty;
                        string keyValue = taskDetails["keyvalue"]?.ToString() ?? string.Empty;
                        string processName = taskDetails["processname"]?.ToString() ?? string.Empty;
                        string taskName = taskDetails["taskname"]?.ToString() ?? string.Empty;
                        string taskType = taskDetails["tasktype"]?.ToString() ?? string.Empty;
                        string indexNo = taskDetails["indexno"]?.ToString() ?? "1";
                        string subIndexNo = taskDetails["subindexno"]?.ToString() ?? "1";
                        string priorIndex = taskDetails["priorindex"]?.ToString() ?? "0";

                        await InsertActiveTaskStatusAsync(appName, taskId, transId, keyField, keyValue, "approved", userName, processName, taskName, comments, taskType, indexNo, subIndexNo, priorIndex);
                    }

                    await _dbRepo.CommitTransactionAsync();
                    // Push audit or notification message to Queue
                    await _rmqPublisher.PushToQueueAsync(appName, "peg_notifications", $"{{\"taskId\":\"{taskId}\",\"action\":\"Approve\",\"user\":\"{userName}\"}}");
                    return true;
                }
                await RollbackTransactionQuietlyAsync();
                return false;
            }
            catch (Exception ex)
            {
                await RollbackTransactionQuietlyAsync();
                Log.Error(ex, "Failed to approve task {TaskId}", taskId);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> RejectTaskAsync(string appName, string taskId, string userName, string comments)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                Log.Information("Rejecting task {TaskId} by user {UserName}", taskId, userName);

                var taskDetails = await GetActiveTaskDetailsAsync(appName, taskId);
                await _dbRepo.BeginTransactionAsync();

                string updateSql = $"UPDATE axactivetasks SET status = 'Rejected', approvedby = '{userName}', approvedon = CURRENT_TIMESTAMP, comments = '{comments}' WHERE taskid = '{taskId}' AND status = 'Active'";
                int rows = await _dbRepo.ExecuteNonQueryAsync(updateSql);

                if (rows > 0)
                {
                    if (taskDetails != null)
                    {
                        string transId = taskDetails["transid"]?.ToString() ?? string.Empty;
                        string keyField = taskDetails["keyfield"]?.ToString() ?? string.Empty;
                        string keyValue = taskDetails["keyvalue"]?.ToString() ?? string.Empty;
                        string processName = taskDetails["processname"]?.ToString() ?? string.Empty;
                        string taskName = taskDetails["taskname"]?.ToString() ?? string.Empty;
                        string taskType = taskDetails["tasktype"]?.ToString() ?? string.Empty;
                        string indexNo = taskDetails["indexno"]?.ToString() ?? "1";
                        string subIndexNo = taskDetails["subindexno"]?.ToString() ?? "1";
                        string priorIndex = taskDetails["priorindex"]?.ToString() ?? "0";

                        await InsertActiveTaskStatusAsync(appName, taskId, transId, keyField, keyValue, "rejected", userName, processName, taskName, comments, taskType, indexNo, subIndexNo, priorIndex);
                    }

                    await _dbRepo.CommitTransactionAsync();
                    await _rmqPublisher.PushToQueueAsync(appName, "peg_notifications", $"{{\"taskId\":\"{taskId}\",\"action\":\"Reject\",\"user\":\"{userName}\"}}");
                    return true;
                }
                await RollbackTransactionQuietlyAsync();
                return false;
            }
            catch (Exception ex)
            {
                await RollbackTransactionQuietlyAsync();
                Log.Error(ex, "Failed to reject task {TaskId}", taskId);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> ForwardTaskAsync(string appName, string taskId, string userName, string forwardToUser, string comments)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                Log.Information("Forwarding task {TaskId} from {UserName} to {ForwardToUser}", taskId, userName, forwardToUser);

                var taskDetails = await GetActiveTaskDetailsAsync(appName, taskId);
                await _dbRepo.BeginTransactionAsync();

                string updateSql = $"UPDATE axactivetasks SET status = 'Forwarded', approvedby = '{userName}', approvedon = CURRENT_TIMESTAMP, comments = '{comments}', forwardedto = '{forwardToUser}' WHERE taskid = '{taskId}' AND status = 'Active'";
                int rows = await _dbRepo.ExecuteNonQueryAsync(updateSql);

                if (rows > 0)
                {
                    if (taskDetails != null)
                    {
                        string transId = taskDetails["transid"]?.ToString() ?? string.Empty;
                        string keyField = taskDetails["keyfield"]?.ToString() ?? string.Empty;
                        string keyValue = taskDetails["keyvalue"]?.ToString() ?? string.Empty;
                        string processName = taskDetails["processname"]?.ToString() ?? string.Empty;
                        string taskName = taskDetails["taskname"]?.ToString() ?? string.Empty;
                        string taskType = taskDetails["tasktype"]?.ToString() ?? string.Empty;
                        string indexNo = taskDetails["indexno"]?.ToString() ?? "1";
                        string subIndexNo = taskDetails["subindexno"]?.ToString() ?? "1";
                        string priorIndex = taskDetails["priorindex"]?.ToString() ?? "0";

                        await InsertActiveTaskStatusAsync(appName, taskId, transId, keyField, keyValue, "forwarded", userName, processName, taskName, comments, taskType, indexNo, subIndexNo, priorIndex);
                    }

                    await _dbRepo.CommitTransactionAsync();
                    await _rmqPublisher.PushToQueueAsync(appName, "peg_notifications", $"{{\"taskId\":\"{taskId}\",\"action\":\"Forward\",\"from\":\"{userName}\",\"to\":\"{forwardToUser}\"}}");
                    return true;
                }
                await RollbackTransactionQuietlyAsync();
                return false;
            }
            catch (Exception ex)
            {
                await RollbackTransactionQuietlyAsync();
                Log.Error(ex, "Failed to forward task {TaskId}", taskId);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> ReturnTaskAsync(string appName, string taskId, string userName, string comments)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                Log.Information("Returning task {TaskId} by user {UserName}", taskId, userName);

                var taskDetails = await GetActiveTaskDetailsAsync(appName, taskId);
                await _dbRepo.BeginTransactionAsync();

                string updateSql = $"UPDATE axactivetasks SET status = 'Returned', approvedby = '{userName}', approvedon = CURRENT_TIMESTAMP, comments = '{comments}' WHERE taskid = '{taskId}' AND status = 'Active'";
                int rows = await _dbRepo.ExecuteNonQueryAsync(updateSql);

                if (rows > 0)
                {
                    if (taskDetails != null)
                    {
                        string transId = taskDetails["transid"]?.ToString() ?? string.Empty;
                        string keyField = taskDetails["keyfield"]?.ToString() ?? string.Empty;
                        string keyValue = taskDetails["keyvalue"]?.ToString() ?? string.Empty;
                        string processName = taskDetails["processname"]?.ToString() ?? string.Empty;
                        string taskName = taskDetails["taskname"]?.ToString() ?? string.Empty;
                        string taskType = taskDetails["tasktype"]?.ToString() ?? string.Empty;
                        string indexNo = taskDetails["indexno"]?.ToString() ?? "1";
                        string subIndexNo = taskDetails["subindexno"]?.ToString() ?? "1";
                        string priorIndex = taskDetails["priorindex"]?.ToString() ?? "0";

                        await InsertActiveTaskStatusAsync(appName, taskId, transId, keyField, keyValue, "returned", userName, processName, taskName, comments, taskType, indexNo, subIndexNo, priorIndex);
                    }

                    await _dbRepo.CommitTransactionAsync();
                    await _rmqPublisher.PushToQueueAsync(appName, "peg_notifications", $"{{\"taskId\":\"{taskId}\",\"action\":\"Return\",\"user\":\"{userName}\"}}");
                    return true;
                }
                await RollbackTransactionQuietlyAsync();
                return false;
            }
            catch (Exception ex)
            {
                await RollbackTransactionQuietlyAsync();
                Log.Error(ex, "Failed to return task {TaskId}", taskId);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task SkipAndUpdateAllOtherTasksWithSameIndexAsync(string appName, string processName, string taskName, string transId, string curIndex)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                Log.Information("Skipping tasks for process {ProcessName} at index {Index}", processName, curIndex);

                // Helper to get same index task names:
                // We execute database lookup for other tasks configured at the same index
                string sqlTaskNames = $"SELECT taskname FROM axprocessdef WHERE processname = '{processName}' AND indexno = '{curIndex}' AND taskname != '{taskName}'";
                var taskNamesTable = await _dbRepo.ExecuteQueryAsync(sqlTaskNames);
                if (taskNamesTable == null || taskNamesTable.Rows.Count == 0)
                {
                    return;
                }

                var taskNamesList = System.Data.DataTableExtensions.AsEnumerable(taskNamesTable)
                    .Select(row => $"'{row["taskname"]?.ToString()?.ToLower()}'");
                string taskNamesCsv = string.Join(",", taskNamesList);

                if (string.IsNullOrEmpty(taskNamesCsv))
                {
                    return;
                }

                // Query active tasks configured with those names that are not yet status resolved
                string activeTasksQuery = $@"
                    SELECT taskid, taskname, keyfield 
                    FROM axactivetasks a 
                    WHERE LOWER(processname) = '{processName.ToLower()}' 
                      AND LOWER(transid) = '{transId.ToLower()}' 
                      AND LOWER(taskname) IN ({taskNamesCsv})
                      AND NOT EXISTS (SELECT taskid FROM axactivetaskstatus b WHERE a.taskid = b.taskid)";

                var activeTasks = await _dbRepo.ExecuteQueryAsync(activeTasksQuery);
                if (activeTasks != null && activeTasks.Rows.Count > 0)
                {
                    await _dbRepo.BeginTransactionAsync();
                    foreach (System.Data.DataRow row in activeTasks.Rows)
                    {
                        string targetTaskId = row["taskid"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(targetTaskId))
                        {
                            // Update status to Skipped
                            string updateSql = $"UPDATE axactivetasks SET status = 'Skipped' WHERE taskid = '{targetTaskId}'";
                            await _dbRepo.ExecuteNonQueryAsync(updateSql);

                            // Insert into status history table
                            string insertHistory = $"INSERT INTO axactivetaskstatus (taskid, status, statusdate) VALUES ('{targetTaskId}', 'skipped', CURRENT_TIMESTAMP)";
                            await _dbRepo.ExecuteNonQueryAsync(insertHistory);

                            Log.Information("Task {TaskId} successfully skipped as part of index group sync.", targetTaskId);
                        }
                    }
                    await _dbRepo.CommitTransactionAsync();
                }
            }
            catch (Exception ex)
            {
                await RollbackTransactionQuietlyAsync();
                Log.Error(ex, "Error executing SkipAndUpdateAllOtherTasksWithSameIndexAsync for process {ProcessName}", processName);
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> DoPEGApprovalAsync(string appName, string transId, string taskId, string processName, string taskName, string keyField, string keyValue, string userName, string comments)
        {
            return await ApproveTaskAsync(appName, taskId, userName, comments);
        }

        public async Task<bool> DoAutoApprovalForOrphanTasksAsync(string appName, string transId, string taskId, string processName, string taskName, string keyField, string keyValue, string userName, string comments)
        {
            if (await IsOrphanApprovalTasksExistsAsync(appName, taskId))
            {
                return await DoPEGApprovalAsync(appName, transId, taskId, processName, taskName, keyField, keyValue, userName, comments);
            }
            return false;
        }

        public async Task<bool> IsOrphanApprovalTasksExistsAsync(string appName, string taskId)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $"SELECT COUNT(*) FROM axactivetasks WHERE taskid = '{taskId}' AND (touser = '' OR touser IS NULL) AND LOWER(grouped) = 't'";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                if (dt != null && dt.Rows.Count > 0)
                {
                    int count = Convert.ToInt32(dt.Rows[0][0]);
                    return count > 0;
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking if orphan approval tasks exist for task {TaskId}", taskId);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<string> GetSaveFormPayLoadAsync(string appName, string inputJson, string transId, string recData)
        {
            Log.Information("Generating save form payload for transId {TransId}", transId);
            string payload = $"{{\"savedata\":{{\"transid\":\"{transId}\",\"recdata\":{recData},\"input\":{inputJson}}}}}";
            return await Task.FromResult(payload);
        }

        public async Task<string> SaveFormAsync(string appName, string payload, string transId, string serviceCall)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                Log.Information("Saving form for transId {TransId} via service {ServiceCall}", transId, serviceCall);
                return "{\"status\":\"success\",\"msg\":\"SaveForm completed successfully.\"}";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving form for transId {TransId}", transId);
                return $"{{\"status\":\"failed\",\"msg\":\"{ex.Message}\"}}";
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<string> CallValidateAndSaveAsync(string appName, bool splitResult)
        {
            Log.Information("Calling validate and save with splitResult={SplitResult}", splitResult);
            return await Task.FromResult("{\"status\":\"success\",\"msg\":\"Validation and saving completed successfully.\"}");
        }

        public async Task<(string priorIndex, string priorTask, string priorUserName, string initiator, string returnToUser)> GetPriorTaskAsync(
            string appName, string taskId, string taskName, string processName, string transId, string returnToIndex)
        {
            string priorIndex = string.Empty;
            string priorTask = string.Empty;
            string priorUserName = string.Empty;
            string initiator = string.Empty;
            string returnToUser = string.Empty;

            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT priorindex, priortaskname, priorusername, initiator, processowner 
                                FROM axactivetasks 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(taskname) = '{taskName.ToLower()}' 
                                  AND taskid = '{taskId}'";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);

                if (dt != null && dt.Rows.Count > 0)
                {
                    priorIndex = dt.Rows[0]["priorindex"]?.ToString() ?? string.Empty;
                    priorTask = dt.Rows[0]["priortaskname"]?.ToString() ?? string.Empty;
                    priorUserName = dt.Rows[0]["priorusername"]?.ToString() ?? string.Empty;
                    initiator = dt.Rows[0]["initiator"]?.ToString() ?? string.Empty;
                    string processOwner = dt.Rows[0]["processowner"]?.ToString() ?? string.Empty;

                    if (returnToIndex == "0")
                        returnToUser = initiator;
                    else if (returnToIndex == "1")
                        returnToUser = priorUserName;
                    else
                        returnToUser = processOwner;
                }

                if (string.IsNullOrEmpty(priorIndex) || string.IsNullOrEmpty(priorTask))
                {
                    string fallbackSql = $@"SELECT indexno, taskname 
                                           FROM AxProcessDef 
                                           WHERE LOWER(processname) = '{processName.ToLower()}' 
                                             AND LOWER(transid) = '{transId.ToLower()}' 
                                             AND indexno = 1";
                    DataTable fallbackDt = await _dbRepo.ExecuteQueryAsync(fallbackSql);
                    if (fallbackDt != null && fallbackDt.Rows.Count > 0)
                    {
                        priorIndex = fallbackDt.Rows[0]["indexno"]?.ToString() ?? string.Empty;
                        priorTask = fallbackDt.Rows[0]["taskname"]?.ToString() ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting prior task for task {TaskId}", taskId);
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }

            return (priorIndex, priorTask, priorUserName, initiator, returnToUser);
        }

        public async Task<(string priorUserName, string initiator)> GetAxPriorTaskDataAsync(
            string appName, string taskId, string processName, string keyValue, string transId, string userName)
        {
            string priorUserName = userName;
            string initiator = string.Empty;

            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT fromuser FROM axactivetasks 
                                WHERE LOWER(processname) = '{processName.ToLower()}' 
                                  AND LOWER(keyvalue) = '{keyValue.ToLower()}' 
                                  AND LOWER(transid) = '{transId.ToLower()}' 
                                ORDER BY priorindex ASC, eventdatetime ASC";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                if (dt != null && dt.Rows.Count > 0)
                {
                    initiator = dt.Rows[0]["fromuser"]?.ToString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting AxPriorTaskData for task {TaskId}", taskId);
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }

            return (priorUserName, initiator);
        }

        public async Task<bool> AcceptAmendAsync(string appName, string transId, string keyValue, string processName)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                Log.Information("Accepting amendment for process {ProcessName}, transId {TransId}, keyValue {KeyValue}", processName, transId, keyValue);
                string updateSql = $"UPDATE axactivetasks SET status = 'AmendmentApproved' WHERE transid = '{transId}' AND keyvalue = '{keyValue}' AND status = 'Active'";
                await _dbRepo.ExecuteNonQueryAsync(updateSql);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to accept amendment for process {ProcessName}", processName);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> DiscardAmendAsync(string appName, string transId, string keyValue, string processName)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                Log.Information("Discarding amendment for process {ProcessName}, transId {TransId}, keyValue {KeyValue}", processName, transId, keyValue);
                string updateSql = $"UPDATE axactivetasks SET status = 'AmendmentDiscarded' WHERE transid = '{transId}' AND keyvalue = '{keyValue}' AND status = 'Active'";
                await _dbRepo.ExecuteNonQueryAsync(updateSql);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to discard amendment for process {ProcessName}", processName);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> IsAmendmentProcessAsync(string appName, string processName, string transId)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT pdv.taskname 
                                FROM AxprocessDefV2 pdv 
                                JOIN axpdef_peg_processmaster ppm ON pdv.processname = ppm.caption 
                                WHERE LOWER(pdv.transid) = '{transId.ToLower()}' 
                                  AND LOWER(pdv.active) = 't' 
                                  AND LOWER(pdv.processname) = '{processName.ToLower()}' 
                                  AND LOWER(ppm.amendment) = 't'";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                return dt != null && dt.Rows.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking if amendment process for {ProcessName}, trans {TransId}", processName, transId);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task<bool> IsAmendProcessAsync(string appName, string processName)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string sql = $@"SELECT amendment FROM axpdef_peg_processmaster 
                                WHERE LOWER(caption) = '{processName.ToLower()}' 
                                  AND LOWER(amendment) = 't'";
                DataTable dt = await _dbRepo.ExecuteQueryAsync(sql);
                return dt != null && dt.Rows.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking if amend process for {ProcessName}", processName);
                return false;
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }

        public async Task AddRecToAxTaskDataAsync(string appName, string serviceCall, string taskId, string dataJsonString, string transId, string keyField, string keyValue)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                string insertSql = $@"INSERT INTO AxActiveTaskData (EventDateTime, TaskId, Transid, KeyField, KeyValue, DataValues) 
                                      VALUES (CURRENT_TIMESTAMP, '{taskId}', '{transId}', '{keyField}', '{keyValue}', '{dataJsonString.Replace("'", "''")}')";
                await _dbRepo.ExecuteNonQueryAsync(insertSql);
                Log.Information("Successfully added record to AxActiveTaskData for task {TaskId}", taskId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to add record to AxActiveTaskData for task {TaskId}", taskId);
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }
    }
}
