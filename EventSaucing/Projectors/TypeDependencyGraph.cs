using System;
using System.Collections.Generic;
using System.Text;

namespace EventSaucing.Projectors
{
    public class TypeDependencyGraph
    {
        private readonly IEnumerable<Projector.Messages.DependUponProjectors> _messages;

        public TypeDependencyGraph(params Projector.Messages.DependUponProjectors[] messages ) {
            _messages = messages;


        }
        public class Node {

            public Type Type { get; set; }
            public List<Node> Edges { get; } = new List<Node>();
        }
        
    }
}
