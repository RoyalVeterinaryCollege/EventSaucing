using System;
using System.Collections.Generic;
using System.Text;
using NEventStore.Persistence;

namespace EventSaucing.StreamProcessors.Reactors
{
    public abstract class Reactor : StreamProcessor
    {
        public Reactor(IPersistStreams persistStreams) : base(persistStreams) { }
    }
}
