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

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug($"GracePeriod task doing background work.");

                await SimulateSomeActivityAsync();

                await Task.Delay(5000, stoppingToken);
            }

            _logger.LogDebug($"UserActivitySimulatorService background task is stopping.");
        }

        private Task SimulateSomeActivityAsync() {
            var order = new Order(Guid.NewGuid());
            var items = new List<string>() { "Book", "Film", "Pen" };
            order.AddItem(items[_rnd.Next(items.Count)],1);
            _repository.Save(order, Guid.NewGuid());
            return Task.CompletedTask;
        }
    }
}
