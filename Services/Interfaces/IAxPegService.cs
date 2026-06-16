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
    }
}
