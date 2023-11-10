using NEventStore.Domain.Core;
using System.Collections.Generic;


namespace EventSaucing.Aggregates
{
    /// <summary>
    /// Base class for aggregates
    /// </summary>
    public abstract class Aggregate : AggregateBase {

		private Dictionary<string, object> _eventMessageHeaders;
		protected Dictionary<string, object> EventMessageHeaders {
			get {
				if (_eventMessageHeaders == null) {
					_eventMessageHeaders = new Dictionary<string, object>();
					_eventMessageHeaders.Add("AggregateType", this.GetType().FullName);
				}
				return _eventMessageHeaders;
			}
		}
    }
}