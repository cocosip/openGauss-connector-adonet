using NUnit.Framework;

namespace OpenGauss.Tests
{
    [TestFixture(SyncOrAsync.Sync)]
    [TestFixture(SyncOrAsync.Async)]
    public abstract class SyncOrAsyncTestBase : TestBase
    {
        protected bool IsAsync => SyncOrAsync == SyncOrAsync.Async;

        protected SyncOrAsync SyncOrAsync { get; }

        protected SyncOrAsyncTestBase(SyncOrAsync syncOrAsync) => SyncOrAsync = syncOrAsync;
    }

    public enum SyncOrAsync
    {
        Sync,
        Async
    }
}
