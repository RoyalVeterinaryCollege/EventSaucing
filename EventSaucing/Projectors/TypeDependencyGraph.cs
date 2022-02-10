using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;

namespace EventSaucing.Projectors {
    public class TypeDependencyGraph {
        private readonly IEnumerable<Projector.Messages.DependUponProjectors> _messages;
        public class Node
        {
            public Type Type { get; set; }
            public IActorRef Projector { get; set; }
            public List<Node> Edges { get; } = new List<Node>();
        }
        public TypeDependencyGraph(params Projector.Messages.DependUponProjectors[] messages) {
            _messages = messages;
        }

        public void Traverse(Action<Node> a) {

        }

    }
}