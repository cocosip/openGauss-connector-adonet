using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Transactions;
using NUnit.Framework;

// TransactionScope exists in netstandard20, but distributed transactions do not.
// We used to support distributed transactions back when we targeted .NET Framework, keeping them here in case
// they get ported to .NET Core (https://github.com/dotnet/runtime/issues/715)
#if DISTRIBUTED_TRANSACTIONS

namespace OpenGauss.Tests
{
    [NonParallelizable]
    public class DistributedTransactionTests : TestBase
    {
        [Test]
        public void Two_connections()
        {
            using (var conn1 = OpenConnection(ConnectionStringEnlistOff))
            using (var conn2 = OpenConnection(ConnectionStringEnlistOff))
            {
                using (var scope = new TransactionScope())
                {
                    conn1.EnlistTransaction(Transaction.Current);
                    conn2.EnlistTransaction(Transaction.Current);

                    Assert.That(conn1.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test1')"), Is.EqualTo(1), "Unexpected first insert rowcount");
                    Assert.That(conn2.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test2')"), Is.EqualTo(1), "Unexpected second insert rowcount");

                    scope.Complete();
                }
            }
            // TODO: There may be a race condition here, where the prepared transaction above still hasn't committed.
            AssertNoDistributedIdentifier();
            AssertNoPreparedTransactions();
            AssertNumberOfRows(2);
        }

        [Test]
        public void Two_connections_rollback()
        {
            using (new TransactionScope())
            using (var conn1 = OpenConnection(ConnectionStringEnlistOn))
            using (var conn2 = OpenConnection(ConnectionStringEnlistOn))
            {
                Assert.That(conn1.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test1')"), Is.EqualTo(1), "Unexpected first insert rowcount");
                Assert.That(conn2.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test2')"), Is.EqualTo(1), "Unexpected second insert rowcount");
            }
            // TODO: There may be a race condition here, where the prepared transaction above still hasn't committed.
            AssertNoDistributedIdentifier();
            AssertNoPreparedTransactions();
            AssertNumberOfRows(0);
        }

        [Test, Ignore("Flaky")]
        public void Distributed_rollback()
        {
            var disposedCalled = false;
            var tx = new TransactionScope();
            try
            {
                using (var conn1 = OpenConnection(ConnectionStringEnlistOn))
                {
                    Assert.That(conn1.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test1')"), Is.EqualTo(1), "Unexpected first insert rowcount");

                    EnlistResource.EscalateToDistributed(true);
                    AssertHasDistributedIdentifier();
                    tx.Complete();
                }
                disposedCalled = true;
                Assert.That(() => tx.Dispose(), Throws.TypeOf<TransactionAbortedException>());
                // TODO: There may be a race condition here, where the prepared transaction above still hasn't completed.
                AssertNoDistributedIdentifier();
                AssertNoPreparedTransactions();
                AssertNumberOfRows(0);
            }
            finally
            {
                if (!disposedCalled)
                    tx.Dispose();
            }
        }

        [Test(Description = "Transaction race, bool distributed")]
        [Explicit("Fails on Appveyor (https://ci.appveyor.com/project/roji/opengauss/build/3.3.0-250)")]
        public void Transaction_race([Values(false, true)] bool distributed)
        {
            for (var i = 1; i <= 100; i++)
            {
                var eventQueue = new ConcurrentQueue<TransactionEvent>();
                try
                {
                    using (var tx = new TransactionScope())
                    using (var conn1 = OpenConnection(ConnectionStringEnlistOn))
                    {
                        eventQueue.Enqueue(new TransactionEvent("Scope started, connection enlisted"));
                        Assert.That(conn1.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test1')"), Is.EqualTo(1), "Unexpected first insert rowcount");
                        eventQueue.Enqueue(new TransactionEvent("Insert done"));

                        if (distributed)
                        {
                            EnlistResource.EscalateToDistributed(eventQueue);
                            AssertHasDistributedIdentifier();
                        }
                        else
                        {
                            EnlistResource.EnlistVolatile(eventQueue);
                            AssertNoDistributedIdentifier();
                        }

                        tx.Complete();
                        eventQueue.Enqueue(new TransactionEvent("Scope completed"));
                    }
                    eventQueue.Enqueue(new TransactionEvent("Scope disposed"));
                    AssertNoDistributedIdentifier();
                    if (distributed)
                    {
                        // There may be a race condition here, where the prepared transaction above still hasn't completed.
                        // This is by design of MS DTC. Giving it up to 100ms to complete. If it proves flaky, raise
                        // maxLoop.
                        const int maxLoop = 20;
                        for (var j = 0; j < maxLoop; j++)
                        {
                            Thread.Sleep(10);
                            try
                            {
                                AssertNumberOfRows(i);
                                break;
                            }
                            catch
                            {
                                if (j == maxLoop - 1)
                                    throw;
                            }
                        }
                    }
                    else
                        AssertNumberOfRows(i);
                }
                catch (Exception ex)
                {
                    Assert.Fail(
                        @"Failed at iteration {0}.
Events:
{1}
Exception {2}",
                        i, FormatEventQueue(eventQueue), ex);
                }
            }
        }

        [Test]
        public void Two_connections_with_failure()
        {
            using (var conn1 = OpenConnection(ConnectionStringEnlistOff))
            using (var conn2 = OpenConnection(ConnectionStringEnlistOff))
            {
                var scope = new TransactionScope();
                conn1.EnlistTransaction(Transaction.Current);
                conn2.EnlistTransaction(Transaction.Current);

                Assert.That(conn1.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test1')"), Is.EqualTo(1), "Unexpected first insert rowcount");
                Assert.That(conn2.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test2')"), Is.EqualTo(1), "Unexpected second insert rowcount");

                conn1.ExecuteNonQuery($"SELECT pg_terminate_backend({conn2.ProcessID})");
                scope.Complete();
                Assert.That(() => scope.Dispose(), Throws.Exception.TypeOf<TransactionAbortedException>());

                AssertNoDistributedIdentifier();
                AssertNoPreparedTransactions();
                using (var tx = conn1.BeginTransaction())
                {
                    Assert.That(conn1.ExecuteScalar(@"SELECT COUNT(*) FROM data"), Is.EqualTo(0), "Unexpected data count");
                    tx.Rollback();
                }
            }
        }

        [Test(Description = "Connection reuse race after transaction, bool distributed"), Explicit]
        public void Connection_reuse_race_after_transaction([Values(false, true)] bool distributed)
        {
            for (var i = 1; i <= 100; i++)
            {
                var eventQueue = new ConcurrentQueue<TransactionEvent>();
                try
                {
                    using (var conn1 = OpenConnection(ConnectionStringEnlistOff))
                    {
                        using (var scope = new TransactionScope())
                        {
                            conn1.EnlistTransaction(Transaction.Current);
                            eventQueue.Enqueue(new TransactionEvent("Scope started, connection enlisted"));

                            if (distributed)
                            {
                                EnlistResource.EscalateToDistributed(eventQueue);
                                AssertHasDistributedIdentifier();
                            }
                            else
                            {
                                EnlistResource.EnlistVolatile(eventQueue);
                                AssertNoDistributedIdentifier();
                            }

                            Assert.That(conn1.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test1')"), Is.EqualTo(1), "Unexpected first insert rowcount");
                            eventQueue.Enqueue(new TransactionEvent("Insert done"));

                            scope.Complete();
                            eventQueue.Enqueue(new TransactionEvent("Scope completed"));
                        }
                        eventQueue.Enqueue(new TransactionEvent("Scope disposed"));

                        Assert.DoesNotThrow(() => conn1.ExecuteScalar(@"SELECT COUNT(*) FROM data"));
                    }
                }
                catch (Exception ex)
                {
                    Assert.Fail(
                        @"Failed at iteration {0}.
Events:
{1}
Exception {2}",
                        i, FormatEventQueue(eventQueue), ex);
                }
            }
        }

        [Test(Description = "Connection reuse race after rollback, bool distributed"), Explicit("Currently failing.")]
        public void Connection_reuse_race_after_rollback([Values(false, true)] bool distributed)
        {
            for (var i = 1; i <= 100; i++)
            {
                var eventQueue = new ConcurrentQueue<TransactionEvent>();
                try
                {
                    using (var conn1 = OpenConnection(ConnectionStringEnlistOff))
                    {
                        using (new TransactionScope())
                        {
                            conn1.EnlistTransaction(Transaction.Current);
                            eventQueue.Enqueue(new TransactionEvent("Scope started, connection enlisted"));

                            if (distributed)
                            {
                                EnlistResource.EscalateToDistributed(eventQueue);
                                AssertHasDistributedIdentifier();
                            }
                            else
                            {
                                EnlistResource.EnlistVolatile(eventQueue);
                                AssertNoDistributedIdentifier();
                            }

                            Assert.That(conn1.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test1')"), Is.EqualTo(1), "Unexpected first insert rowcount");
                            eventQueue.Enqueue(new TransactionEvent("Insert done"));

                            eventQueue.Enqueue(new TransactionEvent("Scope not completed"));
                        }
                        eventQueue.Enqueue(new TransactionEvent("Scope disposed"));
                        conn1.EnlistTransaction(null);
                        eventQueue.Enqueue(new TransactionEvent("Connection enlisted with null"));
                        Assert.DoesNotThrow(() => conn1.ExecuteScalar(@"SELECT COUNT(*) FROM data"));
                    }
                }
                catch (Exception ex)
                {
                    Assert.Fail(
                        @"Failed at iteration {0}.
Events:
{1}
Exception {2}",
                        i, FormatEventQueue(eventQueue), ex);
                }
            }
        }

        [Test(Description = "Connection reuse race chaining transactions, bool distributed")]
        [Explicit]
        public void Connection_reuse_race_chaining_transaction([Values(false, true)] bool distributed)
        {
            for (var i = 1; i <= 100; i++)
            {
                var eventQueue = new ConcurrentQueue<TransactionEvent>();
                try
                {
                    using (var conn1 = OpenConnection(ConnectionStringEnlistOff))
                    {
                        using (var scope = new TransactionScope())
                        {
                            eventQueue.Enqueue(new TransactionEvent("First scope started"));
                            conn1.EnlistTransaction(Transaction.Current);
                            eventQueue.Enqueue(new TransactionEvent("First scope, connection enlisted"));

                            if (distributed)
                            {
                                EnlistResource.EscalateToDistributed(eventQueue);
                                AssertHasDistributedIdentifier();
                            }
                            else
                            {
                                EnlistResource.EnlistVolatile(eventQueue);
                                AssertNoDistributedIdentifier();
                            }

                            Assert.That(conn1.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test1')"), Is.EqualTo(1), "Unexpected first insert rowcount");
                            eventQueue.Enqueue(new TransactionEvent("First insert done"));

                            scope.Complete();
                            eventQueue.Enqueue(new TransactionEvent("First scope completed"));
                        }
                        eventQueue.Enqueue(new TransactionEvent("First scope disposed"));

                        using (var scope = new TransactionScope())
                        {
                            eventQueue.Enqueue(new TransactionEvent("Second scope started"));
                            conn1.EnlistTransaction(Transaction.Current);
                            eventQueue.Enqueue(new TransactionEvent("Second scope, connection enlisted"));

                            if (distributed)
                            {
                                EnlistResource.EscalateToDistributed(eventQueue);
                                AssertHasDistributedIdentifier();
                            }
                            else
                            {
                                EnlistResource.EnlistVolatile(eventQueue);
                                AssertNoDistributedIdentifier();
                            }

                            Assert.That(conn1.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test1')"), Is.EqualTo(1), "Unexpected second insert rowcount");
                            eventQueue.Enqueue(new TransactionEvent("Second insert done"));

                            scope.Complete();
                            eventQueue.Enqueue(new TransactionEvent("Second scope completed"));
                        }
                        eventQueue.Enqueue(new TransactionEvent("Second scope disposed"));
                    }
                }
                catch (Exception ex)
                {
                    Assert.Fail(
                        @"Failed at iteration {0}.
Events:
{1}
Exception {2}",
                        i, FormatEventQueue(eventQueue), ex);
                }
            }
        }

        [Test]
        public void Reuse_connection_with_escalation()
        {
            using (new TransactionScope())
            {
                using (var conn1 = new OpenGaussConnection(ConnectionStringEnlistOn))
                {
                    conn1.Open();
                    var processId = conn1.ProcessID;
                    using (new OpenGaussConnection(ConnectionStringEnlistOn)) { }
                    conn1.Close();

                    conn1.Open();
                    Assert.That(conn1.ProcessID, Is.EqualTo(processId));
                    conn1.Close();
                }
            }
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1594")]
        public void Bug1594()
        {
            using (new TransactionScope())
            {
                using (var conn = OpenConnection(ConnectionStringEnlistOn))
                using (var innerScope1 = new TransactionScope())
                {
                    conn.ExecuteNonQuery(@"INSERT INTO data (name) VALUES ('test1')");
                    innerScope1.Complete();
                }
                using (OpenConnection(ConnectionStringEnlistOn))
                using (new TransactionScope())
                {
                    // Don't complete, triggering rollback
                }
            }
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1737")]
        public void Multiple_unpooled_connections_do_not_reuse()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                Pooling = false,
                Enlist = true
            };

            using var scope = new TransactionScope();

            int processId;

            using (var conn1 = OpenConnection(csb))
            using (var cmd = new OpenGaussCommand("SELECT 1", conn1))
            {
                processId = conn1.ProcessID;
                cmd.ExecuteNonQuery();
            }

            using (var conn2 = OpenConnection(csb))
            using (var cmd = new OpenGaussCommand("SELECT 1", conn2))
            {
                // The connection reuse optimization isn't implemented for unpooled connections (though it could be)
                Assert.That(conn2.ProcessID, Is.Not.EqualTo(processId));
                cmd.ExecuteNonQuery();
            }

            scope.Complete();
        }

        #region Utilities

        void AssertNoPreparedTransactions()
            => Assert.That(GetNumberOfPreparedTransactions(), Is.EqualTo(0), "Prepared transactions found");

        int GetNumberOfPreparedTransactions()
        {
            using (var conn = OpenConnection(ConnectionStringEnlistOff))
            using (var cmd = new OpenGaussCommand("SELECT COUNT(*) FROM pg_prepared_xacts WHERE database = @database", conn))
            {
                cmd.Parameters.Add(new OpenGaussParameter("database", conn.Database));
                return (int)(long)cmd.ExecuteScalar()!;
            }
        }

        void AssertNumberOfRows(int expected)
          => Assert.That(_controlConn.ExecuteScalar(@"SELECT COUNT(*) FROM data"), Is.EqualTo(expected), "Unexpected data count");

        static void AssertNoDistributedIdentifier()
            => Assert.That(Transaction.Current?.TransactionInformation.DistributedIdentifier ?? Guid.Empty, Is.EqualTo(Guid.Empty), "Distributed identifier found");

        static void AssertHasDistributedIdentifier()
            => Assert.That(Transaction.Current?.TransactionInformation.DistributedIdentifier ?? Guid.Empty, Is.Not.EqualTo(Guid.Empty), "Distributed identifier not found");

        public string ConnectionStringEnlistOn
            => new OpenGaussConnectionStringBuilder(ConnectionString) { Enlist = true }.ToString();

        public string ConnectionStringEnlistOff
            => new OpenGaussConnectionStringBuilder(ConnectionString) { Enlist = false }.ToString();

        static string FormatEventQueue(ConcurrentQueue<TransactionEvent> eventQueue)
        {
            eventQueue.Enqueue(new TransactionEvent(@"-------------
Start formatting event queue, going to sleep a bit for late events
-------------"));
            Thread.Sleep(20);
            var eventsMessage = new StringBuilder();
            foreach (var evt in eventQueue)
            {
                eventsMessage.AppendLine(evt.Message);
            }
            return eventsMessage.ToString();
        }

        // Idea from NHibernate test project, DtcFailuresFixture
        public class EnlistResource : IEnlistmentNotification
        {
            public static int Counter { get; set; }

            readonly bool _shouldRollBack;
            readonly string _name;
            readonly ConcurrentQueue<TransactionEvent>? _eventQueue;

            public static void EnlistVolatile(ConcurrentQueue<TransactionEvent> eventQueue)
                => EnlistVolatile(false, eventQueue);

            public static void EnlistVolatile(bool shouldRollBack = false, ConcurrentQueue<TransactionEvent>? eventQueue = null)
                => Enlist(false, shouldRollBack, eventQueue);

            public static void EscalateToDistributed(ConcurrentQueue<TransactionEvent> eventQueue)
                => EscalateToDistributed(false, eventQueue);

            public static void EscalateToDistributed(bool shouldRollBack = false, ConcurrentQueue<TransactionEvent>? eventQueue = null)
                => Enlist(true, shouldRollBack, eventQueue);

            static void Enlist(bool durable, bool shouldRollBack, ConcurrentQueue<TransactionEvent>? eventQueue)
            {
                Counter++;

                var name = $"{(durable ? "Durable" : "Volatile")} resource {Counter}";
                var resource = new EnlistResource(shouldRollBack, name, eventQueue);
                if (durable)
                    Transaction.Current!.EnlistDurable(Guid.NewGuid(), resource, EnlistmentOptions.None);
                else
                    Transaction.Current!.EnlistVolatile(resource, EnlistmentOptions.None);

                Transaction.Current.TransactionCompleted += resource.Current_TransactionCompleted!;

                eventQueue?.Enqueue(new TransactionEvent(name + ": enlisted"));
            }

            EnlistResource(bool shouldRollBack, string name, ConcurrentQueue<TransactionEvent>? eventQueue)
            {
                _shouldRollBack = shouldRollBack;
                _name = name;
                _eventQueue = eventQueue;
            }

            public void Prepare(PreparingEnlistment preparingEnlistment)
            {
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": prepare phase start"));
                Thread.Sleep(1);
                if (_shouldRollBack)
                {
                    _eventQueue?.Enqueue(new TransactionEvent(_name + ": prepare phase, calling rollback-ed"));
                    preparingEnlistment.ForceRollback();
                }
                else
                {
                    _eventQueue?.Enqueue(new TransactionEvent(_name + ": prepare phase, calling prepared"));
                    preparingEnlistment.Prepared();
                }
                Thread.Sleep(1);
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": prepare phase end"));
            }

            public void Commit(Enlistment enlistment)
            {
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": commit phase start"));
                Thread.Sleep(1);
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": commit phase, calling done"));
                enlistment.Done();
                Thread.Sleep(1);
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": commit phase end"));
            }

            public void Rollback(Enlistment enlistment)
            {
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": rollback phase start"));
                Thread.Sleep(1);
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": rollback phase, calling done"));
                enlistment.Done();
                Thread.Sleep(1);
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": rollback phase end"));
            }

