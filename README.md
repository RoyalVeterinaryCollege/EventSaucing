EventSaucing 
===========
Add a little EventSauce to your project ;)

This library brings together different stacks to create an Event Sourcing solution.

----------
> **Reading list before using the project:**

> - [Dapper](http://dapper-tutorial.net/dapper)
> - [Event Sourcing design](https://martinfowler.com/eaaDev/EventSourcing.html)

####Installing
The latest release of EventSaucing is available on NuGet or can be downloaded from GitHub.

####Usage
Once referenced in your project you need to configure it in your Startup inside ``ConfigureServices``.
```
var builder = new ContainerBuilder();

builder.RegisterEventSaucingModules(new EventSaucingConfiguration {
	ConnectionString = // Set your connection string
});

builder.Populate(services); // Populate your services.

var container = builder.Build();

container.StartEventSaucing(); // Start EventSaucing
```
You can then construct your Aggregates by inheriting from the ``Aggregate`` class and including ``Apply()`` methods to handle the raised events on the aggregate.

```
public class FooAggregate : Aggregate {
	public FooAggregate(Guid id) {
		base.Id = id;
	}
	
	public int Bar { get; set; }

	public void Create(int bar) {
		RaiseEvent(new FooCreated(bar));
	}

	void Apply(FooCreated @event) {			
		Bar = @event.Bar;
	}
}
// A POCO for the event state
public class FooCreated {
	public FooCreated(int bar) {
		Bar = bar;
	}
	public int Bar { get; }
}
```

You can then create Projectors by inheriting from ``ProjectorBase`` and setting up which events to handle. You must also give each projector a unique number. In this example Dapper is used to interact with the persistence store. 

```
[Projector(1)] // The unique projector id.
public class FooProjector: ProjectorBase {
	readonly ConventionBasedCommitProjecter _conventionProjector;

	public FooProjector(IDbService dbService, IPersistStreams persistStreams):base(persistStreams, dbService) {
		var conventionalDispatcher = new ConventionBasedEventDispatcher(c => Checkpoint = c.ToSome())
		   .FirstProject<FooCreated>(OnFooCreated)
		   .ThenProject<SomeEvent>(OnSomeEventHandler);

		_conventionProjector = new ConventionBasedCommitProjecter(this, dbService, conventionalDispatcher);
	}

	public override void Project(ICommit commit) {
		_conventionProjector.Project(commit);
	}

	private void OnFooCreated(IDbTransaction tx, ICommit commit, FooCreated @event) {
		var sqlParams = new { 
			Id = commit.AggregateId(), 
			Bar = FooCreated.Bar 
		};

		const string sql = @"
			INSERT INTO [dbo].[FooProjector.Foo]
				   ([Id]
				   ,[Bar])
			 SELECT
				   @Id
				   ,@Bar
			WHERE NOT EXISTS(SELECT * FROM [dbo].[FooProjector.Foo] WHERE Id = @Id);";
		tx.Connection.Execute(sql, (object)sqlParams, tx);
	}
```

####Dependencies
[Dapper](https://github.com/StackExchange/Dapper) - Used to interact with the sql persistence store.
[NEventStore](https://github.com/NEventStore/NEventStore) - Used for storage of the event stream.
[Serilog](https://github.com/serilog/serilog) - Used for logging.
[Akka.NET](https://github.com/akkadotnet/akka.net/) - Used to deliver deliver events to aggregates & projectors. 
[Autofac](https://github.com/autofac/Autofac) - Used for dependency injection.
[Scalesque](https://github.com/NoelKennedy/scalesque) - Used to add functional programming in c#.