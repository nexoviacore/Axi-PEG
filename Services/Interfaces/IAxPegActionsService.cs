using System.Threading.Tasks;

namespace AxPeg.Services.Interfaces
{
    public interface IAxPegActionsService
    {
        Task<bool> ApproveTaskAsync(string appName, string taskId, string userName, string comments);
        Task<bool> RejectTaskAsync(string appName, string taskId, string userName, string comments);
        Task<bool> ForwardTaskAsync(string appName, string taskId, string userName, string forwardToUser, string comments);
        Task<bool> ReturnTaskAsync(string appName, string taskId, string userName, string comments);
        Task SkipAndUpdateAllOtherTasksWithSameIndexAsync(string appName, string processName, string taskName, string transId, string curIndex);
        Task<bool> DoPEGApprovalAsync(string appName, string transId, string taskId, string processName, string taskName, string keyField, string keyValue, string userName, string comments);
        Task<bool> DoAutoApprovalForOrphanTasksAsync(string appName, string transId, string taskId, string processName, string taskName, string keyField, string keyValue, string userName, string comments);
        Task<bool> IsOrphanApprovalTasksExistsAsync(string appName, string taskId);
        Task<string> GetSaveFormPayLoadAsync(string appName, string inputJson, string transId, string recData);
        Task<string> SaveFormAsync(string appName, string payload, string transId, string serviceCall);
        Task<string> CallValidateAndSaveAsync(string appName, bool splitResult);
        Task<(string priorIndex, string priorTask, string priorUserName, string initiator, string returnToUser)> GetPriorTaskAsync(string appName, string taskId, string taskName, string processName, string transId, string returnToIndex);
        Task<(string priorUserName, string initiator)> GetAxPriorTaskDataAsync(string appName, string taskId, string processName, string keyValue, string transId, string userName);
        Task<bool> AcceptAmendAsync(string appName, string transId, string keyValue, string processName);
        Task<bool> DiscardAmendAsync(string appName, string transId, string keyValue, string processName);
        Task<bool> IsAmendmentProcessAsync(string appName, string processName, string transId);
        Task<bool> IsAmendProcessAsync(string appName, string processName);
        Task AddRecToAxTaskDataAsync(string appName, string serviceCall, string taskId, string dataJsonString, string transId, string keyField, string keyValue);
    }
}
