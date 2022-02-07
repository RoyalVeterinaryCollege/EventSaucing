using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using FluentAssertions;

namespace EventSaucing.EventStream
{
    [TestFixture]
    public abstract class InMemoryCommitSerialiserCacheTests {
        protected InMemoryCommitSerialiserCache sut = new InMemoryCommitSerialiserCache(5);

        protected virtual void Because(){}

        public InMemoryCommitSerialiserCacheTests() {
            Because();
        }
    }
    public class When_cache_is_empty : InMemoryCommitSerialiserCacheTests {
        [Test] public void It_returns_no_commits() {
            sut.GetCommitsAfter(0L).Should().BeEmpty();
        }
    }

    public class When_cache_contains_one_commit : InMemoryCommitSerialiserCacheTests
    {
        private FakeCommit _fakeCommit1;

        protected override void Because() {
            base.Because();
            _fakeCommit1 = new FakeCommit(){CheckpointToken = 10L};
            sut.Cache(_fakeCommit1);
        }

        [Test]  public void It_returns_no_commits_after_commit_checkpoint() {
            sut.GetCommitsAfter(11L).Should().BeEmpty();
        }

        [Test] public void It_returns_only_commits_after_the_checkpoint() {
            sut.GetCommitsAfter(9L)
                .Should().ContainSingle(x => x.Commit == _fakeCommit1);
        }
    }

    public class When_cache_contains_two_contiguous_commits : InMemoryCommitSerialiserCacheTests
    {
        private FakeCommit _fakeCommit1;
        private FakeCommit _fakeCommit2;

        protected override void Because()
        {
            base.Because();
            _fakeCommit1 = new FakeCommit() { CheckpointToken = 10L };
            _fakeCommit2 = new FakeCommit() { CheckpointToken = 11L };
            //note out of order
            sut.Cache(_fakeCommit2);
            sut.Cache(_fakeCommit1);
        }
        
        [Test]
        public void It_returns_expected_count_of_commits() {
            sut.GetCommitsAfter(9L)
                .Should().HaveCount(2);
        }

        [Test]
        public void It_returns_commits_in_correct_order()  {
            sut.GetCommitsAfter(9L)
                .Select(x => x.Commit.CheckpointToken)
                .Should().BeEquivalentTo(new[] { 10L, 11L });
        }
    }

    public class When_cache_contains_two_non_contiguous_commits : InMemoryCommitSerialiserCacheTests
    {
        private FakeCommit _fakeCommit1;
        private FakeCommit _fakeCommit2;

        protected override void Because()
        {
            base.Because();
            _fakeCommit1 = new FakeCommit() { CheckpointToken = 10L };
            _fakeCommit2 = new FakeCommit() { CheckpointToken = 12L };//note gap
            sut.Cache(_fakeCommit1);
            sut.Cache(_fakeCommit2);
        }

        [Test]
        public void It_returns_only_the_commit_it_can_place_in_order()
        {
            sut.GetCommitsAfter(9L)
                .Should().HaveCount(1);
        }

        [Test]
        public void It_returns_only_the_correct_commit() {
            sut.GetCommitsAfter(9L)
                .Should().ContainSingle(x => x.Commit == _fakeCommit1);
        }

        [Test]
        public void It_returns_the_later_commit_if_requested()
        {
            sut.GetCommitsAfter(11L)
                .Should().ContainSingle(x => x.Commit == _fakeCommit2);
        }
    }
}