            public void InDoubt(Enlistment enlistment)
            {
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": in-doubt phase start"));
                Thread.Sleep(1);
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": in-doubt phase, calling done"));
                enlistment.Done();
                Thread.Sleep(1);
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": in-doubt phase end"));
            }

            void Current_TransactionCompleted(object sender, TransactionEventArgs e)
            {
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": transaction completed start"));
                Thread.Sleep(1);
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": transaction completed middle"));
                Thread.Sleep(1);
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": transaction completed end"));
            }
        }

        public class TransactionEvent
        {
            public TransactionEvent(string message)
            {
                Message = $"{message} (TId {Thread.CurrentThread.ManagedThreadId})";
            }
            public string Message { get; }
        }

        #endregion Utilities

        #region Setup

        OpenGaussConnection _controlConn = default!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            using (new TransactionScope(TransactionScopeOption.RequiresNew))
            {
                try
                {
                    Transaction.Current!.EnlistPromotableSinglePhase(new FakePromotableSinglePhaseNotification());
                }
                catch (NotImplementedException)
                {
                    Assert.Ignore("Promotable single phase transactions aren't supported (mono < 3.0.0?)");
                }
            }

            _controlConn = OpenConnection();

            // Make sure prepared transactions are enabled in postgresql.conf (disabled by default)
            if (int.Parse((string)_controlConn.ExecuteScalar("SHOW max_prepared_transactions")!) == 0)
            {
                TestUtil.IgnoreExceptOnBuildServer("max_prepared_transactions is set to 0 in your postgresql.conf");
                _controlConn.Close();
            }

            // Rollback any lingering prepared transactions from failed previous runs
            var lingeringTrqnsqctions = new List<string>();
            using (var cmd = new OpenGaussCommand("SELECT gid FROM pg_prepared_xacts WHERE database=@database", _controlConn))
            {
                cmd.Parameters.AddWithValue("database", new OpenGaussConnectionStringBuilder(ConnectionString).Database!);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        lingeringTrqnsqctions.Add(reader.GetString(0));
                }
            }
            foreach (var xactGid in lingeringTrqnsqctions)
                _controlConn.ExecuteNonQuery($"ROLLBACK PREPARED '{xactGid}'");

            // All tests in this fixture should have exclusive access to the database they're running on.
            // If we run these tests in parallel (i.e. two builds in parallel) they will interfere.
            // Solve this by taking a PostgreSQL advisory lock for the lifetime of the fixture.
            _controlConn.ExecuteNonQuery("SELECT pg_advisory_lock(666)");

            _controlConn.ExecuteNonQuery("DROP TABLE IF EXISTS data");
            _controlConn.ExecuteNonQuery("CREATE TABLE data (name TEXT)");
        }

        [SetUp]
        public void SetUp()
        {
            _controlConn.ExecuteNonQuery("TRUNCATE data");
            EnlistResource.Counter = 0;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _controlConn?.Close();
            _controlConn = null!;
        }

        class FakePromotableSinglePhaseNotification : IPromotableSinglePhaseNotification
        {
            public byte[] Promote() => null!;
            public void Initialize() {}
            public void SinglePhaseCommit(SinglePhaseEnlistment singlePhaseEnlistment) {}
            public void Rollback(SinglePhaseEnlistment singlePhaseEnlistment) {}
        }

        #endregion
    }
}

#endif
