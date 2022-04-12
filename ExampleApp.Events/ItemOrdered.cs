namespace ExampleApp.Events;

public readonly record struct ItemOrdered(string name, int quantity);