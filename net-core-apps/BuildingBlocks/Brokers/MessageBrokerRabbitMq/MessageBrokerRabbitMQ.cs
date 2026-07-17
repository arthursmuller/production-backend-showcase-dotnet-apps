using MessageBrokerCore.Abstractions;
using MessageBrokerRabbitMq.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace MessageBrokerRabbitMq
{
    public abstract class MessageBrokerRabbitMQ : IMessageBroker, IDisposable
    {
        public readonly IRabbitMQPersistentConnection _persistentConnection;
        private IModel? _model;
        private readonly string _rmqExchangeName;
        protected string _routingKey;
        protected string _queueName;
        private readonly ILogger<MessageBrokerRabbitMQ> _logger;

        public MessageBrokerRabbitMQ(
            IServiceProvider provider,
            string rmqExchangeName,
            string routingKey,
            string queueName,
            int retryCount)
        {
            _persistentConnection = provider.GetRequiredService<IRabbitMQPersistentConnection>();
            _logger = provider.GetRequiredService<ILogger<MessageBrokerRabbitMQ>>();
            _rmqExchangeName = rmqExchangeName;
            _routingKey = routingKey;
            _queueName = queueName;
        }

        public void CreateConnection(bool? isDelayed = false)
        {
            if (!_persistentConnection.IsConnected)
                _persistentConnection.TryAndConnect();

            var channel = _persistentConnection.CreateModel();

            try
            {
                channel.QueueDeclare(queue: _queueName,
                                   durable: true,
                                   exclusive: false,
                                   autoDelete: false,
                                   arguments: null);

                channel.CallbackException += (sender, ea) =>
                {
                    RecoverConsumerChannel(channel, isDelayed == true);
                };

                DisposeConsumerChannel();
                _model = channel;
            }
            catch
            {
                channel.Dispose();
                throw;
            }
        }

        private void RecoverConsumerChannel(IModel failedChannel, bool isDelayed)
        {
            // Ignore callbacks from a channel that has already been replaced. Each consumer owns
            // its own channel; a failure in one consumer must not dispose another consumer's model.
            if (!ReferenceEquals(_model, failedChannel))
                return;

            DisposeConsumerChannel();
            CreateConnection(isDelayed);

            if (isDelayed)
                RegisterDelayQueue();
            else
                RegisterQueue();

            StartBasicConsume();
        }

        public void RegisterQueue()
        {
            var channel = GetConsumerChannel();

            // durable=true to match topology declared via definitions.json (and net-core stack)
            channel.ExchangeDeclare(_rmqExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);

            channel.QueueDeclare(_queueName, true, false, false, null);

            channel.QueueBind(queue: _queueName,
                                  exchange: _rmqExchangeName,
                                  routingKey: _routingKey);
        }

        public void RegisterDelayQueue()
        {
            var channel = GetConsumerChannel();
            IDictionary<String, Object> args = new Dictionary<String, Object>();
            args.Add("x-delayed-type", ExchangeType.Direct);

            channel.ExchangeDeclare(_rmqExchangeName, "x-delayed-message", true, false, args);

            channel.QueueDeclare(_queueName, true, false, false, null);

            channel.QueueBind(queue: _queueName,
                                  exchange: _rmqExchangeName,
                                  routingKey: _routingKey);
        }

        public void Subscribe() => StartBasicConsume();

        public void StartBasicConsume()
        {
            string? response = null;

            _logger.LogInformation(
                 "[*] Waiting for message. routingKey: {RoutingKey} queue: {QueueName}",
                 _routingKey,
                 _queueName);
            try
            {
                var channel = GetConsumerChannel();
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += (_, eventArgs) => Consumer_Received_Async(channel, eventArgs);

                channel.BasicConsume(queue: _queueName,
                                     autoAck: false,
                                     consumer: consumer);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(
                        "Message Process fail,error:{ex.Message},stackTrace:{ex.StackTrace}",
                        ex.Message,
                        ex.StackTrace);

                _logger.LogError(-1, ex, "Message Process fail");

                response = "";
            }
            finally
            {
                if (response != null)
                {
                    var responseBytes = Encoding.UTF8.GetBytes(response);
                    /*_channel.BasicPublish("", props.ReplyTo, replyProps, responseBytes);*/
                }

            }
        }

        private async Task Consumer_Received_Async(IModel channel, BasicDeliverEventArgs eventArgs)
        {
            var routingKey = eventArgs.RoutingKey;
            var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

            try
            {
                await ProcessMessageAsync(message);
                channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(
                        "{routingKey} Message Process fail,error:{ex.Message},stackTrace:{ex.StackTrace}",
                        routingKey,
                        ex.Message,
                        ex.StackTrace);

                _logger.LogError(-1, ex, "{routingKey}  Message Process fail",
                        routingKey);
            }
        }

        private IModel GetConsumerChannel()
        {
            if (_model is null || !_model.IsOpen)
                throw new InvalidOperationException(
                    "The RabbitMQ consumer channel is not open. Call CreateConnection before registering or subscribing the queue.");

            return _model;
        }

        private void DisposeConsumerChannel()
        {
            var channel = _model;
            _model = null;

            if (channel is null)
                return;

            try
            {
                channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose RabbitMQ consumer channel for queue {QueueName}", _queueName);
            }
        }

        public void Dispose() => DisposeConsumerChannel();

        public abstract Task ProcessMessageAsync(string message);
    }
}
