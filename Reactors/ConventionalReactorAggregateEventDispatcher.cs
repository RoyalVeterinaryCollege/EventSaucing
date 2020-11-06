using Dapper;
using EventSaucing.NEventStore;
using NEventStore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;



namespace EventSaucing.Reactors
{

    public delegate void ProjectionMethod(IDbTransaction tx, ICommit commit, object @event);

    /// <summary>
    /// A delegate describing the signature of a method which projects an event
    /// </summary>
    public delegate Task ConventionalReactionMethod(object reactor, object @event);

    /// <summary>
    /// An event dispatcher for reactors which react to aggregate events
    /// 
    /// Looks for methods which start with 'Apply' and have one parameter.  These methods can be either async or sync
    /// </summary>
    public class ConventionalReactorAggregateEventDispatcher
    {
        /// <summary>
        /// Typeof event -> to method which can project that event
        /// </summary>
        Dictionary<Type, ConventionalReactionMethod> _dispatchTable;
        private readonly Type reactorType;

        public ConventionalReactorAggregateEventDispatcher(Type reactorType)  {
            this.reactorType = reactorType;

            BuildDispatchTable();
        }

        /// <summary>
        /// Determines if the method is a reaction method.  Could parameterise this.
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private bool IsAggregateReactionMethod(MethodInfo method)  {
            if (!method.Name.StartsWith("Apply")) return false;
            var parameters = method.GetParameters();
            if (parameters.Length != 1) return false;
            return true;
        }

        private ConventionalReactionMethod MakeAsyncInvocation(MethodInfo method) {
            if(method.ReturnType == typeof(Task)) {
                //async
                return new ConventionalReactionMethod(
                    (reactor, @event) => {return (Task)method.Invoke(reactor, new[] { @event });
                });
            } else {
                //wrap sync method
                return new ConventionalReactionMethod(
                    (reactor, @event) => {
                       var result= method.Invoke(reactor, new[] { @event });
                       return Task.CompletedTask;
                    }
                );
            }
        }

        public void BuildDispatchTable()  {
            _dispatchTable = (from method in reactorType.GetMethods()
                              where IsAggregateReactionMethod(method)
                              select new
                              {
                                  EventType = method.GetParameters()[0].ParameterType // only one parameter and thats the event
                                  ,
                                  InvocationMethod = MakeAsyncInvocation(method)
                              })
                         .ToDictionary(x => x.EventType, x => x.InvocationMethod);
        }
        public async Task ReactAsync(object reactor, IEventStream stream) {
            foreach (EventMessage eventMessage in stream.CommittedEvents) {
                await ReactAsync(reactor, eventMessage.Body);
            }
        }
        public async Task ReactAsync(object reactor, object @event)  {
            Type eventType = @event.GetType();
            if (!_dispatchTable.ContainsKey(eventType)) return ;
            var projectionMethod = _dispatchTable[eventType];
            await projectionMethod(reactor, @event);
        }
        public async Task ReactAsync(object reactor, ICommit commit)  {
            foreach (var eventMessage in commit.Events)  {
                try {
                    await ReactAsync(reactor, eventMessage);
                }  catch (Exception)  {
                    //todo error handling here or nah?
                    //logger.Error(error.InnerException, $"{nameof(ConventionalReactorAggregateEventDispatcher)} caught exception when trying to project event {eventType} in commit {commit.CommitId} at checkpoint {commit.CheckpointToken} for aggregate {commit.AggregateId()}");
                    throw;
                }
            }
        }
    }
}
