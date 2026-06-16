using System.Threading.Tasks;

namespace AxPeg.Lib.Interfaces
{
    public interface IRabbitMQPublisher
    {
        Task<bool> PushToQueueAsync(string appName, string queueName, string queueData);
    }
}
