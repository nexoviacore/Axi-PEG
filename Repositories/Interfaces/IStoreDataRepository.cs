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
    }
}
