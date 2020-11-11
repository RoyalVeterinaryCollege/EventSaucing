﻿using System.Collections.Generic;
using System.Linq;

namespace EventSaucing.Reactors {
    /// <summary>
    /// Previously persisted publication and subscription data for the reactor
    /// </summary>
    public class PreviouslyPersistedPubSubData {
        public PreviouslyPersistedPubSubData(IEnumerable<ReactorAggregateSubscription> enumerable1, IEnumerable<ReactorSubscription> enumerable2, IEnumerable<ReactorPublication> enumerable3) {
            AggregateSubscriptions = enumerable1.ToList();
            ReactorSubscriptions = enumerable2.ToList();
            Publications = enumerable3.ToList();
        }

        public List<ReactorAggregateSubscription> AggregateSubscriptions { get; private set; }
        public List<ReactorSubscription> ReactorSubscriptions { get; private set; }
        public List<ReactorPublication> Publications { get; private set; }
    }
}