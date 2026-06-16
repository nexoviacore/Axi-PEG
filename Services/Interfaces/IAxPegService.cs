using System;
using System.Data;
using System.Threading.Tasks;

namespace AxPeg.Services.Interfaces
{
    public interface IAxPegService
    {
        Task<bool> CanInitiatePEGAsync(string appName, string processName, string taskName, string indexNo, string keyValue);
        Task<string> GetTaskIdAsync(string appName, string transId, string processName, string taskName, string taskType, string keyValue, string indexNo);
        Task<string> GetInitiatorAsync(string appName, string processName, string taskName, string transId, string keyValue);
        Task<bool> IsPEGV2ProcessAsync(string appName, string processName);
        Task<bool> HasPegActiveTasksAsync(string appName, string processName, string taskName, string keyValue);
        Task EvaluateProcessSetAsync(string appName, string processName, string keyValue, string transId);
        Task CheckAxProcessDefAsync(string appName, string transId, string keyValue);

        // Max Index Calculations
        Task<string> GetMaxIndexNoAsync(string appName, string processName, string groupedIndexNo, string keyValue);

        // Grouped Index Logic
        Task<string> GetGroupWithPriorIndexNoAsync(string appName, string processName, string taskName, string transId, string keyValue);
        Task<string> GetSameGroupedIndexTaskNamesAsync(string appName, string processName, string taskName, string transId, string keyValue, string groupWithPriorIndex);
        Task<bool> IsSameGroupedIndexTaskExistsAndActiveAsync(string appName, string processName, string taskName, string transId, string keyValue, string groupWithPriorIndex);

        // Parent/Sub-task Relationship Mapping
        Task<DataTable> GetParentTaskXDSOfSubTaskAsync(string appName, string processName, string taskName, string keyValue);
        Task<(bool HasSub, string SubTaskNames)> HasSubTasksAsync(string appName, string processName, string parentTaskName, string keyValue);
        Task<bool> HaveSubTasksCompletedAsync(string appName, string processName, string taskName, string keyValue);
        Task<bool> HasActiveSubTasksAsync(string appName, string processName, string parentTaskName, string keyValue, string subTaskNames);
        Task<string> GetParentTaskUserAsync(string appName, string taskId);

        // Additional Workflow Engine Validation Rules & Subroutines
        Task<bool> IsPriorTaskUserAsync(string appName, string processName, string priorTaskName, string keyValue, string priorIndex, string userName, string transId);
        Task<string> GetSameIndexTaskNamesAsync(string appName, string processName, string taskName, string indexNo);
        Task<bool> IsSameIndexTaskExistsAndActiveAsync(string appName, string processName, string taskName, string indexNo, string keyValue);

        // Dynamic Parameter Parsing & Registering
        Task<string> GetTaskParamsAsync(string appName, string processName, string taskName, string keyValue, string transId, int paramsFrom);
        Task<string> GetTaskParamsValuesAsync(string appName, string taskParams, string transId, Func<string, string> getFieldValue);
        Task RegisterAxActiveParamsAsync(string appName, string processName, string taskName, string keyValue, string transId, Action<string, string, string> registerToParser);
        Task AddRecToAxActiveTaskParamsAsync(string appName, DataRow activeTaskRow, string taskId, string status, string transId, Func<string, string> getFieldValue);
        Task<string> GetTransInitDateTimeAsync(string appName, string processName, string keyValue);
    }
}

