using System;
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

        public async Task<bool> ApproveTaskAsync(string appName, string taskId, string userName, string comments)
        {
            try
            {
                await _dbRepo.OpenConnectionAsync(appName);
                Log.Information("Approving task {TaskId} by user {UserName}", taskId, userName);

                // Update active tasks status
                string updateSql = $"UPDATE axactivetasks SET status = 'Approved', approvedby = '{userName}', approvedon = GETDATE(), comments = '{comments}' WHERE taskid = '{taskId}' AND status = 'Active'";
                int rows = await _dbRepo.ExecuteNonQueryAsync(updateSql);
                
                if (rows > 0)
                {
                    // Push audit or notification message to Queue
                    await _rmqPublisher.PushToQueueAsync(appName, "peg_notifications", $"{{\"taskId\":\"{taskId}\",\"action\":\"Approve\",\"user\":\"{userName}\"}}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
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

                string updateSql = $"UPDATE axactivetasks SET status = 'Rejected', approvedby = '{userName}', approvedon = GETDATE(), comments = '{comments}' WHERE taskid = '{taskId}' AND status = 'Active'";
                int rows = await _dbRepo.ExecuteNonQueryAsync(updateSql);

                if (rows > 0)
                {
                    await _rmqPublisher.PushToQueueAsync(appName, "peg_notifications", $"{{\"taskId\":\"{taskId}\",\"action\":\"Reject\",\"user\":\"{userName}\"}}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
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

                string updateSql = $"UPDATE axactivetasks SET status = 'Forwarded', approvedby = '{userName}', approvedon = GETDATE(), comments = '{comments}', forwardedto = '{forwardToUser}' WHERE taskid = '{taskId}' AND status = 'Active'";
                int rows = await _dbRepo.ExecuteNonQueryAsync(updateSql);

                if (rows > 0)
                {
                    await _rmqPublisher.PushToQueueAsync(appName, "peg_notifications", $"{{\"taskId\":\"{taskId}\",\"action\":\"Forward\",\"from\":\"{userName}\",\"to\":\"{forwardToUser}\"}}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
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

                string updateSql = $"UPDATE axactivetasks SET status = 'Returned', approvedby = '{userName}', approvedon = GETDATE(), comments = '{comments}' WHERE taskid = '{taskId}' AND status = 'Active'";
                int rows = await _dbRepo.ExecuteNonQueryAsync(updateSql);

                if (rows > 0)
                {
                    await _rmqPublisher.PushToQueueAsync(appName, "peg_notifications", $"{{\"taskId\":\"{taskId}\",\"action\":\"Return\",\"user\":\"{userName}\"}}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
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
                    foreach (System.Data.DataRow row in activeTasks.Rows)
                    {
                        string targetTaskId = row["taskid"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(targetTaskId))
                        {
                            // Update status to Skipped
                            string updateSql = $"UPDATE axactivetasks SET status = 'Skipped' WHERE taskid = '{targetTaskId}'";
                            await _dbRepo.ExecuteNonQueryAsync(updateSql);

                            // Insert into status history table
                            string insertHistory = $"INSERT INTO axactivetaskstatus (taskid, status, statusdate) VALUES ('{targetTaskId}', 'skipped', GETDATE())";
                            await _dbRepo.ExecuteNonQueryAsync(insertHistory);

                            Log.Information("Task {TaskId} successfully skipped as part of index group sync.", targetTaskId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing SkipAndUpdateAllOtherTasksWithSameIndexAsync for process {ProcessName}", processName);
            }
            finally
            {
                await _dbRepo.CloseConnectionAsync();
            }
        }
    }
}
