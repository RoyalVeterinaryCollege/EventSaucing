using System.Net;
using ExampleApp.Domain;
using NEventStore.Domain.Persistence;

namespace ExampleApp.Services
{
    public class UserActivitySimulatorService : BackgroundService
    {
        private readonly IRepository _repository;
        private readonly ILogger _logger;
        private readonly Random _rnd = new Random();
        public UserActivitySimulatorService(IRepository repository, ILogger logger) {
            _repository = repository;
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            _logger.LogDebug($"UserActivitySimulatorService is starting.");

            stoppingToken.Register(() =>
                _logger.LogDebug($" GracePeriod background task is stopping."));

            while (!stoppingToken.IsCancellationRequested)  {
                _logger.LogDebug($"GracePeriod task doing background work.");

                await SimulateSomeActivityAsync();

                await Task.Delay(5000, stoppingToken);
            }

            _logger.LogDebug($"UserActivitySimulatorService background task is stopping.");
        }

        private Task SimulateSomeActivityAsync() {
            try {
                var orders = new List<Guid>() {
                    Guid.Parse("c26b5d00-6dc5-43f5-849c-7cd21dbdf127"),
                    Guid.Parse("24003666-8d84-4e3f-856e-89987dde499d"),
                    Guid.Parse("41bb49a4-806b-4c44-a7ca-6d3300140b86")
                };
                var items = new List<string>() { "Book", "Film", "Pen" };

                var order = _repository.GetById<Order>(orders[_rnd.Next(orders.Count)]);
                if (order is null) order = new Order(orders[_rnd.Next(orders.Count)]);
                order.AddItem(items[_rnd.Next(items.Count)], 1);
                _repository.Save(order, Guid.NewGuid(), headers => headers["node"] = Dns.GetHostName());
            }
            catch (ConflictingCommandException) {
                // just hide this.  both nodes happened to save same order at same time
                // in real app, one user would lose their changes and would have to retry
            }


            return Task.CompletedTask;
        }
    }
}
