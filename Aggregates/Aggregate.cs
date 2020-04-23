using NEventStore.Domain.Core;
using System.Collections.Generic;


namespace EventSaucing.Aggregates
{
    /// <summary>
    /// Base class for aggregates
    /// </summary>
    public abstract class Aggregate : AggregateBase {
        /// <summary>
        /// Method called once after the aggregate is first created.  Not called on subsequent instantiations.
        /// </summary>
        public virtual void OnFirstCreated() { }

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