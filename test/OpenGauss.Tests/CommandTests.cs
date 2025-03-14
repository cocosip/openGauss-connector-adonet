using System;
using System.Buffers.Binary;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenGauss.NET;
using OpenGauss.NET.Internal;
using OpenGauss.Tests.Support;
using OpenGauss.NET.Types;
using NUnit.Framework;
using static OpenGauss.Tests.TestUtil;
using OpenGauss.NET.TypeMapping;
using OpenGauss.NET.BackendMessages;

namespace OpenGauss.Tests
{
    public class CommandTests : MultiplexingTestBase
    {
        #region Legacy batching

        [Test]
        [TestCase(new[] { true }, TestName = "SingleQuery")]
        [TestCase(new[] { false }, TestName = "SingleNonQuery")]
        [TestCase(new[] { true, true }, TestName = "TwoQueries")]
        [TestCase(new[] { false, false }, TestName = "TwoNonQueries")]
        [TestCase(new[] { false, true }, TestName = "NonQueryQuery")]
        [TestCase(new[] { true, false }, TestName = "QueryNonQuery")]
        public async Task Multiple_statements(bool[] queries)
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "name TEXT", out var table);
            var sb = new StringBuilder();
            foreach (var query in queries)
                sb.Append(query ? "SELECT 1;" : $"UPDATE {table} SET name='yo' WHERE 1=0;");
            var sql = sb.ToString();
            foreach (var prepare in new[] { false, true })
            {
                using var cmd = new OpenGaussCommand(sql, conn);
                if (prepare && !IsMultiplexing)
                    cmd.Prepare();
                using var reader = await cmd.ExecuteReaderAsync();
                var numResultSets = queries.Count(q => q);
                for (var i = 0; i < numResultSets; i++)
                {
                    Assert.That(reader.Read(), Is.True);
                    Assert.That(reader[0], Is.EqualTo(1));
                    Assert.That(reader.NextResult(), Is.EqualTo(i != numResultSets - 1));
                }
            }
        }

        [Test]
        public async Task Multiple_statements_with_parameters([Values(PrepareOrNot.NotPrepared, PrepareOrNot.Prepared)] PrepareOrNot prepare)
        {
            if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p1; SELECT @p2", conn);
            var p1 = new OpenGaussParameter("p1", OpenGaussDbType.Integer);
            var p2 = new OpenGaussParameter("p2", OpenGaussDbType.Text);
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();
            p1.Value = 8;
            p2.Value = "foo";
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(8));
            Assert.That(reader.NextResult(), Is.True);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("foo"));
            Assert.That(reader.NextResult(), Is.False);
        }

        [Test]
        public async Task SingleRow_legacy_batching([Values(PrepareOrNot.NotPrepared, PrepareOrNot.Prepared)] PrepareOrNot prepare)
        {
            if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT 1; SELECT 2", conn);
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));
            Assert.That(reader.Read(), Is.False);
            Assert.That(reader.NextResult(), Is.False);
        }

        [Test, Description("Makes sure a later command can depend on an earlier one")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/641")]
        public async Task Multiple_statements_with_dependencies()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "a INT", out var table);

            await conn.ExecuteNonQueryAsync($"ALTER TABLE {table} ADD COLUMN b INT; INSERT INTO {table} (b) VALUES (8)");
            Assert.That(await conn.ExecuteScalarAsync($"SELECT b FROM {table}"), Is.EqualTo(8));
        }

        [Test, Description("Forces async write mode when the first statement in a multi-statement command is big")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/641")]
        public async Task Multiple_statements_large_first_command()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand($"SELECT repeat('X', {conn.Settings.WriteBufferSize}); SELECT @p", conn);
            var expected1 = new string('X', conn.Settings.WriteBufferSize);
            var expected2 = new string('Y', conn.Settings.WriteBufferSize);
            cmd.Parameters.AddWithValue("p", expected2);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetString(0), Is.EqualTo(expected1));
            reader.NextResult();
            reader.Read();
            Assert.That(reader.GetString(0), Is.EqualTo(expected2));
        }

        [Test, NonParallelizable]
        public async Task Legacy_batching_is_not_supported_when_EnableSqlParsing_is_disabled()
        {
            using var _ = DisableSqlRewriting();

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT 1; SELECT 2", conn);
            Assert.That(async () => await cmd.ExecuteReaderAsync(), Throws.Exception.TypeOf<PostgresException>()
                .With.Property(nameof(PostgresException.SqlState)).EqualTo("42601"));
        }

        #endregion

        #region Timeout

        [Test, Description("Checks that CommandTimeout gets enforced as a socket timeout")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/327")]
        [CancelAfter(10000)]
        public async Task Timeout()
        {
            if (IsMultiplexing)
                return; // Multiplexing, Timeout

            // Mono throws a socket exception with WouldBlock instead of TimedOut (see #1330)
            var isMono = Type.GetType("Mono.Runtime") != null;
            using var conn = await OpenConnectionAsync(ConnectionString + ";CommandTimeout=1");
            using var cmd = CreateSleepCommand(conn, 10);
            Assert.That(() => cmd.ExecuteNonQuery(), Throws.Exception
                .TypeOf<OpenGaussException>()
                .With.InnerException.TypeOf<TimeoutException>()
                );
            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Open));
        }

        [Test, Description("Times out an async operation, testing that cancellation occurs successfully")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/607")]
        [CancelAfter(10000)]
        public async Task Timeout_async_soft()
        {
            if (IsMultiplexing)
                return; // Multiplexing, Timeout

            using var conn = await OpenConnectionAsync(builder => builder.CommandTimeout = 1);
            using var cmd = CreateSleepCommand(conn, 10);
            Assert.That(async () => await cmd.ExecuteNonQueryAsync(),
                Throws.Exception
                    .TypeOf<OpenGaussException>()
                    .With.InnerException.TypeOf<TimeoutException>());
            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Open));
        }

        [Test, Description("Times out an async operation, with unsuccessful cancellation (socket break)")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/607")]
        [CancelAfter(10000)]
        public async Task Timeout_async_hard()
        {
            if (IsMultiplexing)
                return; // Multiplexing, Timeout

            var builder = new OpenGaussConnectionStringBuilder(ConnectionString) { CommandTimeout = 1 };
            await using var postmasterMock = PgPostmasterMock.Start(builder.ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);
            await postmasterMock.WaitForServerConnection();

            var processId = conn.ProcessID;

            Assert.That(async () => await conn.ExecuteScalarAsync("SELECT 1"),
                Throws.Exception
                    .TypeOf<OpenGaussException>()
                    .With.InnerException.TypeOf<TimeoutException>());

            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Broken));
            Assert.That((await postmasterMock.WaitForCancellationRequest()).ProcessId,
                Is.EqualTo(processId));
        }

        [Test]
        public async Task Timeout_from_connection_string()
        {
            Assert.That(OpenGaussConnector.MinimumInternalCommandTimeout, Is.Not.EqualTo(OpenGaussCommand.DefaultTimeout));
            var timeout = OpenGaussConnector.MinimumInternalCommandTimeout;
            var connString = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                CommandTimeout = timeout
            }.ToString();
            using var conn = new OpenGaussConnection(connString);
            var command = new OpenGaussCommand("SELECT 1", conn);
            conn.Open();
            Assert.That(command.CommandTimeout, Is.EqualTo(timeout));
            command.CommandTimeout = 10;
            await command.ExecuteScalarAsync();
            Assert.That(command.CommandTimeout, Is.EqualTo(10));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/395")]
        public async Task Timeout_switch_connection()
        {
            using (var conn = new OpenGaussConnection(ConnectionString))
            {
                if (conn.CommandTimeout >= 100 && conn.CommandTimeout < 105)
                    TestUtil.IgnoreExceptOnBuildServer("Bad default command timeout");
            }

            using (var c1 = await OpenConnectionAsync(ConnectionString + ";CommandTimeout=100"))
            {
                using (var cmd = c1.CreateCommand())
                {
                    Assert.That(cmd.CommandTimeout, Is.EqualTo(100));
                    using (var c2 = new OpenGaussConnection(ConnectionString + ";CommandTimeout=101"))
                    {
                        cmd.Connection = c2;
                        Assert.That(cmd.CommandTimeout, Is.EqualTo(101));
                    }
                    cmd.CommandTimeout = 102;
                    using (var c2 = new OpenGaussConnection(ConnectionString + ";CommandTimeout=101"))
                    {
                        cmd.Connection = c2;
                        Assert.That(cmd.CommandTimeout, Is.EqualTo(102));
                    }
                }
            }
        }

        [Test]
        [CancelAfter(10000)]
        public async Task Prepare_timeout_hard([Values] SyncOrAsync async)
        {
            if (IsMultiplexing)
                return; // Multiplexing, Timeout

            var builder = new OpenGaussConnectionStringBuilder(ConnectionString) { CommandTimeout = 1 };
            await using var postmasterMock = PgPostmasterMock.Start(builder.ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);
            await postmasterMock.WaitForServerConnection();

            var processId = conn.ProcessID;

            var cmd = new OpenGaussCommand("SELECT 1", conn);
            Assert.That(async () =>
                {
                    if (async == SyncOrAsync.Sync)
                        cmd.Prepare();
                    else
                        await cmd.PrepareAsync();
                },
                Throws.Exception
                    .TypeOf<OpenGaussException>()
                    .With.InnerException.TypeOf<TimeoutException>());

            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Broken));
            Assert.That((await postmasterMock.WaitForCancellationRequest()).ProcessId,
                Is.EqualTo(processId));
        }

        #endregion

        #region Cancel

        [Test, Description("Basic cancellation scenario")]
        [CancelAfter(6000)]
        public async Task Cancel()
        {
            if (IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            using var cmd = CreateSleepCommand(conn, 5);

            var queryTask = Task.Run(() => cmd.ExecuteNonQuery());
            // We have to be sure the command's state is InProgress, otherwise the cancellation request will never be sent
            cmd.WaitUntilCommandIsInProgress();
            cmd.Cancel();
            Assert.That(async () => await queryTask, Throws
                .TypeOf<OperationCanceledException>()
                .With.InnerException.TypeOf<PostgresException>()
                .With.InnerException.Property(nameof(PostgresException.SqlState)).EqualTo(PostgresErrorCodes.QueryCanceled)
            );
        }

        [Test]
        public async Task Cancel_async_immediately()
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await using var conn = await OpenConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";

            var t = cmd.ExecuteScalarAsync(cts.Token);
            Assert.That(t.IsCompleted, Is.True); // checks, if a query has completed synchronously
            Assert.That(t.Status, Is.EqualTo(TaskStatus.Canceled));
            Assert.ThrowsAsync<OperationCanceledException>(async () => await t);

            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Open));
            Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));
        }

        [Test, Description("Cancels an async query with the cancellation token, with successful PG cancellation")]
        public async Task Cancel_async_soft()
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            await using var conn = await OpenConnectionAsync();
            using var cmd = CreateSleepCommand(conn);
            using var cancellationSource = new CancellationTokenSource();
            var t = cmd.ExecuteNonQueryAsync(cancellationSource.Token);
            cancellationSource.Cancel();

            var exception = Assert.ThrowsAsync<OperationCanceledException>(async () => await t)!;
            Assert.That(exception.InnerException,
                Is.TypeOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo(PostgresErrorCodes.QueryCanceled));
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellationSource.Token));

            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Open));
            Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));
        }

        [Test, Description("Cancels an async query with the cancellation token, with unsuccessful PG cancellation (socket break)")]
        public async Task Cancel_async_hard()
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);
            await postmasterMock.WaitForServerConnection();

            var processId = conn.ProcessID;

            using var cancellationSource = new CancellationTokenSource();
            using var cmd = new OpenGaussCommand("SELECT 1", conn);
            var t = cmd.ExecuteScalarAsync(cancellationSource.Token);
            cancellationSource.Cancel();

            var exception = Assert.ThrowsAsync<OperationCanceledException>(async () => await t)!;
            Assert.That(exception.InnerException, Is.TypeOf<TimeoutException>());
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellationSource.Token));

            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Broken));
            Assert.That((await postmasterMock.WaitForCancellationRequest()).ProcessId,
                Is.EqualTo(processId));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/3466")]
        [CancelAfter(6000)]
        public async Task Bug3466([Values(false, true)] bool isBroken)
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                Pooling = false,
            };
            await using var postmasterMock = PgPostmasterMock.Start(csb.ToString(), completeCancellationImmediately: false);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);
            var serverMock = await postmasterMock.WaitForServerConnection();

            var processId = conn.ProcessID;

            using var cancellationSource = new CancellationTokenSource();
            using var cmd = new OpenGaussCommand("SELECT 1", conn)
            {
                CommandTimeout = 3
            };
            var t = Task.Run(() => cmd.ExecuteScalar());
            // We have to be sure the command's state is InProgress, otherwise the cancellation request will never be sent
            cmd.WaitUntilCommandIsInProgress();
            // Perform cancellation, which will block on the server side
            var cancelTask = Task.Run(() => cmd.Cancel());
            // Note what we have to wait for the cancellation request, otherwise the connection might be closed concurrently
            // and the cancellation request is never send
            var cancellationRequest = await postmasterMock.WaitForCancellationRequest();

            if (isBroken)
            {
                Assert.ThrowsAsync<OperationCanceledException>(async () => await t);
                Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Broken));
            }
            else
            {
                await serverMock
                    .WriteParseComplete()
                    .WriteBindComplete()
                    .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Int4))
                    .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
                    .WriteCommandComplete()
                    .WriteReadyForQuery()
                    .FlushAsync();
                Assert.DoesNotThrowAsync(async () => await t);
                Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Open));
                await conn.CloseAsync();
            }

            // Release the cancellation at the server side, and make sure it completes without an exception
            cancellationRequest.Complete();
            Assert.DoesNotThrowAsync(async () => await cancelTask);
        }

        [Test, Description("Check that cancel only affects the command on which its was invoked")]
        [Explicit("Timing-sensitive")]
        [CancelAfter(3000)]
        public async Task Cancel_cross_command()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd1 = CreateSleepCommand(conn, 2);
            using var cmd2 = new OpenGaussCommand("SELECT 1", conn);
            var cancelTask = Task.Factory.StartNew(() =>
            {
                Thread.Sleep(300);
                cmd2.Cancel();
            });
            Assert.That(() => cmd1.ExecuteNonQueryAsync(), Throws.Nothing);
            cancelTask.Wait();
        }

        #endregion

        #region Cursors

        [Test]
        public async Task Cursor_statement()
        {
            using var conn = await OpenConnectionAsync();
            using var t = conn.BeginTransaction();
            await using var _ = await CreateTempTable(conn, "name TEXT", out var table);

            for (var x = 0; x < 5; x++)
                await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('X')");

            var i = 0;
            var command = new OpenGaussCommand($"DECLARE TE CURSOR FOR SELECT * FROM {table}", conn);
            command.ExecuteNonQuery();
            command.CommandText = "FETCH FORWARD 3 IN TE";
            var dr = command.ExecuteReader();

            while (dr.Read())
                i++;
            Assert.That(i, Is.EqualTo(3));
            dr.Close();

            i = 0;
            command.CommandText = "FETCH BACKWARD 1 IN TE";
            var dr2 = command.ExecuteReader();
            while (dr2.Read())
                i++;
            Assert.That(i, Is.EqualTo(1));
            dr2.Close();

            command.CommandText = "close te;";
            command.ExecuteNonQuery();
        }

        [Test]
        public async Task Cursor_move_RecordsAffected()
        {
            using var connection = await OpenConnectionAsync();
            using var transaction = connection.BeginTransaction();
            var command = new OpenGaussCommand("DECLARE curs CURSOR FOR SELECT * FROM (VALUES (1), (2), (3)) as t", connection);
            command.ExecuteNonQuery();
            command.CommandText = "MOVE FORWARD ALL IN curs";
            var count = command.ExecuteNonQuery();
            Assert.That(count, Is.EqualTo(3));
        }

        #endregion

        #region CommandBehavior.CloseConnection

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/693")]
        public async Task CloseConnection()
        {
            using var conn = await OpenConnectionAsync();
            using (var cmd = new OpenGaussCommand("SELECT 1", conn))
            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                while (reader.Read()) { }
            Assert.That(conn.State, Is.EqualTo(ConnectionState.Closed));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1194")]
        public async Task CloseConnection_with_open_reader_with_CloseConnection()
        {
            using var conn = await OpenConnectionAsync();
            var cmd = new OpenGaussCommand("SELECT 1", conn);
            var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
            var wasClosed = false;
            reader.ReaderClosed += (sender, args) => { wasClosed = true; };
            conn.Close();
            Assert.That(wasClosed, Is.True);
        }

        [Test]
        public async Task CloseConnection_with_exception()
        {
            using var conn = await OpenConnectionAsync();
            using (var cmd = new OpenGaussCommand("SE", conn))
                Assert.That(() => cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection), Throws.Exception.TypeOf<PostgresException>());
            Assert.That(conn.State, Is.EqualTo(ConnectionState.Closed));
        }

        #endregion

        [Test]
        public async Task SingleRow([Values(PrepareOrNot.NotPrepared, PrepareOrNot.Prepared)] PrepareOrNot prepare)
        {
            if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT 1, 2 UNION SELECT 3, 4", conn);
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();

            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            Assert.That(() => reader.GetInt32(0), Throws.Exception.TypeOf<InvalidOperationException>());
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));
            Assert.That(reader.Read(), Is.False);
        }

        #region Parameters

        [Test]
        public async Task Positional_parameter()
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = new OpenGaussCommand("SELECT $1", conn);
            cmd.Parameters.Add(new OpenGaussParameter { OpenGaussDbType = OpenGaussDbType.Integer, Value = 8 });
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(8));
        }

        [Test]
        public async Task Positional_parameters_are_not_supported_with_legacy_batching()
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = new OpenGaussCommand("SELECT $1; SELECT $1", conn);
            cmd.Parameters.Add(new OpenGaussParameter { OpenGaussDbType = OpenGaussDbType.Integer, Value = 8 });
            Assert.That(async () => await cmd.ExecuteScalarAsync(), Throws.Exception.TypeOf<PostgresException>()
                .With.Property(nameof(PostgresException.SqlState)).EqualTo(PostgresErrorCodes.SyntaxError));
        }

        [Test, NonParallelizable]
        public async Task Positional_parameters_are_supported_when_EnableSqlParsing_is_disabled()
        {
            using var _ = DisableSqlRewriting();

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT $1", conn);
            cmd.Parameters.Add(new OpenGaussParameter { OpenGaussDbType = OpenGaussDbType.Integer, Value = 8 });
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(8));
        }

        [Test, NonParallelizable]
        public async Task Named_parameters_are_not_supported_when_EnableSqlParsing_is_disabled()
        {
            using var _ = DisableSqlRewriting();

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p", conn);
            cmd.Parameters.Add(new OpenGaussParameter("p", 8));
            Assert.That(async () => await cmd.ExecuteScalarAsync(), Throws.Exception.TypeOf<NotSupportedException>());
        }

        [Test, Description("Makes sure writing an unset parameter isn't allowed")]
        public async Task Parameter_without_Value()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p", conn);
            cmd.Parameters.Add(new OpenGaussParameter("@p", OpenGaussDbType.Integer));
            Assert.That(() => cmd.ExecuteScalarAsync(), Throws.Exception.TypeOf<InvalidCastException>());
        }

        [Test]
        public async Task Unreferenced_named_parameter_works()
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = new OpenGaussCommand("SELECT 1", conn);
            cmd.Parameters.AddWithValue("not_used", 8);
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(1));
        }

        [Test]
        public async Task Unreferenced_positional_parameter_works()
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = new OpenGaussCommand("SELECT 1", conn);
            cmd.Parameters.Add(new OpenGaussParameter { Value = 8 });
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(1));
        }

        [Test]
        public async Task Mixing_positional_and_named_parameters_is_not_supported()
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = new OpenGaussCommand("SELECT $1, @p", conn);
            cmd.Parameters.Add(new OpenGaussParameter { Value = 8 });
            cmd.Parameters.Add(new OpenGaussParameter { ParameterName = "p", Value = 9 });
            Assert.That(() => cmd.ExecuteNonQueryAsync(), Throws.Exception.TypeOf<NotSupportedException>());
        }

        [Test]
        [IssueLink("https://github.com/opengauss/opengauss/issues/4171")]
        public async Task Cached_command_clears_parameters_placeholder_type()
        {
            await using var conn = await OpenConnectionAsync();

            await using (var cmd1 = conn.CreateCommand())
            {
                cmd1.CommandText = "SELECT @p1";
                cmd1.Parameters.AddWithValue("@p1", 8);
                await using var reader1 = await cmd1.ExecuteReaderAsync();
                reader1.Read();
                Assert.That(reader1[0], Is.EqualTo(8));
            }

            await using (var cmd2 = conn.CreateCommand())
            {
                cmd2.CommandText = "SELECT $1";
                cmd2.Parameters.AddWithValue(8);
                await using var reader2 = await cmd2.ExecuteReaderAsync();
                reader2.Read();
                Assert.That(reader2[0], Is.EqualTo(8));
            }
        }

        [Test]
        [IssueLink("https://github.com/opengauss/opengauss/issues/4171")]
        public async Task Reuse_command_with_different_parameter_placeholder_types()
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT @p1";
            cmd.Parameters.AddWithValue("@p1", 8);
            _ = await cmd.ExecuteScalarAsync();

            cmd.CommandText = "SELECT $1";
            cmd.Parameters[0].ParameterName = null;
            _ = await cmd.ExecuteScalarAsync();
        }

        [Test]
        public async Task Positional_output_parameters_are_not_supported()
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = new OpenGaussCommand("SELECT $1", conn);
            cmd.Parameters.Add(new OpenGaussParameter { Value = 8, Direction = ParameterDirection.InputOutput });
            Assert.That(() => cmd.ExecuteNonQueryAsync(), Throws.Exception.TypeOf<NotSupportedException>());
        }

        [Test]
        public void Parameters_get_name()
        {
            var command = new OpenGaussCommand();

            // Add parameters.
            command.Parameters.Add(new OpenGaussParameter(":Parameter1", DbType.Boolean));
            command.Parameters.Add(new OpenGaussParameter(":Parameter2", DbType.Int32));
            command.Parameters.Add(new OpenGaussParameter(":Parameter3", DbType.DateTime));
            command.Parameters.Add(new OpenGaussParameter("Parameter4", DbType.DateTime));

            var idbPrmtr = command.Parameters["Parameter1"];
            Assert.That(idbPrmtr, Is.Not.Null);
            command.Parameters[0].Value = 1;

            // Get by indexers.

            Assert.That(command.Parameters["Parameter1"].ParameterName, Is.EqualTo(":Parameter1"));
            Assert.That(command.Parameters["Parameter2"].ParameterName, Is.EqualTo(":Parameter2"));
            Assert.That(command.Parameters["Parameter3"].ParameterName, Is.EqualTo(":Parameter3"));
            Assert.That(command.Parameters["Parameter4"].ParameterName, Is.EqualTo("Parameter4")); //Should this work?

            Assert.That(command.Parameters[0].ParameterName, Is.EqualTo(":Parameter1"));
            Assert.That(command.Parameters[1].ParameterName, Is.EqualTo(":Parameter2"));
            Assert.That(command.Parameters[2].ParameterName, Is.EqualTo(":Parameter3"));
            Assert.That(command.Parameters[3].ParameterName, Is.EqualTo("Parameter4"));
        }

        [Test]
        public async Task Same_param_multiple_times()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p1, @p1", conn);
            cmd.Parameters.AddWithValue("@p1", 8);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader[0], Is.EqualTo(8));
            Assert.That(reader[1], Is.EqualTo(8));
        }

        [Test]
        public async Task Generic_parameter()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p1, @p2, @p3, @p4", conn);
            cmd.Parameters.Add(new OpenGaussParameter<int>("p1", 8));
            cmd.Parameters.Add(new OpenGaussParameter<short>("p2", 8) { OpenGaussDbType = OpenGaussDbType.Integer });
            cmd.Parameters.Add(new OpenGaussParameter<string>("p3", "hello"));
            cmd.Parameters.Add(new OpenGaussParameter<char[]>("p4", new[] { 'f', 'o', 'o' }));
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetInt32(0), Is.EqualTo(8));
            Assert.That(reader.GetInt32(1), Is.EqualTo(8));
            Assert.That(reader.GetString(2), Is.EqualTo("hello"));
            Assert.That(reader.GetString(3), Is.EqualTo("foo"));
        }

        #endregion Parameters

        [Test]
        public async Task CommandText_not_set()
        {
            using var conn = await OpenConnectionAsync();
            using (var cmd = new OpenGaussCommand())
            {
                cmd.Connection = conn;
                Assert.That(cmd.ExecuteNonQueryAsync, Throws.Exception.TypeOf<InvalidOperationException>());
                cmd.CommandText = null;
                Assert.That(cmd.ExecuteNonQueryAsync, Throws.Exception.TypeOf<InvalidOperationException>());
                cmd.CommandText = "";
            }

            using (var cmd = conn.CreateCommand())
                Assert.That(cmd.ExecuteNonQueryAsync, Throws.Exception.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task ExecuteScalar()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "name TEXT", out var table);
            using var command = new OpenGaussCommand($"SELECT name FROM {table}", conn);
            Assert.That(command.ExecuteScalarAsync, Is.Null);

            await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES (NULL)");
            Assert.That(command.ExecuteScalarAsync, Is.EqualTo(DBNull.Value));

            await conn.ExecuteNonQueryAsync($"TRUNCATE {table}");
            for (var i = 0; i < 2; i++)
                await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('X')");
            Assert.That(command.ExecuteScalarAsync, Is.EqualTo("X"));
        }

        [Test]
        public async Task ExecuteNonQuery()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand { Connection = conn };
            await using var _ = await CreateTempTable(conn, "name TEXT", out var table);

            cmd.CommandText = $"INSERT INTO {table} (name) VALUES ('John')";
            Assert.That(cmd.ExecuteNonQueryAsync, Is.EqualTo(1));

            cmd.CommandText = $"INSERT INTO {table} (name) VALUES ('John'); INSERT INTO {table} (name) VALUES ('John')";
            Assert.That(cmd.ExecuteNonQueryAsync, Is.EqualTo(2));

            cmd.CommandText = $"INSERT INTO {table} (name) VALUES ('{new string('x', conn.Settings.WriteBufferSize)}')";
            Assert.That(cmd.ExecuteNonQueryAsync, Is.EqualTo(1));
        }

        [Test, Description("Makes sure a command is unusable after it is disposed")]
        public async Task Dispose()
        {
            using var conn = await OpenConnectionAsync();
            var cmd = new OpenGaussCommand("SELECT 1", conn);
            cmd.Dispose();
            Assert.That(() => cmd.ExecuteScalarAsync(), Throws.Exception.TypeOf<ObjectDisposedException>());
            Assert.That(() => cmd.ExecuteNonQueryAsync(), Throws.Exception.TypeOf<ObjectDisposedException>());
            Assert.That(() => cmd.ExecuteReaderAsync(), Throws.Exception.TypeOf<ObjectDisposedException>());
            Assert.That(() => cmd.PrepareAsync(), Throws.Exception.TypeOf<ObjectDisposedException>());
        }

        [Test, Description("Disposing a command with an open reader does not close the reader. This is the SqlClient behavior.")]
        public async Task Command_Dispose_does_not_close_reader()
        {
            using var conn = await OpenConnectionAsync();
            var cmd = new OpenGaussCommand("SELECT 1, 2", conn);
            await cmd.ExecuteReaderAsync();
            cmd.Dispose();
            cmd = new OpenGaussCommand("SELECT 3", conn);
            Assert.That(() => cmd.ExecuteScalarAsync(), Throws.Exception.TypeOf<OpenGaussOperationInProgressException>());
        }

        [Test]
        public async Task Non_standards_conforming_strings()
        {
            using var conn = await OpenConnectionAsync();

            if (IsMultiplexing)
            {
                Assert.That(() => conn.ExecuteNonQueryAsync("set standard_conforming_strings=off"),
                    Throws.Exception.TypeOf<NotSupportedException>());
            }
            else
            {
                await conn.ExecuteNonQueryAsync("set standard_conforming_strings=off");
                Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));
                await conn.ExecuteNonQueryAsync("set standard_conforming_strings=on");
            }
        }

        [Test]
        public async Task Parameter_and_operator_unclear()
        {
            using var conn = await OpenConnectionAsync();
            //Without parenthesis the meaning of [, . and potentially other characters is
            //a syntax error. See comment in OpenGaussCommand.GetClearCommandText() on "usually-redundant parenthesis".
            using var command = new OpenGaussCommand("select :arr[2]", conn);
            command.Parameters.AddWithValue(":arr", new int[] { 5, 4, 3, 2, 1 });
            using var rdr = await command.ExecuteReaderAsync();
            rdr.Read();
            Assert.That(4, Is.EqualTo(rdr.GetInt32(0)));
        }

        [Test]
        [TestCase(CommandBehavior.Default)]
        [TestCase(CommandBehavior.SequentialAccess)]
        public async Task Statement_mapped_output_parameters(CommandBehavior behavior)
        {
            using var conn = await OpenConnectionAsync();
            var command = new OpenGaussCommand("select 3, 4 as param1, 5 as param2, 6;", conn);

            var p = new OpenGaussParameter("param2", OpenGaussDbType.Integer);
            p.Direction = ParameterDirection.Output;
            p.Value = -1;
            command.Parameters.Add(p);

            p = new OpenGaussParameter("param1", OpenGaussDbType.Integer);
            p.Direction = ParameterDirection.Output;
            p.Value = -1;
            command.Parameters.Add(p);

            p = new OpenGaussParameter("p", OpenGaussDbType.Integer);
            p.Direction = ParameterDirection.Output;
            p.Value = -1;
            command.Parameters.Add(p);

            using var reader = await command.ExecuteReaderAsync(behavior);

            Assert.That(command.Parameters["param1"].Value, Is.EqualTo(4));
            Assert.That(command.Parameters["param2"].Value, Is.EqualTo(5));

            reader.Read();

            Assert.That(reader.GetInt32(0), Is.EqualTo(3));
            Assert.That(reader.GetInt32(1), Is.EqualTo(4));
            Assert.That(reader.GetInt32(2), Is.EqualTo(5));
            Assert.That(reader.GetInt32(3), Is.EqualTo(6));
        }

        [Test]
        public async Task Bug1006158_output_parameters()
        {
            using var conn = await OpenConnectionAsync();
            await using (GetTempFunctionName(conn, out var function))
            {
                var createFunction = $@"
CREATE OR REPLACE FUNCTION {function}(OUT a integer, OUT b boolean) AS
$BODY$DECLARE
BEGIN
    a := 3;
    b := true;
END;$BODY$
LANGUAGE 'plpgsql' VOLATILE;";

                var command = new OpenGaussCommand(createFunction, conn);
                await command.ExecuteNonQueryAsync();

                command = new OpenGaussCommand(function, conn);
                command.CommandType = CommandType.StoredProcedure;

                command.Parameters.Add(new OpenGaussParameter("a", DbType.Int32));
                command.Parameters[0].Direction = ParameterDirection.Output;
                command.Parameters.Add(new OpenGaussParameter("b", DbType.Boolean));
                command.Parameters[1].Direction = ParameterDirection.Output;

                var result = await command.ExecuteScalarAsync();

                Assert.That(command.Parameters[0].Value, Is.EqualTo(3));
                Assert.That(command.Parameters[1].Value, Is.EqualTo(true));
            }
        }

        [Test]
        public async Task Bug1010788_UpdateRowSource()
        {
            if (IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "id SERIAL PRIMARY KEY, name TEXT", out var table);

            var command = new OpenGaussCommand($"SELECT * FROM {table}", conn);
            Assert.That(command.UpdatedRowSource, Is.EqualTo(UpdateRowSource.Both));

            var cmdBuilder = new OpenGaussCommandBuilder();
            var da = new OpenGaussDataAdapter(command);
            cmdBuilder.DataAdapter = da;
            Assert.That(da.SelectCommand, Is.Not.Null);
            Assert.That(cmdBuilder.DataAdapter, Is.Not.Null);

            var updateCommand = cmdBuilder.GetUpdateCommand();
            Assert.That(updateCommand.UpdatedRowSource, Is.EqualTo(UpdateRowSource.None));
        }

        [Test]
        public async Task TableDirect()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "name TEXT", out var table);

            await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('foo')");
            using var cmd = new OpenGaussCommand(table, conn) { CommandType = CommandType.TableDirect };
            using var rdr = await cmd.ExecuteReaderAsync();
            Assert.That(rdr.Read(), Is.True);
            Assert.That(rdr["name"], Is.EqualTo("foo"));
        }

        [Test]
        [TestCase(CommandBehavior.Default)]
        [TestCase(CommandBehavior.SequentialAccess)]
        public async Task Input_and_output_parameters(CommandBehavior behavior)
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @c-1 AS c, @a+2 AS b", conn);
            cmd.Parameters.Add(new OpenGaussParameter("a", 3));
            var b = new OpenGaussParameter { ParameterName = "b", Direction = ParameterDirection.Output };
            cmd.Parameters.Add(b);
            var c = new OpenGaussParameter { ParameterName = "c", Direction = ParameterDirection.InputOutput, Value = 4 };
            cmd.Parameters.Add(c);
            using (await cmd.ExecuteReaderAsync(behavior))
            {
                Assert.That(b.Value, Is.EqualTo(5));
                Assert.That(c.Value, Is.EqualTo(3));
            }
        }

        [Test]
        public async Task Send_OpenGaussDbType_Unknown([Values(PrepareOrNot.NotPrepared, PrepareOrNot.Prepared)] PrepareOrNot prepare)
        {
            if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p::TIMESTAMP", conn);
            cmd.CommandText = "SELECT @p::TIMESTAMP";
            cmd.Parameters.Add(new OpenGaussParameter("p", OpenGaussDbType.Unknown) { Value = "2008-1-1" });
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetValue(0), Is.EqualTo(new DateTime(2008, 1, 1)));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/503")]
        public async Task Invalid_UTF8()
        {
            const string badString = "SELECT 'abc\uD801\uD802d'";
            using var conn = await OpenConnectionAsync();
            Assert.That(() => conn.ExecuteScalarAsync(badString), Throws.Exception.TypeOf<EncoderFallbackException>());
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/395")]
        public async Task Use_across_connection_change([Values(PrepareOrNot.Prepared, PrepareOrNot.NotPrepared)] PrepareOrNot prepare)
        {
            if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
                return;

            using var conn1 = await OpenConnectionAsync();
            using var conn2 = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT 1", conn1);
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();
            cmd.Connection = conn2;
            Assert.That(cmd.IsPrepared, Is.False);
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(1));
        }

        [Test, Description("CreateCommand before connection open")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/565")]
        public async Task Create_command_before_connection_open()
        {
            using var conn = new OpenGaussConnection(ConnectionString);
            var cmd = new OpenGaussCommand("SELECT 1", conn);
            conn.Open();
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(1));
        }

        [Test]
        public void Connection_not_set_throws()
        {
            var cmd = new OpenGaussCommand("SELECT 1");
            Assert.That(() => cmd.ExecuteScalarAsync(), Throws.Exception.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Connection_not_open_throws()
        {
            using var conn = CreateConnection();
            var cmd = new OpenGaussCommand("SELECT 1", conn);
            Assert.That(() => cmd.ExecuteScalarAsync(), Throws.Exception.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Command_is_recycled()
        {
            using var conn = OpenConnection();
            var cmd1 = conn.CreateCommand();
            cmd1.CommandText = "SELECT @p1";
            var tx = conn.BeginTransaction();
            cmd1.Transaction = tx;
            cmd1.Parameters.AddWithValue("p1", 8);
            _ = cmd1.ExecuteScalar();
            cmd1.Dispose();

            var cmd2 = conn.CreateCommand();
            Assert.That(cmd2, Is.SameAs(cmd1));
            Assert.That(cmd2.CommandText, Is.Empty);
            Assert.That(cmd2.CommandType, Is.EqualTo(CommandType.Text));
            Assert.That(cmd2.Transaction, Is.Null);
            Assert.That(cmd2.Parameters, Is.Empty);
            // TODO: Leaving this for now, since it'll be replaced by the new batching API
            // Assert.That(cmd2.Statements, Is.Empty);
        }

        [Test]
        public void Command_recycled_resets_CommandType()
        {
            using var conn = CreateConnection();
            var cmd1 = conn.CreateCommand();
            cmd1.CommandType = CommandType.StoredProcedure;
            cmd1.Dispose();

            var cmd2 = conn.CreateCommand();
            Assert.That(cmd2.CommandType, Is.EqualTo(CommandType.Text));
        }

        [Test]
        [IssueLink("https://github.com/opengauss/opengauss/issues/831")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/2795")]
        [CancelAfter(10000)]
        public async Task Many_parameters([Values(PrepareOrNot.NotPrepared, PrepareOrNot.Prepared)] PrepareOrNot prepare)
        {
            if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "some_column INT", out var table);
            using var cmd = new OpenGaussCommand { Connection = conn };
            var sb = new StringBuilder($"INSERT INTO {table} (some_column) VALUES ");
            for (var i = 0; i < ushort.MaxValue; i++)
            {
                var paramName = "p" + i;
                cmd.Parameters.Add(new OpenGaussParameter(paramName, 8));
                if (i > 0)
                    sb.Append(", ");
                sb.Append($"(@{paramName})");
            }

            cmd.CommandText = sb.ToString();

            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();

            await cmd.ExecuteNonQueryAsync();
        }

        [Test, Description("Bypasses PostgreSQL's uint16 limitation on the number of parameters")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/831")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/858")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/2703")]
        public async Task Too_many_parameters_throws([Values(PrepareOrNot.NotPrepared, PrepareOrNot.Prepared)] PrepareOrNot prepare)
        {
            if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand { Connection = conn };
            var sb = new StringBuilder("SOME RANDOM SQL ");
            for (var i = 0; i < ushort.MaxValue + 1; i++)
            {
                var paramName = "p" + i;
                cmd.Parameters.Add(new OpenGaussParameter(paramName, 8));
                if (i > 0)
                    sb.Append(", ");
                sb.Append('@');
                sb.Append(paramName);
            }
            cmd.CommandText = sb.ToString();

            if (prepare == PrepareOrNot.Prepared)
            {
                Assert.That(() => cmd.Prepare(), Throws.Exception
                    .InstanceOf<OpenGaussException>()
                    .With.Message.EqualTo("A statement cannot have more than 65535 parameters"));
            }
            else
            {
                Assert.That(() => cmd.ExecuteNonQueryAsync(), Throws.Exception
                    .InstanceOf<OpenGaussException>()
                    .With.Message.EqualTo("A statement cannot have more than 65535 parameters"));
            }
        }

        [Test, Description("An individual statement cannot have more than 65535 parameters, but a command can (across multiple statements).")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/1199")]
        public async Task Many_parameters_across_statements()
        {
            // Create a command with 1000 statements which have 70 params each
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand { Connection = conn };
            var paramIndex = 0;
            var sb = new StringBuilder();
            for (var statementIndex = 0; statementIndex < 1000; statementIndex++)
            {
                if (statementIndex > 0)
                    sb.Append("; ");
                sb.Append("SELECT ");
                var startIndex = paramIndex;
                var endIndex = paramIndex + 70;
                for (; paramIndex < endIndex; paramIndex++)
                {
                    var paramName = "p" + paramIndex;
                    cmd.Parameters.Add(new OpenGaussParameter(paramName, 8));
                    if (paramIndex > startIndex)
                        sb.Append(", ");
                    sb.Append('@');
                    sb.Append(paramName);
                }
            }

            cmd.CommandText = sb.ToString();
            await cmd.ExecuteNonQueryAsync();
        }

        [Test, Description("Makes sure that OpenGauss doesn't attempt to send all data before the user can start reading. That would cause a deadlock.")]
        public async Task Batched_big_statements_do_not_deadlock()
        {
            // We're going to send a large multistatement query that would exhaust both the client's and server's
            // send and receive buffers (assume 64k per buffer).
            var data = new string('x', 1024);
            using var conn = await OpenConnectionAsync();
            var sb = new StringBuilder();
            for (var i = 0; i < 500; i++)
                sb.Append("SELECT @p;");
            using var cmd = new OpenGaussCommand(sb.ToString(), conn);
            cmd.Parameters.AddWithValue("p", OpenGaussDbType.Text, data);
            using var reader = await cmd.ExecuteReaderAsync();
            for (var i = 0; i < 500; i++)
            {
                reader.Read();
                Assert.That(reader.GetString(0), Is.EqualTo(data));
                reader.NextResult();
            }
        }

        [Test, CancelAfter(10000)]
        public void Batched_small_then_big_statements_do_not_deadlock_in_sync_io()
        {
            if (IsMultiplexing)
                return; // Multiplexing, sync I/O

            // This makes sure we switch to async writing for batches, starting from the 2nd statement at the latest.
            // Otherwise, a small first first statement followed by a huge big one could cause us to deadlock, as we're stuck
            // synchronously sending the 2nd statement while PG is stuck sending the results of the 1st.
            using var conn = OpenConnection();
            var data = new string('x', 5_000_000);
            using var cmd = new OpenGaussCommand("SELECT generate_series(1, 500000); SELECT @p", conn);
            cmd.Parameters.AddWithValue("p", OpenGaussDbType.Text, data);
            cmd.ExecuteNonQuery();
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1429")]
        public async Task Same_command_different_param_values()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p", conn);
            cmd.Parameters.AddWithValue("p", 8);
            await cmd.ExecuteNonQueryAsync();

            cmd.Parameters[0].Value = 9;
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(9));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1429")]
        public async Task Same_command_different_param_instances()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p", conn);
            cmd.Parameters.AddWithValue("p", 8);
            await cmd.ExecuteNonQueryAsync();

            cmd.Parameters.RemoveAt(0);
            cmd.Parameters.AddWithValue("p", 9);
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(9));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/3509"), CancelAfter(5000)]
        public async Task Bug3509()
        {
            if (IsMultiplexing)
                return;

            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                KeepAlive = 1,
            };
            await using var postmasterMock = PgPostmasterMock.Start(csb.ToString());
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);
            var serverMock = await postmasterMock.WaitForServerConnection();
            // Wait for a keepalive to arrive at the server, reply with an error
            await serverMock.WaitForData();
            var queryTask = Task.Run(async () => await conn.ExecuteNonQueryAsync("SELECT 1"));
            // TODO: kind of flaky - think of the way to rewrite
            // giving a queryTask some time to get stuck on a lock
            await Task.Delay(100);
            await serverMock
                .WriteErrorResponse("42")
                .WriteReadyForQuery()
                .FlushAsync();

            await serverMock
                .WriteScalarResponseAndFlush(1);

            var ex = Assert.ThrowsAsync<OpenGaussException>(async () => await queryTask)!;
            Assert.That(ex.InnerException, Is.TypeOf<OpenGaussException>()
                .With.InnerException.TypeOf<PostgresException>());
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/4134")]
        public async Task Cached_command_double_dispose()
        {
            await using var conn = await OpenConnectionAsync();

            var cmd1 = conn.CreateCommand();
            cmd1.Dispose();
            cmd1.Dispose();

            var cmd2 = conn.CreateCommand();
            Assert.That(cmd2, Is.SameAs(cmd1));

            cmd2.CommandText = "SELECT 1";
            Assert.That(await cmd2.ExecuteScalarAsync(), Is.EqualTo(1));
        }

        public CommandTests(MultiplexingMode multiplexingMode) : base(multiplexingMode) { }
    }
}
