using NEventStore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {


    /// <summary>
    /// A delegate describing the signature of a method which projects an event or article
    /// </summary>
    public delegate Task ConventionalReactionMethod(object reactor, object payload);

    /// <summary>
    /// An event dispatcher for reactors which react to aggregate events
    /// 
    /// Looks for methods which start with 'Apply' and have one parameter.  These methods can be either async or sync
    /// </summary>
    public class ConventionalReactionDispatcher
    {
        /// <summary>
        /// Typeof event -> to method which can react to that event/article
        /// </summary>
        Dictionary<Type, ConventionalReactionMethod> _dispatchTable;
        private readonly Type reactorType;

        public ConventionalReactionDispatcher(Type reactorType)  {
            this.reactorType = reactorType;

            BuildDispatchTable();
        }

        /// <summary>
        /// Determines if the method is a reaction method.  Could parameterise this.
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private bool IsReactionMethod(MethodInfo method)  {
            if (!method.Name.StartsWith("Apply")) return false;
            var parameters = method.GetParameters();
            if (parameters.Length != 1) return false;
            return true;
        }

        private ConventionalReactionMethod MakeAsyncInvocation(MethodInfo method) {
            if(method.ReturnType == typeof(Task)) {
                //async
                return new ConventionalReactionMethod(
                    (reactor, payload) => {return (Task)method.Invoke(reactor, new[] { payload });
                });
            } else {
                //wrap sync method
                return new ConventionalReactionMethod(
                    (reactor, payload) => {
                       var result=method.Invoke(reactor, new[] { payload });
                       return Task.CompletedTask;
                    }
                );
            }
        }

        public void BuildDispatchTable()  {
            _dispatchTable = (from method in reactorType.GetMethods()
                              where IsReactionMethod(method)
                              select new
                              {
                                  EventType = method.GetParameters()[0].ParameterType // only one parameter and thats the payload : either an event or an article
                                  ,
                                  InvocationMethod = MakeAsyncInvocation(method)
                              })
                         .ToDictionary(x => x.EventType, x => x.InvocationMethod);
        }
        public async Task DispatchEventStreamAsync(IReactor reactor, IEventStream stream) {
            foreach (EventMessage eventMessage in stream.CommittedEvents) {
                await DispatchPayloadAsync(reactor, eventMessage.Body);
            }
        }
        /// <summary>
        /// Dispatches the payload to the reactor
        /// </summary>
        /// <param name="reactor">IReactor</param>
        /// <param name="payload">object Either the event or the article</param>
        /// <returns></returns>
        public async Task DispatchPayloadAsync(IReactor reactor, object payload)  {
            Type eventType = payload.GetType();
            if (!_dispatchTable.ContainsKey(eventType)) return ;
            var projectionMethod = _dispatchTable[eventType];
            await projectionMethod(reactor, payload);
        }
    }
}
