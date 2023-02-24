using EventSaucing.Aggregates;
using ExampleApp.Events;
using Scalesque;

namespace ExampleApp.Domain;

public class Order : Aggregate
{
    public Order(Guid id) {
        Id = id;
    }

    readonly Dictionary<string, int> items = new Dictionary<string, int>();
    public void AddItem(string itemName, int quantity) {
        RaiseEvent(new OrderPlacedForItem(itemName, quantity));
    }

    void Apply(OrderPlacedForItem @evt) => items[@evt.name] = items.GetOrElse(evt.name,()=>0) + @evt.quantity;
}