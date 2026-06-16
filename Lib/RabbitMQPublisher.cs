using System;
using System.Threading.Tasks;
using AxExtend.Interface;
using AxPeg.Lib.Interfaces;
using AxPeg.Exceptions;

namespace AxPeg.Lib
{
    public class RabbitMQPublisher : IRabbitMQPublisher
    {
        private readonly IAxExtend _axExtend;

        public RabbitMQPublisher(IAxExtend axExtend)
        {
            _axExtend = axExtend;
        }

        public async Task<bool> PushToQueueAsync(string appName, string queueName, string queueData)
        {
            try
            {
                // Note: GetRabbitMQProducer does not require appName in AxPlugins, but we can call it directly
                var rmq = await _axExtend.GetRabbitMQProducer();
                if (rmq == null)
                {
                    throw new PegException("Unable to initialize RabbitMQ producer from AxExtend.");
                }

                // SendMessages returns boolean status
                return rmq.SendMessages(queueData, queueName, false, 0, 0);
            }
            catch (Exception ex)
            {
                throw new PegException($"RabbitMQ Publish error: {ex.Message}", ex);
            }
        }
    }
}
