using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommonDomain;
using CommonDomain.Core;
using Scalesque;

namespace EventSaucing.NEventStore {
    /// <summary>
    /// This is similar to NEventStore's ConventionEventRouter but it is more efficient as it can be shared between instances.
    ///
    /// It is threadsafe for event dispatch
    /// </summary>
    /// <remarks>The NEventStore's ConventionEventRouter has to be built once per instance, this is build once per type</remarks>
    public class SharedConventionEventRouter {
        public delegate void Apply(IAggregate aggregate, object @event);

        readonly IDictionary<Type, Apply> _handlers = new Dictionary<Type, Apply>();

        readonly bool _throwOnApplyNotFound;

        public SharedConventionEventRouter(bool throwOnApplyNotFound, Type aggregateType) {
            this._throwOnApplyNotFound = throwOnApplyNotFound;
            Register(aggregateType);
        }

        private void Register(Type aggregate) {
            if (aggregate == null) {
                throw new ArgumentNullException("aggregate");
            }

            // Get instance methods named Apply with one parameter returning void
            var applyMethods =
                aggregate.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         .Where(
                             m => m.Name == "Apply" && m.GetParameters().Length == 1 && m.ReturnParameter.ParameterType == typeof(void))
                         .Select(m => new { Method = m, MessageType = m.GetParameters().Single().ParameterType });

            foreach (var apply in applyMethods) {
                MethodInfo applyMethod = apply.Method;
                this._handlers.Add(apply.MessageType, (agg, @event) => applyMethod.Invoke(agg, new[] { @event }));
            }
        }

        public virtual void Dispatch(IAggregate aggregate, object @event) {
            if (@event == null) {
                throw new ArgumentNullException("eventMessage");
            }

            Apply handler;
            if (_handlers.TryGetValue(@event.GetType(), out handler)) {
                handler(aggregate, @event);
            } else if (_throwOnApplyNotFound) {
                string exceptionMessage =
                "Aggregate of type '{0}' raised an event of type '{1}' but not handler could be found to handle the message."
                    .format(aggregate.GetType().Name, @event.GetType().Name);

                throw new HandlerForDomainEventNotFoundException(exceptionMessage);
            }
        }
    }
}