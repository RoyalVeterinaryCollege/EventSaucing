using NEventStore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace EventSaucing.StreamProcessors {

	public class ConventionBasedEventDispatcher {

		Dictionary<Type, ConventionalStreamProcessorMethodAsync> dispatchTable;

		public ConventionBasedEventDispatcher(object projector) {
			BuildDispatchTable(projector);
		}

		public static bool IsStreamProcessorMethod(MethodInfo method) {
			if (!method.Name.StartsWith("On")) return false;
			if (method.ReturnType != typeof(Task)) return false;
			var parameters = method.GetParameters();
			if (parameters.Length != 2) return false;
			if (parameters[0].ParameterType != typeof(ICommit)) return false;
			return true;
		}

		private ConventionalStreamProcessorMethodAsync MakeAsyncInvocation(object projector, MethodInfo method) {
			return new ConventionalStreamProcessorMethodAsync((ICommit commit, object evt) => { return (Task)method.Invoke(projector, new[] { commit, evt }); });
		}

		public void BuildDispatchTable(object projector) {
			dispatchTable = (
				from method in projector.GetType().GetMethods()
				where IsStreamProcessorMethod(method)
				select new {
					EventType = method.GetParameters().Last().ParameterType, // last parameter is the event
					InvocationMethod = MakeAsyncInvocation(projector, method)
				}).ToDictionary(x => x.EventType, x => x.InvocationMethod);
		}

		public IEnumerable<(ConventionalStreamProcessorMethodAsync, object)> GetStreamProcessorMethods(ICommit commit) {

			var commitEvents = (
				from events in commit.Events
				where events != null && dispatchTable.ContainsKey(events.Body.GetType())
				select events.Body).ToList();

			if (!commitEvents.Any())
				yield break;

			foreach (var eventMessage in commitEvents) {
				Type eventType = eventMessage.GetType();
				yield return (dispatchTable[eventType], eventMessage);
			}
		}
	}

	public delegate Task ConventionalStreamProcessorMethodAsync(ICommit commit, object evt);
}
