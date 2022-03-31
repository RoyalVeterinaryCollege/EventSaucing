using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NEventStore;


namespace EventSaucing.StreamProcessors.Projectors
{

    /// <summary>
    /// Binds conventional projectors to commits.
    /// 
    /// Looks for methods which start with 'On' and are assignable to the ConventionalProjectionMethod delegate.
    /// </summary>
    public class ConventionBasedEventDispatcher {
        /// <summary>
        /// Typeof event -> to method which can project that event
        /// </summary>
        Dictionary<Type, ConventionalProjectionMethodAsync> _dispatchTable;

        public ConventionBasedEventDispatcher(object projector)  {
            BuildDispatchTable(projector);
        }

        /// <summary>
        /// Determines if a method meets the convention for the dispatcher.  Method must start with 'On', have 3 parameters (1st = IDbTransaction, 2nd ICommit) and return Task.
        /// </summary>
        /// <param name="method">MethodInfo</param>
        /// <returns>bool true if the methos</returns>
        public static bool IsProjectionMethod(MethodInfo method)  {
            if (!method.Name.StartsWith("On")) return false;
            if (method.ReturnType != typeof(Task)) return false;
            var parameters = method.GetParameters();
            if (parameters.Length != 3) return false;
            if (parameters[0].ParameterType != typeof(IDbTransaction)) return false;
            if (parameters[1].ParameterType != typeof(ICommit)) return false;
            return true;
        }

        private ConventionalProjectionMethodAsync MakeAsyncInvocation(object projector, MethodInfo method) {
            return new ConventionalProjectionMethodAsync(
                (IDbTransaction tx, ICommit commit, object @event) => {
                   return (Task)method.Invoke(projector, new[] { tx, commit, @event });
                });
        }

        public void BuildDispatchTable(object projector)  {
            _dispatchTable = (from method in projector.GetType().GetMethods()
                             where IsProjectionMethod(method)
                             select new {
                                 EventType = method.GetParameters().Last().ParameterType //last parameter is the event
                                 , InvocationMethod = MakeAsyncInvocation(projector, method)
                              })
                         .ToDictionary(x => x.EventType, x => x.InvocationMethod);
        }

        public IEnumerable<(ConventionalProjectionMethodAsync, object)> GetProjectionMethods(ICommit commit) {
            //see if we project any of these events
            var projectedEvents = (from events in commit.Events
                where events != null && _dispatchTable.ContainsKey(events.Body.GetType())
                select events.Body).ToList();

            // guard none
            if (!projectedEvents.Any())
                yield break;

            // get the projection method from the dispatch table
            foreach (var eventMessage in projectedEvents) {
                Type eventType = eventMessage.GetType();
                yield return (_dispatchTable[eventType], eventMessage);
            }
        }
    }
}
