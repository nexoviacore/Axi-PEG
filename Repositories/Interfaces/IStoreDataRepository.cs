using System.Data;
using System.Threading.Tasks;

namespace AxPeg.Repositories.Interfaces
{
    public interface IStoreDataRepository
    {
        Task<bool> OpenConnectionAsync(string appName);
        Task CloseConnectionAsync();
        Task<DataTable> ExecuteQueryAsync(string sql);
        Task<int> ExecuteNonQueryAsync(string sql);
        Task<string> GetFieldDataFromDBAsync(string tableName, string fieldName, string whereCond, string orderBy, bool fetchAll = false);
        Task<int> GetDataRowCountAsync(string tableName, string fieldName, string whereCond, string orderBy);
        Task<string> GetRecIDAsync(string tableName, string whereCond);
        Task<double> GetTableIdAsync(string tableName);
        string ConvertSDtoRapidSaveInputJSON(System.Collections.Generic.List<Dtos.FieldRecord> fieldList, string appName, string userName, string transId);
        
        // Database Transaction Procedures
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();

        // Database Sequence Generators
        Task<string> GetPrefixFieldValueAsync(string tableName, string seqTable, string fieldName, string sval, string transId, bool onlyGet, int digits, string userName, double newRecId);
        Task<string> GetLastNoAsync(string tableName, string seqTable, string fieldName, string transId, bool onlyGet, string userName, double newRecId);
        Task<string> GetUserActivePrefixAsync(string tableName, string transId, string fieldName, string userName);
        Task<string> GetActivePrefixAsync(string tableName, string seqTable, string transId, string fieldName, string userName);
        Task SetPrefixAsync(string tableName, string seqTable, string transId, string fieldName, string prefix, string userName);

        // History Recording & Change Tracking
        Task<int> GetNoofTimesModifiedAsync(string tableName, string transId, double recordId);
        Task SaveHistoryToDBAsync(string tableName, string transId, double recordId, string userName, string fieldName, int modNo, int frameNo, int parentRow, double tableRecId, double idValue, double oldIdValue, string newValue, string oldValue, string delflag, bool transDeleted, bool isNewTrans, bool isCancelTrans, string cancelRemarks);
        Task SaveHistoryToTableAsync(string tableName, string transId, double recordId, string changedValueText, int modNo, bool transDeleted);
        Task SaveCancelRemarksToHistoryAsync(string tableName, string transId, double recordId, string userName, string cancelRemarks, string? changedValueText = null);
        
        // Outbound & Site Synchronization
        Task InsertIntoOutboundTableAsync(string tableName, string transId, double recordId, string userName);
        Task<bool> CheckInsertToOutboundAsync(string tableName, string transId);
    }
}

