using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventSaucing.NEventStore;
using NUnit.Framework;

namespace EventSaucing.Projectors {
    [TestFixture]
    public abstract class TypeDependencyGraphTests {
        protected TypeDependencyGraph _sut;


        public TypeDependencyGraphTests() {
            Because();
        }

        protected abstract void Because();

        protected virtual Projector.Messages.DependUponProjectors Dummy<T>(params Type[] dependedOnProjectors) {
            return new Projector.Messages.DependUponProjectors(typeof(T), new ReadOnlyCollection<Type>(dependedOnProjectors));
        }

    }

    public class When_a_pair_of_projectors_with_a_dependency : TypeDependencyGraphTests   {
        protected override void Because() {
            _sut = new TypeDependencyGraph( Dummy<object>(typeof(int)) , Dummy<int>());
        }

        [Test]
        public void Should_blah() {
            _sut.Graph.
        }
    }
}