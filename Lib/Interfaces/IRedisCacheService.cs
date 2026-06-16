using System.Threading.Tasks;

namespace AxPeg.Lib.Interfaces
{
    public interface IRedisCacheService
    {
        Task<bool> KeyExistsAsync(string appName, string key);
        Task<string> StringGetAsync(string appName, string key);
        Task<bool> StringSetAsync(string appName, string key, string value, int? expiryInSeconds = null);
    }
}
