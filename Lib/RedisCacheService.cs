using System;
using System.Threading.Tasks;
using AxExtend.Interface;
using AxPeg.Lib.Interfaces;
using AxPeg.Exceptions;
using Serilog;

namespace AxPeg.Lib
{
    public class RedisCacheService : IRedisCacheService
    {
        private readonly IAxExtend _axExtend;

        public RedisCacheService(IAxExtend axExtend)
        {
            _axExtend = axExtend;
        }

        public async Task<bool> KeyExistsAsync(string appName, string key)
        {
            try
            {
                var connected = await _axExtend.OpenRedisConnectionAsync(appName);
                if (!connected)
                {
                    throw new RedisException("Failed to open Redis connection.");
                }

                var redis = await _axExtend.GetRedis();
                return await redis.KeyExistsAsync(key);
            }
            catch (Exception ex) when (!(ex is RedisException))
            {
                throw new RedisException($"Redis KeyExists error: {ex.Message}", ex);
            }
            finally
            {
                await _axExtend.CloseRedisConnectionAsync();
            }
        }

        public async Task<string> StringGetAsync(string appName, string key)
        {
            try
            {
                var connected = await _axExtend.OpenRedisConnectionAsync(appName);
                if (!connected)
                {
                    throw new RedisException("Failed to open Redis connection.");
                }

                var redis = await _axExtend.GetRedis();
                var value = await redis.StringGetAsync(key);
                return value?.ToString() ?? string.Empty;
            }
            catch (Exception ex) when (!(ex is RedisException))
            {
                throw new RedisException($"Redis StringGet error: {ex.Message}", ex);
            }
            finally
            {
                await _axExtend.CloseRedisConnectionAsync();
            }
        }

        public async Task<bool> StringSetAsync(string appName, string key, string value, int? expiryInSeconds = null)
        {
            try
            {
                var connected = await _axExtend.OpenRedisConnectionAsync(appName);
                if (!connected)
                {
                    throw new RedisException("Failed to open Redis connection.");
                }

                var redis = await _axExtend.GetRedis();
                return await redis.StringSetAsync(key, value, expiryInSeconds ?? 0);
            }
            catch (Exception ex) when (!(ex is RedisException))
            {
                throw new RedisException($"Redis StringSet error: {ex.Message}", ex);
            }
            finally
            {
                await _axExtend.CloseRedisConnectionAsync();
            }
        }
    }
}
