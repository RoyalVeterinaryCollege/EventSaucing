namespace ExampleApp.Events;

public readonly record struct OrderPlacedForItem(string name, int quantity);