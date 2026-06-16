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
    }
}
