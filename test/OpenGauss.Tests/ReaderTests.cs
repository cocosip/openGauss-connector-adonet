﻿using System;
using System.Buffers.Binary;
using System.Collections;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework.Constraints;
using OpenGauss.NET;
using OpenGauss.NET.Internal;
using OpenGauss.NET.Internal.TypeHandling;
using OpenGauss.NET.PostgresTypes;
using OpenGauss.Tests.Support;
using OpenGauss.NET.Types;
using NUnit.Framework;
using static OpenGauss.Tests.TestUtil;
using OpenGauss.NET.BackendMessages;
using OpenGauss.NET.TypeMapping;

namespace OpenGauss.Tests
{
    [TestFixture(MultiplexingMode.NonMultiplexing, CommandBehavior.Default)]
    [TestFixture(MultiplexingMode.Multiplexing, CommandBehavior.Default)]
    [TestFixture(MultiplexingMode.NonMultiplexing, CommandBehavior.SequentialAccess)]
    [TestFixture(MultiplexingMode.Multiplexing, CommandBehavior.SequentialAccess)]
    public class ReaderTests : MultiplexingTestBase
    {
        [Test]
        public async Task Seek_columns()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT 1,2,3", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));
            if (IsSequential)
                Assert.That(() => reader.GetInt32(0), Throws.Exception.TypeOf<InvalidOperationException>());
            else
                Assert.That(reader.GetInt32(0), Is.EqualTo(1));
            Assert.That(reader.GetInt32(1), Is.EqualTo(2));
            if (IsSequential)
                Assert.That(() => reader.GetInt32(0), Throws.Exception.TypeOf<InvalidOperationException>());
            else
                Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        }

        [Test]
        public async Task No_resultset()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "id INT", out var table);

            using (var cmd = new OpenGaussCommand($"INSERT INTO {table} VALUES (8)", conn))
            using (var reader = await cmd.ExecuteReaderAsync(Behavior))
            {
                Assert.That(() => reader.GetOrdinal("foo"), Throws.Exception.TypeOf<InvalidOperationException>());
                Assert.That(reader.Read(), Is.False);
                Assert.That(() => reader.GetOrdinal("foo"), Throws.Exception.TypeOf<InvalidOperationException>());
                Assert.That(reader.FieldCount, Is.EqualTo(0));
                Assert.That(reader.NextResult(), Is.False);
                Assert.That(() => reader.GetOrdinal("foo"), Throws.Exception.TypeOf<InvalidOperationException>());
            }

            using (var cmd = new OpenGaussCommand($"SELECT 1; INSERT INTO {table} VALUES (8)", conn))
            using (var reader = await cmd.ExecuteReaderAsync(Behavior))
            {
                await reader.NextResultAsync();
                Assert.That(() => reader.GetOrdinal("foo"), Throws.Exception.TypeOf<InvalidOperationException>());
                Assert.That(reader.Read(), Is.False);
                Assert.That(() => reader.GetOrdinal("foo"), Throws.Exception.TypeOf<InvalidOperationException>());
                Assert.That(reader.FieldCount, Is.EqualTo(0));
            }
        }

        [Test]
        public async Task Empty_resultset()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT 1 AS foo WHERE FALSE", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            Assert.That(reader.Read(), Is.False);
            Assert.That(reader.FieldCount, Is.EqualTo(1));
            Assert.That(reader.GetOrdinal("foo"), Is.EqualTo(0));
            Assert.That(() => reader[0], Throws.Exception.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task FieldCount()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "int INT", out var table);

            using var cmd = new OpenGaussCommand("SELECT 1; SELECT 2,3", conn);
            using (var reader = await cmd.ExecuteReaderAsync(Behavior))
            {
                Assert.That(reader.FieldCount, Is.EqualTo(1));
                Assert.That(reader.Read(), Is.True);
                Assert.That(reader.FieldCount, Is.EqualTo(1));
                Assert.That(reader.Read(), Is.False);
                Assert.That(reader.FieldCount, Is.EqualTo(1));
                Assert.That(reader.NextResult(), Is.True);
                Assert.That(reader.FieldCount, Is.EqualTo(2));
                Assert.That(reader.NextResult(), Is.False);
                Assert.That(reader.FieldCount, Is.EqualTo(0));
            }

            cmd.CommandText = $"INSERT INTO {table} (int) VALUES (1)";
            using (var reader = await cmd.ExecuteReaderAsync(Behavior))
            {
                // Note MSDN docs that seem to say we should case -1 in this case: https://msdn.microsoft.com/en-us/library/system.data.idatarecord.fieldcount(v=vs.110).aspx
                // But SqlClient returns 0
                Assert.That(() => reader.FieldCount, Is.EqualTo(0));

            }
        }

        [Test]
        public async Task RecordsAffected()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "int INT", out var table);

            var sb = new StringBuilder();
            for (var i = 0; i < 10; i++)
                sb.Append($"INSERT INTO {table} (int) VALUES ({i});");
            sb.Append("SELECT 1;"); // Testing, that on close reader consumes all rows (as insert doesn't have a result set, but select does)
            for (var i = 10; i < 15; i++)
                sb.Append($"INSERT INTO {table} (int) VALUES ({i});");
            var cmd = new OpenGaussCommand(sb.ToString(), conn);
            var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Close();
            Assert.That(reader.RecordsAffected, Is.EqualTo(15));

            cmd = new OpenGaussCommand($"SELECT * FROM {table}", conn);
            reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Close();
            Assert.That(reader.RecordsAffected, Is.EqualTo(-1));

            cmd = new OpenGaussCommand($"UPDATE {table} SET int=int+1 WHERE int > 10", conn);
            reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Close();
            Assert.That(reader.RecordsAffected, Is.EqualTo(4));

            cmd = new OpenGaussCommand($"UPDATE {table} SET int=8 WHERE int=666", conn);
            reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Close();
            Assert.That(reader.RecordsAffected, Is.EqualTo(0));

            cmd = new OpenGaussCommand($"DELETE FROM {table} WHERE int > 10", conn);
            reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Close();
            Assert.That(reader.RecordsAffected, Is.EqualTo(4));
        }

#pragma warning disable CS0618
        //[Test]
        public async Task StatementOID_legacy_batching()
        {
            using var conn = await OpenConnectionAsync();

            MaximumPgVersionExclusive(conn, "12.0",
"Support for 'CREATE TABLE ... WITH OIDS' has been removed in 12.0. See https://www.postgresql.org/docs/12/release-12.html#id-1.11.6.5.4");

            await using var _ = await GetTempTableName(conn, out var table);

            var query = $@"
CREATE TABLE {table} (name TEXT) WITH OIDS;
INSERT INTO {table} (name) VALUES ('a');
UPDATE {table} SET name='b' WHERE name='doesnt_exist';";

            using (var cmd = new OpenGaussCommand(query, conn))
            {
                using var reader = await cmd.ExecuteReaderAsync(Behavior);

                Assert.That(reader.Statements[0].OID, Is.EqualTo(0));
                Assert.That(reader.Statements[1].OID, Is.Not.EqualTo(0));
                Assert.That(reader.Statements[0].OID, Is.EqualTo(0));
            }

            using (var cmd = new OpenGaussCommand($"SELECT name FROM {table}; DELETE FROM {table}", conn))
            {
                using var reader = await cmd.ExecuteReaderAsync(Behavior);

                await reader.NextResultAsync(); // Consume SELECT result set
                Assert.That(reader.Statements[0].OID, Is.EqualTo(0));
                Assert.That(reader.Statements[1].OID, Is.EqualTo(0));
            }
        }
#pragma warning restore CS0618

        [Test]
        public async Task Get_string_with_parameter()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "name TEXT", out var table);
            const string text = "Random text";
            await conn.ExecuteNonQueryAsync($@"INSERT INTO {table} (name) VALUES ('{text}')");

            var command = new OpenGaussCommand($"SELECT name FROM {table} WHERE name = :value;", conn);
            var param = new OpenGaussParameter
            {
                ParameterName = "value",
                DbType = DbType.String,
                Size = text.Length,
                Value = text
            };
            //param.OpenGaussDbType = OpenGaussDbType.Text;
            command.Parameters.Add(param);

            using var dr = await command.ExecuteReaderAsync(Behavior);
            dr.Read();
            var result = dr.GetString(0);
            Assert.That(result, Is.EqualTo(text));
        }

        [Test]
        public async Task Get_string_with_quote_with_parameter()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTableName(conn, out var table);
            await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (name TEXT);
INSERT INTO {table} (name) VALUES ('Text with '' single quote');");

            const string test = "Text with ' single quote";
            var command = new OpenGaussCommand($"SELECT name FROM {table} WHERE name = :value;", conn);

            var param = new OpenGaussParameter();
            param.ParameterName = "value";
            param.DbType = DbType.String;
            //param.OpenGaussDbType = OpenGaussDbType.Text;
            param.Size = test.Length;
            param.Value = test;
            command.Parameters.Add(param);

            using var dr = await command.ExecuteReaderAsync(Behavior);
            dr.Read();
            var result = dr.GetString(0);
            Assert.That(result, Is.EqualTo(test));
        }

        [Test]
        public async Task Get_value_by_name()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand(@"SELECT 'Random text' AS real_column", conn);
            using var dr = await command.ExecuteReaderAsync(Behavior);
            dr.Read();
            Assert.That(dr["real_column"], Is.EqualTo("Random text"));
            Assert.That(() => dr["non_existing"], Throws.Exception.TypeOf<IndexOutOfRangeException>());
        }

        [Test]
        [IssueLink("https://github.com/opengauss/opengauss/issues/794")]
        public async Task GetFieldType()
        {
            using var conn = await OpenConnectionAsync();
            using (var cmd = new OpenGaussCommand(@"SELECT 1::INT4 AS some_column", conn))
            using (var reader = await cmd.ExecuteReaderAsync(Behavior))
            {
                reader.Read();
                Assert.That(reader.GetFieldType(0), Is.SameAs(typeof(int)));
            }
            using (var cmd = new OpenGaussCommand(@"SELECT 1::INT4 AS some_column", conn))
            {
                cmd.AllResultTypesAreUnknown = true;
                using (var reader = await cmd.ExecuteReaderAsync(Behavior))
                {
                    reader.Read();
                    Assert.That(reader.GetFieldType(0), Is.SameAs(typeof(string)));
                }
            }
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1096")]
        public async Task GetFieldType_SchemaOnly()
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = new OpenGaussCommand(@"SELECT 1::INT4 AS some_column", conn);
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly);
            reader.Read();
            Assert.That(reader.GetFieldType(0), Is.SameAs(typeof(int)));
        }

        [Test]
        public async Task GetPostgresType()
        {
            if (IsMultiplexing)
                Assert.Ignore("Multiplexing: Fails");

            using var conn = await OpenConnectionAsync();
            PostgresType intType;
            using (var cmd = new OpenGaussCommand(@"SELECT 1::INTEGER AS some_column", conn))
            using (var reader = await cmd.ExecuteReaderAsync(Behavior))
            {
                reader.Read();
                intType = (PostgresBaseType)reader.GetPostgresType(0);
                Assert.That(intType.Namespace, Is.EqualTo("pg_catalog"));
                Assert.That(intType.Name, Is.EqualTo("integer"));
                Assert.That(intType.FullName, Is.EqualTo("pg_catalog.integer"));
                Assert.That(intType.DisplayName, Is.EqualTo("integer"));
                Assert.That(intType.InternalName, Is.EqualTo("int4"));
            }

            using (var cmd = new OpenGaussCommand(@"SELECT '{1}'::INTEGER[] AS some_column", conn))
            using (var reader = await cmd.ExecuteReaderAsync(Behavior))
            {
                reader.Read();
                var intArrayType = (PostgresArrayType)reader.GetPostgresType(0);
                Assert.That(intArrayType.Name, Is.EqualTo("integer[]"));
                Assert.That(intArrayType.Element, Is.SameAs(intType));
                Assert.That(intArrayType.DisplayName, Is.EqualTo("integer[]"));
                Assert.That(intArrayType.InternalName, Is.EqualTo("_int4"));
                Assert.That(intType.Array, Is.SameAs(intArrayType));
            }
        }

        /// <seealso cref="ReaderNewSchemaTests.DataTypeName"/>
        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/787")]
        [TestCase("integer")]
        [TestCase("real")]
        [TestCase("integer[]")]
        [TestCase("character varying(10)")]
        [TestCase("character varying")]
        [TestCase("character varying(10)[]")]
        [TestCase("character(10)")]
        [TestCase("character", "character(1)")]
        [TestCase("numeric(1000, 2)")]
        [TestCase("numeric(1000)")]
        [TestCase("numeric")]
        [TestCase("timestamp without time zone")]
        [TestCase("timestamp(2) without time zone")]
        [TestCase("timestamp(2) with time zone")]
        [TestCase("time without time zone")]
        [TestCase("time(2) without time zone")]
        [TestCase("time(2) with time zone")]
        [TestCase("interval")]
        [TestCase("interval(2)")]
        [TestCase("bit", "bit(1)")]
        [TestCase("bit(3)")]
        [TestCase("bit varying")]
        [TestCase("bit varying(3)")]
        public async Task GetDataTypeName(string typeName, string? normalizedName = null)
        {
            if (normalizedName == null)
                normalizedName = typeName;
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand($"SELECT NULL::{typeName} AS some_column", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
            Assert.That(reader.GetDataTypeName(0), Is.EqualTo(normalizedName));
        }

        [Test]
        public async Task GetDataTypeName_enum()
        {
            if (IsMultiplexing)
                Assert.Ignore("Multiplexing: ReloadTypes");

            using var conn = await OpenConnectionAsync();
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.my_enum AS ENUM ('one')");
            conn.ReloadTypes();
            using var cmd = new OpenGaussCommand("SELECT 'one'::my_enum", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
            Assert.That(reader.GetDataTypeName(0), Does.StartWith("pg_temp").And.EndWith(".my_enum"));
        }

        [Test]
        public async Task GetDataTypeName_domain()
        {
            if (IsMultiplexing)
                Assert.Ignore("Multiplexing: ReloadTypes");

            using var conn = await OpenConnectionAsync();
            conn.ExecuteNonQuery("CREATE DOMAIN pg_temp.my_domain AS VARCHAR(10)");
            conn.ReloadTypes();
            using var cmd = new OpenGaussCommand("SELECT 'one'::my_domain", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
            // In the RowDescription, PostgreSQL sends the type OID of the underlying type and not of the domain.
            Assert.That(reader.GetDataTypeName(0), Is.EqualTo("character varying(10)"));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/794")]
        public async Task GetDataTypeNameTypes_unknown()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand(@"SELECT 1::INTEGER AS some_column", conn);
            cmd.AllResultTypesAreUnknown = true;
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
            Assert.That(reader.GetDataTypeName(0), Is.EqualTo("integer"));
        }

        [Test]
        [IssueLink("https://github.com/opengauss/opengauss/issues/791")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/794")]
        public async Task GetDataTypeOID()
        {
            using var conn = await OpenConnectionAsync();
            var int4OID = await conn.ExecuteScalarAsync("SELECT oid FROM pg_type WHERE typname = 'int4'");
            using (var cmd = new OpenGaussCommand(@"SELECT 1::INT4 AS some_column", conn))
            using (var reader = await cmd.ExecuteReaderAsync(Behavior))
            {
                reader.Read();
                Assert.That(reader.GetDataTypeOID(0), Is.EqualTo(int4OID));
            }
            using (var cmd = new OpenGaussCommand(@"SELECT 1::INT4 AS some_column", conn))
            {
                cmd.AllResultTypesAreUnknown = true;
                using (var reader = await cmd.ExecuteReaderAsync(Behavior))
                {
                    reader.Read();
                    Assert.That(reader.GetDataTypeOID(0), Is.EqualTo(int4OID));
                }
            }
        }

        [Test]
        public async Task GetName()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand(@"SELECT 1 AS some_column", conn);
            using var dr = await command.ExecuteReaderAsync(Behavior);
            dr.Read();
            Assert.That(dr.GetName(0), Is.EqualTo("some_column"));
        }

        [Test]
        public async Task GetFieldValue_as_object()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT 'foo'::TEXT", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
            Assert.That(reader.GetFieldValue<object>(0), Is.EqualTo("foo"));
        }

        [Test]
        public async Task GetValues()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand(@"SELECT 'hello', 1, '2014-01-01'::DATE", conn);
            using (var dr = await command.ExecuteReaderAsync(Behavior))
            {
                dr.Read();
                var values = new object[4];
                Assert.That(dr.GetValues(values), Is.EqualTo(3));
                Assert.That(values, Is.EqualTo(new object?[] { "hello", 1, new DateTime(2014, 1, 1), null }));
            }
            using (var dr = await command.ExecuteReaderAsync(Behavior))
            {
                dr.Read();
                var values = new object[2];
                Assert.That(dr.GetValues(values), Is.EqualTo(2));
                Assert.That(values, Is.EqualTo(new object[] { "hello", 1 }));
            }
        }

#pragma warning disable 618 // OpenGaussDate is obsolete, remove in 7.0
        [Test]
        public async Task GetProviderSpecificValues()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand(@"SELECT 'hello', 1, '2014-01-01'::DATE", conn);
            using (var dr = await command.ExecuteReaderAsync(Behavior))
            {
                dr.Read();
                var values = new object[4];
                Assert.That(dr.GetProviderSpecificValues(values), Is.EqualTo(3));
                Assert.That(values, Is.EqualTo(new object?[] { "hello", 1, new OpenGaussDateTime(2014, 1, 1, 0, 0, 0), null }));
            }
            using (var dr = await command.ExecuteReaderAsync(Behavior))
            {
                dr.Read();
                var values = new object[2];
                Assert.That(dr.GetProviderSpecificValues(values), Is.EqualTo(2));
                Assert.That(values, Is.EqualTo(new object[] { "hello", 1 }));
            }
        }
#pragma warning restore 618

        [Test]
        public async Task ExecuteReader_getting_empty_resultset_with_output_parameter()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "name TEXT", out var table);
            var command = new OpenGaussCommand($"SELECT * FROM {table} WHERE name = NULL;", conn);
            var param = new OpenGaussParameter("some_param", OpenGaussDbType.Varchar);
            param.Direction = ParameterDirection.Output;
            command.Parameters.Add(param);
            using var dr = await command.ExecuteReaderAsync(Behavior);
            Assert.That(dr.NextResult(), Is.False);
        }

        [Test]
        public async Task Get_value_from_empty_resultset()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "name TEXT", out var table);
            using var command = new OpenGaussCommand($"SELECT * FROM {table} WHERE name = :value;", conn);
            const string test = "Text single quote";
            var param = new OpenGaussParameter();
            param.ParameterName = "value";
            param.DbType = DbType.String;
            //param.OpenGaussDbType = OpenGaussDbType.Text;
            param.Size = test.Length;
            param.Value = test;
            command.Parameters.Add(param);

            using var dr = await command.ExecuteReaderAsync(Behavior);
            dr.Read();
            // This line should throw the invalid operation exception as the datareader will
            // have an empty resultset.
            Assert.That(() => Console.WriteLine(dr.IsDBNull(1)),
                Throws.Exception.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task Read_past_reader_end()
        {
            using var conn = await OpenConnectionAsync();
            var command = new OpenGaussCommand("SELECT 1", conn);
            using var dr = await command.ExecuteReaderAsync(Behavior);
            while (dr.Read()) { }
            Assert.That(() => dr[0], Throws.Exception.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task Reader_dispose_state_does_not_leak()
        {
            if (IsMultiplexing || Behavior != CommandBehavior.Default)
                return;

            var startReaderClosedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var continueReaderClosedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var _ = CreateTempPool(ConnectionString, out var connectionString);
            await using var conn1 = await OpenConnectionAsync(connectionString);
            var connID = conn1.Connector!.Id;
            var readerCloseTask = Task.Run(async () =>
            {
                using var cmd = conn1.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
                reader.ReaderClosed += (s, e) =>
                {
                    startReaderClosedTcs.SetResult();
                    continueReaderClosedTcs.Task.GetAwaiter().GetResult();
                };
            });

            await startReaderClosedTcs.Task;
            await using var conn2 = await OpenConnectionAsync(connectionString);
            Assert.That(conn2.Connector!.Id, Is.EqualTo(connID));
            using var cmd = conn2.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.That(reader.State, Is.EqualTo(ReaderState.BeforeResult));
            continueReaderClosedTcs.SetResult();
            await readerCloseTask;
            Assert.That(reader.State, Is.EqualTo(ReaderState.BeforeResult));
        }

        [Test]
        public async Task SingleResult()
        {
            await using var conn = await OpenConnectionAsync();
            await using var command = new OpenGaussCommand(@"SELECT 1; SELECT 2", conn);
            var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | Behavior);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));
            Assert.That(reader.NextResult(), Is.False);
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/400")]
        public async Task Exception_thrown_from_ExecuteReaderAsync([Values(PrepareOrNot.Prepared, PrepareOrNot.NotPrepared)] PrepareOrNot prepare)
        {
            if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            await using var _ = GetTempFunctionName(conn, out var function);

            await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql';
                ");

            using var cmd = new OpenGaussCommand($"SELECT {function}()", conn);
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();
            Assert.That(async () => await cmd.ExecuteReaderAsync(Behavior), Throws.Exception.TypeOf<PostgresException>());
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1032")]
        public async Task Exception_thrown_from_NextResult([Values(PrepareOrNot.Prepared, PrepareOrNot.NotPrepared)] PrepareOrNot prepare)
        {
            if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            await using var _ = GetTempFunctionName(conn, out var function);

            await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql';
                ");

            using var cmd = new OpenGaussCommand($"SELECT 1; SELECT {function}()", conn);
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            Assert.That(() => reader.NextResult(), Throws.Exception.TypeOf<PostgresException>());
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/967")]
        public async Task OpenGaussException_references_BatchCommand_with_single_command()
        {
            await using var conn = await OpenConnectionAsync();
            await using var _ = GetTempFunctionName(conn, out var function);

            await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql'");

            // We use OpenGaussConnection.CreateCommand to test that the command isn't recycled when referenced in an exception
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {function}()";

            try
            {
                await cmd.ExecuteReaderAsync(Behavior);
                Assert.Fail();
            }
            catch (PostgresException e)
            {
                Assert.That(e.BatchCommand, Is.SameAs(cmd.InternalBatchCommands[0]));
            }

            // Make sure the command isn't recycled by the connection when it's disposed - this is important since internal command
            // resources are referenced by the exception above, which is very likely to escape the using statement of the command.
            cmd.Dispose();
            var cmd2 = conn.CreateCommand();
            Assert.That(cmd2, Is.Not.SameAs(cmd));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/967")]
        public async Task OpenGaussException_references_BatchCommand_with_multiple_commands()
        {
            await using var conn = await OpenConnectionAsync();
            await using var _ = GetTempFunctionName(conn, out var function);

            await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql'");

            // We use OpenGaussConnection.CreateCommand to test that the command isn't recycled when referenced in an exception
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1; {function}()";

            await using (var reader = await cmd.ExecuteReaderAsync(Behavior))
            {
                try
                {
                    await reader.NextResultAsync();
                    Assert.Fail();
                }
                catch (PostgresException e)
                {
                    Assert.That(e.BatchCommand, Is.SameAs(cmd.InternalBatchCommands[1]));
                }
            }

            // Make sure the command isn't recycled by the connection when it's disposed - this is important since internal command
            // resources are referenced by the exception above, which is very likely to escape the using statement of the command.
            cmd.Dispose();
            var cmd2 = conn.CreateCommand();
            Assert.That(cmd2, Is.Not.SameAs(cmd));
        }

        #region SchemaOnly

        [Test]
        public async Task SchemaOnly_returns_no_data()
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = new OpenGaussCommand("SELECT 1", conn);
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly);
            Assert.That(reader.Read(), Is.False);
        }

        [Test]
        public async Task SchemaOnly_support_function_call()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = GetTempFunctionName(conn, out var function);

            await conn.ExecuteNonQueryAsync($"CREATE OR REPLACE FUNCTION {function}() RETURNS SETOF integer as 'SELECT 1;' LANGUAGE 'sql';");
            var command = new OpenGaussCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            using var dr = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly);
            var i = 0;
            while (dr.Read())
                i++;
            Assert.That(i, Is.EqualTo(0));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/2827")]
        public async Task SchemaOnly_next_result_beyond_end()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "id INT", out var table);

            using var cmd = new OpenGaussCommand($"SELECT * FROM {table}", conn);
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly);
            Assert.That(reader.NextResult(), Is.False);
            Assert.That(reader.NextResult(), Is.False);
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/4124")]
        public async Task SchemaOnly_GetDataTypeName_with_unsupported_type()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand(@"select aggfnoid from pg_aggregate", conn);
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly);

            Assert.That(reader.GetDataTypeName(0), Is.EqualTo("regproc"));
        }

        #endregion SchemaOnly

        #region GetOrdinal

        [Test]
        public async Task GetOrdinal()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand(@"SELECT 0, 1 AS some_column WHERE 1=0", conn);
            using var reader = await command.ExecuteReaderAsync(Behavior);
            Assert.That(reader.GetOrdinal("some_column"), Is.EqualTo(1));
            Assert.That(() => reader.GetOrdinal("doesn't_exist"), Throws.Exception.TypeOf<IndexOutOfRangeException>());
        }

        [Test]
        public async Task GetOrdinal_case_insensitive()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand("select 123 as FIELD1", conn);
            using var reader = await command.ExecuteReaderAsync(Behavior);
            reader.Read();
            Assert.That(reader.GetOrdinal("fieLd1"), Is.EqualTo(0));
        }

        [Test]
        public async Task GetOrdinal_kana_insensitive()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand("select 123 as ｦｧｨｩｪｫｬ", conn);
            using var reader = await command.ExecuteReaderAsync(Behavior);
            reader.Read();
            Assert.That(reader["ヲァィゥェォャ"], Is.EqualTo(123));
        }

        #endregion GetOrdinal

        [Test]
        public async Task Field_index_does_not_exist()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand("SELECT 1", conn);
            using var dr = await command.ExecuteReaderAsync(Behavior);
            dr.Read();
            Assert.That(() => dr[5], Throws.Exception.TypeOf<IndexOutOfRangeException>());
        }

        [Test, Description("Performs some operations while a reader is still open and checks for exceptions")]
        public async Task Reader_is_still_open()
        {
            await using var conn = await OpenConnectionAsync();
            // We might get the connection, on which the second command was already prepared, so prepare wouldn't start the UserAction
            if (!IsMultiplexing)
                conn.UnprepareAll();
            using var cmd1 = new OpenGaussCommand("SELECT 1", conn);
            await using var reader1 = await cmd1.ExecuteReaderAsync(Behavior);
            Assert.That(() => conn.ExecuteNonQuery("SELECT 1"), Throws.Exception.TypeOf<OpenGaussOperationInProgressException>());
            Assert.That(async () => await conn.ExecuteScalarAsync("SELECT 1"), Throws.Exception.TypeOf<OpenGaussOperationInProgressException>());

            using var cmd2 = new OpenGaussCommand("SELECT 2", conn);
            Assert.That(() => cmd2.ExecuteReader(Behavior), Throws.Exception.TypeOf<OpenGaussOperationInProgressException>());
            if (!IsMultiplexing)
                Assert.That(() => cmd2.Prepare(), Throws.Exception.TypeOf<OpenGaussOperationInProgressException>());
        }

        [Test]
        public async Task Cleans_up_ok_with_dispose_calls([Values(PrepareOrNot.Prepared, PrepareOrNot.NotPrepared)] PrepareOrNot prepare)
        {
            if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand("SELECT 1", conn);
            using var dr = await command.ExecuteReaderAsync(Behavior);
            dr.Read();
            dr.Close();

            using var upd = conn.CreateCommand();
            upd.CommandText = "SELECT 1";
            if (prepare == PrepareOrNot.Prepared)
                upd.Prepare();
        }

        [Test]
        public async Task Null()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p1, @p2::TEXT", conn);
            cmd.Parameters.Add(new OpenGaussParameter("p1", DbType.String) { Value = DBNull.Value });
            cmd.Parameters.Add(new OpenGaussParameter { ParameterName = "p2", Value = DBNull.Value });

            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();

            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                Assert.That(reader.IsDBNull(i), Is.True);
                Assert.That(reader.IsDBNullAsync(i).Result, Is.True);
                Assert.That(reader.GetValue(i), Is.EqualTo(DBNull.Value));
                Assert.That(reader.GetFieldValue<object>(i), Is.EqualTo(DBNull.Value));
                Assert.That(reader.GetProviderSpecificValue(i), Is.EqualTo(DBNull.Value));
                Assert.That(() => reader.GetString(i), Throws.Exception.TypeOf<InvalidCastException>());
            }
        }

        [Test]
        [IssueLink("https://github.com/opengauss/opengauss/issues/742")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/800")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/1234")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/1898")]
        public async Task HasRows([Values(PrepareOrNot.NotPrepared, PrepareOrNot.Prepared)] PrepareOrNot prepare)
        {
            if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "name TEXT", out var table);

            var command = new OpenGaussCommand($"SELECT 1; SELECT * FROM {table} WHERE name='does_not_exist'", conn);
            if (prepare == PrepareOrNot.Prepared)
                command.Prepare();
            using (var reader = await command.ExecuteReaderAsync(Behavior))
            {
                Assert.That(reader.HasRows, Is.True);
                Assert.That(reader.HasRows, Is.True);
                Assert.That(reader.Read(), Is.True);
                Assert.That(reader.HasRows, Is.True);
                Assert.That(reader.Read(), Is.False);
                Assert.That(reader.HasRows, Is.True);
                await reader.NextResultAsync();
                Assert.That(reader.HasRows, Is.False);
            }

            command.CommandText = $"SELECT * FROM {table}";
            if (prepare == PrepareOrNot.Prepared)
                command.Prepare();
            using (var reader = await command.ExecuteReaderAsync(Behavior))
            {
                reader.Read();
                Assert.That(reader.HasRows, Is.False);
            }

            command.CommandText = "SELECT 1";
            if (prepare == PrepareOrNot.Prepared)
                command.Prepare();
            using (var reader = await command.ExecuteReaderAsync(Behavior))
            {
                reader.Read();
                reader.Close();
                Assert.That(() => reader.HasRows, Throws.Exception.TypeOf<InvalidOperationException>());
            }

            command.CommandText = $"INSERT INTO {table} (name) VALUES ('foo'); SELECT * FROM {table}";
            if (prepare == PrepareOrNot.Prepared)
                command.Prepare();
            using (var reader = await command.ExecuteReaderAsync())
            {
                Assert.That(reader.HasRows, Is.True);
                reader.Read();
                Assert.That(reader.GetString(0), Is.EqualTo("foo"));
            }

            Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));
        }

        [Test]
        public async Task HasRows_without_resultset()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "name TEXT", out var table);
            using var command = new OpenGaussCommand($"DELETE FROM {table} WHERE name = 'unknown'", conn);
            using var reader = await command.ExecuteReaderAsync(Behavior);
            Assert.That(reader.HasRows, Is.False);
        }

        [Test]
        public async Task Interval_as_TimeSpan()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand("SELECT CAST('1 hour' AS interval) AS dauer", conn);
            using var dr = await command.ExecuteReaderAsync(Behavior);
            Assert.That(dr.HasRows, Is.True);
            Assert.That(dr.Read(), Is.True);
            Assert.That(dr.HasRows, Is.True);
            var ts = dr.GetTimeSpan(0);
        }

        [Test]
        public async Task Close_connection_in_middle_of_row()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT 1, 2", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/pull/1266")]
        [Description("NextResult was throwing an ArgumentOutOfRangeException when trying to determine the statement to associate with the PostgresException")]
        public async Task Reader_next_result_exception_handling()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTableName(conn, out var table1);
            await using var __ = await GetTempTableName(conn, out var table2);
            await using var ___ = GetTempFunctionName(conn, out var function);

            await conn.ExecuteNonQueryAsync($"DROP FUNCTION IF EXISTS {function}");

            var initializeTablesSql = $@"
CREATE TABLE {table1} (value int NOT NULL);
CREATE TABLE {table2} (value int UNIQUE);
ALTER TABLE ONLY {table1} ADD CONSTRAINT fkey FOREIGN KEY (value) REFERENCES {table2}(value) DEFERRABLE INITIALLY DEFERRED;
CREATE FUNCTION {function}(_value int) RETURNS int AS $BODY$
BEGIN
    INSERT INTO {table1}(value) VALUES(_value);
    RETURN _value;
END;
$BODY$
LANGUAGE plpgsql VOLATILE";

            await conn.ExecuteNonQueryAsync(initializeTablesSql);
            using var cmd = new OpenGaussCommand($"SELECT {function}(1)", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);


            Assert.That(() => reader.NextResult(),
                Throws.Exception.TypeOf<PostgresException>()
                    .With.Property(nameof(PostgresException.SqlState)).EqualTo("23503"));
        }

#pragma warning disable 618 // OpenGaussDate is obsolete, remove in 7.0
        [Test]
        public async Task Invalid_cast()
        {
            using var conn = await OpenConnectionAsync();
            // Chunking type handler
            using (var cmd = new OpenGaussCommand("SELECT 'foo'", conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                reader.Read();
                Assert.That(() => reader.GetInt32(0), Throws.Exception.TypeOf<InvalidCastException>());
            }
            // Simple type handler
            using (var cmd = new OpenGaussCommand("SELECT 1", conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                reader.Read();
                Assert.That(() => reader.GetDate(0), Throws.Exception.TypeOf<InvalidCastException>());
            }
            Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));
        }
#pragma warning restore 618

        [Test, Description("Reads a lot of rows to make sure the long unoptimized path for Read() works")]
        public async Task Many_reads()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand($"SELECT generate_series(1, {conn.Settings.ReadBufferSize})", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            for (var i = 1; i <= conn.Settings.ReadBufferSize; i++)
            {
                Assert.That(reader.Read(), Is.True);
                Assert.That(reader.GetInt32(0), Is.EqualTo(i));
            }
            Assert.That(reader.Read(), Is.False);
        }

        [Test]
        public async Task Nullable_scalar()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p1, @p2", conn);
            var p1 = new OpenGaussParameter { ParameterName = "p1", Value = DBNull.Value, OpenGaussDbType = OpenGaussDbType.Smallint };
            var p2 = new OpenGaussParameter { ParameterName = "p2", Value = (short)8 };
            Assert.That(p2.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Smallint));
            Assert.That(p2.DbType, Is.EqualTo(DbType.Int16));
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();

            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                Assert.That(reader.GetFieldType(i), Is.EqualTo(typeof(short)));
                Assert.That(reader.GetDataTypeName(i), Is.EqualTo("smallint"));
            }

            Assert.That(() => reader.GetFieldValue<object>(0), Is.EqualTo(DBNull.Value));
            Assert.That(() => reader.GetFieldValue<int>(0), Throws.TypeOf<InvalidCastException>());
            Assert.That(() => reader.GetFieldValue<int?>(0), Throws.Nothing);
            Assert.That(reader.GetFieldValue<int?>(0), Is.Null);

            Assert.That(() => reader.GetFieldValue<object>(1), Throws.Nothing);
            Assert.That(() => reader.GetFieldValue<int>(1), Throws.Nothing);
            Assert.That(() => reader.GetFieldValue<int?>(1), Throws.Nothing);
            Assert.That(reader.GetFieldValue<object>(1), Is.EqualTo(8));
            Assert.That(reader.GetFieldValue<int>(1), Is.EqualTo(8));
            Assert.That(reader.GetFieldValue<int?>(1), Is.EqualTo(8));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/2913")]
        public async Task Bug2913_reading_previous_query_messages()
        {
            // No point in testing for multiplexing, as every query may use another connection
            if (IsMultiplexing)
                return;

            var firstMrs = new ManualResetEventSlim(false);
            var secondMrs = new ManualResetEventSlim(false);

            var secondQuery = Task.Run(async () =>
            {
                firstMrs.Wait();
                await using var secondConn = await OpenConnectionAsync();
                using var secondCmd = new OpenGaussCommand(@"SELECT 1; SELECT 2;", secondConn);
                await using var secondReader = await secondCmd.ExecuteReaderAsync(Behavior | CommandBehavior.CloseConnection);

                // Check, that StatementIndex is equals to default value
                Assert.That(secondReader.StatementIndex, Is.EqualTo(0));
                secondMrs.Wait();
                // Check, that the first query didn't change StatementIndex
                Assert.That(secondReader.StatementIndex, Is.EqualTo(0));
            });

            await using (var firstConn = await OpenConnectionAsync())
            {
                // Executing a query, which fails with OpenGaussException on reader disposing, as NotExistingTable doesn't exist
                using var firstCmd = new OpenGaussCommand(@"SELECT 1; SELECT * FROM NotExistingTable;", firstConn);
                await using var firstReader = await firstCmd.ExecuteReaderAsync(Behavior | CommandBehavior.CloseConnection);

                Assert.That(firstReader.StatementIndex, Is.EqualTo(0));

                firstReader.ReaderClosed += (s, e) =>
                {
                    // Starting a second query, which in case of a bug uses firstConn
                    firstMrs.Set();
                    // Waiting for the second query to start executing
                    Thread.Sleep(100);
                    // After waiting, reader is free to reset prepared statements, which also increments StatementIndex
                };

                Assert.ThrowsAsync<PostgresException>(firstReader.NextResultAsync);

                secondMrs.Set();
            }

            await secondQuery;

            // If we're here and a bug is still not fixed, we fail while executing reader, as we're reading skipped messages for the second query
            await using var thirdConn = OpenConnection();
            using var thirdCmd = new OpenGaussCommand(@"SELECT 1; SELECT 2;", thirdConn);
            await using var thirdReader = await thirdCmd.ExecuteReaderAsync(Behavior | CommandBehavior.CloseConnection);
        }

        [Test]
        [IssueLink("https://github.com/opengauss/opengauss/issues/2913")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/3289")]
        public async Task Reader_close_and_dispose()
        {
            await using var conn = await OpenConnectionAsync();
            using var cmd1 = conn.CreateCommand();
            cmd1.CommandText = "SELECT 1";

            var reader1 = await cmd1.ExecuteReaderAsync(Behavior | CommandBehavior.CloseConnection);
            await reader1.CloseAsync();

            await conn.OpenAsync();
            cmd1.Connection = conn;
            var reader2 = await cmd1.ExecuteReaderAsync(Behavior | CommandBehavior.CloseConnection);
            Assert.That(reader1, Is.Not.SameAs(reader2));
            Assert.That(reader2.State, Is.EqualTo(ReaderState.BeforeResult));

            await reader1.DisposeAsync();

            Assert.That(reader2.State, Is.EqualTo(ReaderState.BeforeResult));
        }

        [Test]
        [IssueLink("https://github.com/opengauss/opengauss/issues/2964")]
        public async Task Bug2964_connection_close_and_reader_dispose()
        {
            await using var conn = await OpenConnectionAsync();
            using var cmd1 = conn.CreateCommand();
            cmd1.CommandText = "SELECT 1";

            var reader1 = await cmd1.ExecuteReaderAsync(Behavior);
            await conn.CloseAsync();
            await conn.OpenAsync();

            var reader2 = await cmd1.ExecuteReaderAsync(Behavior);
            Assert.That(reader1, Is.Not.SameAs(reader2));
            Assert.That(reader2.State, Is.EqualTo(ReaderState.BeforeResult));

            await reader1.DisposeAsync();

            Assert.That(reader2.State, Is.EqualTo(ReaderState.BeforeResult));
        }

        [Test]
        public async Task Reader_reuse_on_dispose()
        {
            await using var conn = await OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";

            var reader1 = await cmd.ExecuteReaderAsync(Behavior);
            await reader1.ReadAsync();
            await reader1.DisposeAsync();

            var reader2 = await cmd.ExecuteReaderAsync(Behavior);
            Assert.That(reader1, Is.SameAs(reader2));
            await reader2.DisposeAsync();
        }

        [Test]
        public async Task Unbound_reader_reuse()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                MinPoolSize = 1,
                MaxPoolSize = 1,
            };
            using var _ = CreateTempPool(csb.ToString(), out var connectionString);

            await using var conn1 = await OpenConnectionAsync(connectionString);
            using var cmd1 = conn1.CreateCommand();
            cmd1.CommandText = "SELECT 1";
            var reader1 = await cmd1.ExecuteReaderAsync(Behavior);
            await using (var __ = reader1)
            {
                Assert.That(async () => await reader1.ReadAsync(), Is.EqualTo(true));
                Assert.That(() => reader1.GetInt32(0), Is.EqualTo(1));

                await reader1.CloseAsync();
                await conn1.CloseAsync();
            }

            await using var conn2 = await OpenConnectionAsync(connectionString);
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT 2";
            var reader2 = await cmd2.ExecuteReaderAsync(Behavior);
            await using (var __ = reader2)
            {
                Assert.That(async () => await reader2.ReadAsync(), Is.EqualTo(true));
                Assert.That(() => reader2.GetInt32(0), Is.EqualTo(2));
                Assert.That(reader1, Is.Not.SameAs(reader2));

                await reader2.CloseAsync();
                await conn2.CloseAsync();
            }

            await using var conn3 = await OpenConnectionAsync(connectionString);
            using var cmd3 = conn3.CreateCommand();
            cmd3.CommandText = "SELECT 3";
            var reader3 = await cmd3.ExecuteReaderAsync(Behavior);
            await using (var __ = reader3)
            {
                Assert.That(async () => await reader3.ReadAsync(), Is.EqualTo(true));
                Assert.That(() => reader3.GetInt32(0), Is.EqualTo(3));
                Assert.That(reader1, Is.SameAs(reader3));

                await reader3.CloseAsync();
                await conn3.CloseAsync();
            }
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/3772")]
        public async Task Bug3772()
        {
            if (!IsSequential)
                return;

            await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);

            var pgMock = await postmasterMock.WaitForServerConnection();
            pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Int4), new FieldDescription(PostgresTypeOIDs.Bytea));

            var intValue = new byte[] { 0, 0, 0, 1 };
            var byteValue = new byte[] { 1, 2, 3, 4 };

            var writeBuffer = pgMock.WriteBuffer;
            writeBuffer.WriteByte((byte)BackendMessageCode.DataRow);
            writeBuffer.WriteInt32(4 + 2 + intValue.Length + byteValue.Length + 8);
            writeBuffer.WriteInt16(2);
            writeBuffer.WriteInt32(intValue.Length);
            writeBuffer.WriteBytes(intValue);
            await pgMock.FlushAsync();

            using var cmd = new OpenGaussCommand("SELECT some_int, some_byte FROM some_table", conn);
            await using var reader = await cmd.ExecuteReaderAsync(Behavior);

            await reader.ReadAsync();

            reader.GetInt32(0);

            Assert.That(reader.Connector.ReadBuffer.ReadBytesLeft, Is.Zero);
            Assert.That(reader.Connector.ReadBuffer.ReadPosition, Is.Not.Zero);

            writeBuffer.WriteInt32(byteValue.Length);
            writeBuffer.WriteBytes(byteValue);
            await pgMock
                .WriteDataRow(intValue, Enumerable.Range(1, 100).Select(x => (byte)x).ToArray())
                .WriteCommandComplete()
                .WriteReadyForQuery()
                .FlushAsync();

            await reader.GetFieldValueAsync<byte[]>(1);

            Assert.DoesNotThrowAsync(reader.ReadAsync);
        }

        [Test]
        public async Task Dispose_swallows_exceptions([Values(true, false)] bool async)
        {
            await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);
            var pgMock = await postmasterMock.WaitForServerConnection();

            // Write responses for the query, but break the connection before sending CommandComplete/ReadyForQuery
            await pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Int4))
                .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
                .FlushAsync();

            using var cmd = new OpenGaussCommand("SELECT 1", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();

            pgMock.Close();

            if (async)
                Assert.DoesNotThrow(() => reader.Dispose());
            else
                Assert.DoesNotThrowAsync(async () => await reader.DisposeAsync());
        }

        #region GetBytes / GetStream

        [Test]
        public async Task GetBytes()
        {
            using var conn = await OpenConnectionAsync();
            await using var __ = await CreateTempTable(conn, "bytes BYTEA", out var table);

            // TODO: This is too small to actually test any interesting sequential behavior
            byte[] expected = { 1, 2, 3, 4, 5 };
            var actual = new byte[expected.Length];
            await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (bytes) VALUES ({EncodeByteaHex(expected)})");

            var query = $"SELECT bytes, 'foo', bytes, 'bar', bytes, bytes FROM {table}";
            using var cmd = new OpenGaussCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();

            Assert.That(reader.GetBytes(0, 0, actual, 0, 2), Is.EqualTo(2));
            Assert.That(actual[0], Is.EqualTo(expected[0]));
            Assert.That(actual[1], Is.EqualTo(expected[1]));
            Assert.That(reader.GetBytes(0, 0, null, 0, 0), Is.EqualTo(expected.Length), "Bad column length");
            if (IsSequential)
                Assert.That(() => reader.GetBytes(0, 0, actual, 4, 1),
                    Throws.Exception.TypeOf<InvalidOperationException>(), "Seek back sequential");
            else
            {
                Assert.That(reader.GetBytes(0, 0, actual, 4, 1), Is.EqualTo(1));
                Assert.That(actual[4], Is.EqualTo(expected[0]));
            }
            Assert.That(reader.GetBytes(0, 2, actual, 2, 3), Is.EqualTo(3));
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(reader.GetBytes(0, 0, null, 0, 0), Is.EqualTo(expected.Length), "Bad column length");

            Assert.That(() => reader.GetBytes(1, 0, null, 0, 0), Throws.Exception.TypeOf<InvalidCastException>(),
                "GetBytes on non-bytea");
            Assert.That(() => reader.GetBytes(1, 0, actual, 0, 1),
                Throws.Exception.TypeOf<InvalidCastException>(),
                "GetBytes on non-bytea");
            Assert.That(reader.GetString(1), Is.EqualTo("foo"));
            reader.GetBytes(2, 0, actual, 0, 2);
            // Jump to another column from the middle of the column
            reader.GetBytes(4, 0, actual, 0, 2);
            Assert.That(reader.GetBytes(4, expected.Length - 1, actual, 0, 2), Is.EqualTo(1),
                "Length greater than data length");
            Assert.That(actual[0], Is.EqualTo(expected[expected.Length - 1]), "Length greater than data length");
            Assert.That(() => reader.GetBytes(4, 0, actual, 0, actual.Length + 1),
                Throws.Exception.TypeOf<IndexOutOfRangeException>(), "Length great than output buffer length");
            // Close in the middle of a column
            reader.GetBytes(5, 0, actual, 0, 2);

            //var result = (byte[]) cmd.ExecuteScalar();
            //Assert.AreEqual(2, result.Length);
        }

        [Test]
        public async Task GetStream_second_time_throws([Values(true, false)] bool isAsync)
        {
            var expected = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var streamGetter = BuildStreamGetter(isAsync);

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand($"SELECT {EncodeByteaHex(expected)}::bytea", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);

            await reader.ReadAsync();

            using var stream = await streamGetter(reader, 0);

            Assert.That(async () => await streamGetter(reader, 0),
                Throws.Exception.TypeOf<InvalidOperationException>());
        }

        public static IEnumerable GetStreamCases()
        {
            var binary = MemoryMarshal
                .AsBytes<int>(Enumerable.Range(0, 1024).ToArray())
                .ToArray();
            yield return (binary, binary);

            var bigBinary = MemoryMarshal
                .AsBytes<int>(Enumerable.Range(0, 8193).ToArray())
                .ToArray();
            yield return (bigBinary, bigBinary);

            var bigint = 0xDEADBEEFL;
            var bigintBinary = BitConverter.GetBytes(
                BitConverter.IsLittleEndian
                ? BinaryPrimitives.ReverseEndianness(bigint)
                : bigint);
            yield return (bigint, bigintBinary);
        }

        [Test]
        public async Task GetStream<T>(
            [ValueSource(nameof(GetStreamCases))] (T Generic, byte[] Binary) value,
            [Values(true, false)] bool isAsync)
        {
            var streamGetter = BuildStreamGetter(isAsync);
            var expected = value.Binary;
            var actual = new byte[expected.Length];

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p, @p", conn) { Parameters = { new OpenGaussParameter("p", value.Generic) } };
            using var reader = await cmd.ExecuteReaderAsync(Behavior);

            await reader.ReadAsync();

            using var stream = await streamGetter(reader, 0);
            Assert.That(stream.CanSeek, Is.EqualTo(Behavior == CommandBehavior.Default));
            Assert.That(stream.Length, Is.EqualTo(expected.Length));

            var position = 0;
            while (position < actual.Length)
            {
                if (isAsync)
                    position += await stream.ReadAsync(actual, position, actual.Length - position);
                else
                    position += stream.Read(actual, position, actual.Length - position);
            }

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public async Task Open_stream_when_changing_columns([Values(true, false)] bool isAsync)
        {
            var streamGetter = BuildStreamGetter(isAsync);

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand(@"SELECT @p, @p", conn);
            var data = new byte[] { 1, 2, 3 };
            cmd.Parameters.Add(new OpenGaussParameter("p", data));
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
            var stream = await streamGetter(reader, 0);
            // ReSharper disable once UnusedVariable
            var v = reader.GetValue(1);
            Assert.That(() => stream.ReadByte(), Throws.Exception.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task Open_stream_when_changing_rows([Values(true, false)] bool isAsync)
        {
            var streamGetter = BuildStreamGetter(isAsync);

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand(@"SELECT @p", conn);
            var data = new byte[] { 1, 2, 3 };
            cmd.Parameters.Add(new OpenGaussParameter("p", data));
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
            var s1 = await streamGetter(reader, 0);
            reader.Read();
            Assert.That(() => s1.ReadByte(), Throws.Exception.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task GetBytes_with_null([Values(true, false)] bool isAsync)
        {
            var streamGetter = BuildStreamGetter(isAsync);

            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "bytes BYTEA", out var table);

            var buf = new byte[8];
            await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (bytes) VALUES (NULL)");
            using var cmd = new OpenGaussCommand($"SELECT bytes FROM {table}", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
            Assert.That(reader.IsDBNull(0), Is.True);
            Assert.That(() => reader.GetBytes(0, 0, buf, 0, 1), Throws.Exception.TypeOf<InvalidCastException>(), "GetBytes");
            Assert.That(async () => await streamGetter(reader, 0), Throws.Exception.TypeOf<InvalidCastException>(), "GetStream");
            Assert.That(() => reader.GetBytes(0, 0, null, 0, 0), Throws.Exception.TypeOf<InvalidCastException>(), "GetBytes with null buffer");
        }

        static Func<OpenGaussDataReader, int, Task<Stream>> BuildStreamGetter(bool isAsync)
            => isAsync
                ? (Func<OpenGaussDataReader, int, Task<Stream>>)((r, index) => r.GetStreamAsync(index))
                : (r, index) => Task.FromResult(r.GetStream(index));

        #endregion GetBytes / GetStream

        #region GetChars / GetTextReader

        [Test]
        public async Task GetChars()
        {
            using var conn = await OpenConnectionAsync();
            // TODO: This is too small to actually test any interesting sequential behavior
            const string str = "ABCDE";
            var expected = str.ToCharArray();
            var actual = new char[expected.Length];

            var queryText = $@"SELECT '{str}', 3, '{str}', 4, '{str}', '{str}', '{str}'";
            using var cmd = new OpenGaussCommand(queryText, conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();

            Assert.That(reader.GetChars(0, 0, actual, 0, 2), Is.EqualTo(2));
            Assert.That(actual[0], Is.EqualTo(expected[0]));
            Assert.That(actual[1], Is.EqualTo(expected[1]));
            Assert.That(reader.GetChars(0, 0, null, 0, 0), Is.EqualTo(expected.Length), "Bad column length");
            // Note: Unlike with bytea, finding out the length of the column consumes it (variable-width
            // UTF8 encoding)
            Assert.That(reader.GetChars(2, 0, actual, 0, 2), Is.EqualTo(2));
            if (IsSequential)
                Assert.That(() => reader.GetChars(2, 0, actual, 4, 1), Throws.Exception.TypeOf<InvalidOperationException>(), "Seek back sequential");
            else
            {
                Assert.That(reader.GetChars(2, 0, actual, 4, 1), Is.EqualTo(1));
                Assert.That(actual[4], Is.EqualTo(expected[0]));
            }
            Assert.That(reader.GetChars(2, 2, actual, 2, 3), Is.EqualTo(3));
            Assert.That(actual, Is.EqualTo(expected));
            //Assert.That(reader.GetChars(2, 0, null, 0, 0), Is.EqualTo(expected.Length), "Bad column length");

            Assert.That(() => reader.GetChars(3, 0, null, 0, 0), Throws.Exception.TypeOf<InvalidCastException>(), "GetChars on non-text");
            Assert.That(() => reader.GetChars(3, 0, actual, 0, 1), Throws.Exception.TypeOf<InvalidCastException>(), "GetChars on non-text");
            Assert.That(reader.GetInt32(3), Is.EqualTo(4));
            reader.GetChars(4, 0, actual, 0, 2);
            // Jump to another column from the middle of the column
            reader.GetChars(5, 0, actual, 0, 2);
            Assert.That(reader.GetChars(5, expected.Length - 1, actual, 0, 2), Is.EqualTo(1), "Length greater than data length");
            Assert.That(actual[0], Is.EqualTo(expected[expected.Length - 1]), "Length greater than data length");
            Assert.That(() => reader.GetChars(5, 0, actual, 0, actual.Length + 1), Throws.Exception.TypeOf<IndexOutOfRangeException>(), "Length great than output buffer length");
            // Close in the middle of a column
            reader.GetChars(6, 0, actual, 0, 2);
        }

        [Test]
        public async Task GetTextReader([Values(true, false)] bool isAsync)
        {
            Func<OpenGaussDataReader, int, Task<TextReader>> textReaderGetter;
            if (isAsync)
                textReaderGetter = (r, index) => r.GetTextReaderAsync(index);
            else
                textReaderGetter = (r, index) => Task.FromResult(r.GetTextReader(index));

            using var conn = await OpenConnectionAsync();
            // TODO: This is too small to actually test any interesting sequential behavior
            const string str = "ABCDE";
            var expected = str.ToCharArray();
            var actual = new char[expected.Length];
            //ExecuteNonQuery(String.Format(@"INSERT INTO data (field_text) VALUES ('{0}')", str));

            var queryText = $@"SELECT '{str}', 'foo'";
            using var cmd = new OpenGaussCommand(queryText, conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();

            var textReader = await textReaderGetter(reader, 0);
            textReader.Read(actual, 0, 2);
            Assert.That(actual[0], Is.EqualTo(expected[0]));
            Assert.That(actual[1], Is.EqualTo(expected[1]));
            Assert.That(async () => await textReaderGetter(reader, 0),
                Throws.Exception.TypeOf<InvalidOperationException>(),
                "Sequential text reader twice on same column");
            textReader.Read(actual, 2, 1);
            Assert.That(actual[2], Is.EqualTo(expected[2]));
            textReader.Dispose();

            if (IsSequential)
                Assert.That(() => reader.GetChars(0, 0, actual, 4, 1),
                    Throws.Exception.TypeOf<InvalidOperationException>(), "Seek back sequential");
            else
            {
                Assert.That(reader.GetChars(0, 0, actual, 4, 1), Is.EqualTo(1));
                Assert.That(actual[4], Is.EqualTo(expected[0]));
            }
            Assert.That(reader.GetString(1), Is.EqualTo("foo"));
        }

        [Test]
        public async Task Open_TextReader_when_changing_columns()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand(@"SELECT 'some_text', 'some_text'", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
            var textReader = reader.GetTextReader(0);
            // ReSharper disable once UnusedVariable
            var v = reader.GetValue(1);
            Assert.That(() => textReader.Peek(), Throws.Exception.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task Open_TextReader_when_changing_rows()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand(@"SELECT 'some_text', 'some_text'", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
            var tr1 = reader.GetTextReader(0);
            reader.Read();
            Assert.That(() => tr1.Peek(), Throws.Exception.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task GetChars_when_null()
        {
            var buf = new char[8];
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT NULL::TEXT", conn);
            using var reader = await cmd.ExecuteReaderAsync(Behavior);
            reader.Read();
            Assert.That(reader.IsDBNull(0), Is.True);
            Assert.That(() => reader.GetChars(0, 0, buf, 0, 1), Throws.Exception.TypeOf<InvalidCastException>(), "GetChars");
            Assert.That(() => reader.GetTextReader(0), Throws.Exception.TypeOf<InvalidCastException>(), "GetTextReader");
            Assert.That(() => reader.GetChars(0, 0, null, 0, 0), Throws.Exception.TypeOf<InvalidCastException>(), "GetChars with null buffer");
        }

        [Test]
        public async Task Reader_is_reused()
        {
            if (IsMultiplexing)
                Assert.Ignore("Multiplexing: Fails");

            using var conn = await OpenConnectionAsync();
            OpenGaussDataReader reader1;

            using (var cmd = new OpenGaussCommand("SELECT 8", conn))
            using (reader1 = await cmd.ExecuteReaderAsync(Behavior))
            {
                reader1.Read();
                Assert.That(reader1.GetInt32(0), Is.EqualTo(8));
            }

            using (var cmd = new OpenGaussCommand("SELECT 9", conn))
            using (var reader2 = await cmd.ExecuteReaderAsync(Behavior))
            {
                Assert.That(reader2, Is.SameAs(reader1));
                reader2.Read();
                Assert.That(reader2.GetInt32(0), Is.EqualTo(9));
            }
        }

        #endregion GetChars / GetTextReader

#if DEBUG
        [Test, Description("Tests that everything goes well when a type handler generates a OpenGaussSafeReadException")]
        [CancelAfter(5000)]
        public async Task SafeReadException()
        {
            if (IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            // Temporarily reroute integer to go to a type handler which generates SafeReadExceptions
            conn.TypeMapper.AddTypeResolverFactory(new ExplodingTypeHandlerResolverFactory(safe: true));
            using var cmd = new OpenGaussCommand(@"SELECT 1, 'hello'", conn);
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            reader.Read();
            Assert.That(() => reader.GetInt32(0),
                Throws.Exception.With.Message.EqualTo("Safe read exception as requested"));
            Assert.That(reader.GetString(1), Is.EqualTo("hello"));
        }

        [Test, Description("Tests that when a type handler generates an exception that isn't a OpenGaussSafeReadException, the connection is properly broken")]
        [CancelAfter(5000)]
        public async Task Non_SafeReadException()
        {
            if (IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            // Temporarily reroute integer to go to a type handler which generates some exception
            conn.TypeMapper.AddTypeResolverFactory(new ExplodingTypeHandlerResolverFactory(safe: false));
            using var cmd = new OpenGaussCommand(@"SELECT 1, 'hello'", conn);
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            reader.Read();
            Assert.That(() => reader.GetInt32(0), Throws.Exception.With.Message.EqualTo("Non-safe read exception as requested"));
            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Broken));
            Assert.That(conn.State, Is.EqualTo(ConnectionState.Closed));
        }
#endif

        #region Cancellation

        [Test, Description("Cancels ReadAsync via the OpenGaussCommand.Cancel, with successful PG cancellation")]
        public async Task ReadAsync_cancel_command_soft()
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);

            // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
            var pgMock = await postmasterMock.WaitForServerConnection();
            await pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Int4))
                .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
                .FlushAsync();

            using var cmd = new OpenGaussCommand("SELECT some_int FROM some_table", conn);
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                // Successfully read the first row
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.That(reader.GetInt32(0), Is.EqualTo(1));

                // Attempt to read the second row - simulate blocking and cancellation
                var task = reader.ReadAsync();
                cmd.Cancel();

                var processId = (await postmasterMock.WaitForCancellationRequest()).ProcessId;
                Assert.That(processId, Is.EqualTo(conn.ProcessID));

                await pgMock
                    .WriteErrorResponse(PostgresErrorCodes.QueryCanceled)
                    .WriteReadyForQuery()
                    .FlushAsync();

                var exception = Assert.ThrowsAsync<OperationCanceledException>(async () => await task)!;
                Assert.That(exception.InnerException,
                    Is.TypeOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo(PostgresErrorCodes.QueryCanceled));

                Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Open | ConnectionState.Fetching));
            }

            await pgMock.WriteScalarResponseAndFlush(1);
            Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));
        }

        [Test, Description("Cancels ReadAsync via the cancellation token, with successful PG cancellation")]
        public async Task ReadAsync_cancel_soft()
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);

            // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
            var pgMock = await postmasterMock.WaitForServerConnection();
            await pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Int4))
                .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
                .FlushAsync();

            using var cmd = new OpenGaussCommand("SELECT some_int FROM some_table", conn);
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                // Successfully read the first row
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.That(reader.GetInt32(0), Is.EqualTo(1));

                // Attempt to read the second row - simulate blocking and cancellation
                var cancellationSource = new CancellationTokenSource();
                var task = reader.ReadAsync(cancellationSource.Token);
                cancellationSource.Cancel();

                var processId = (await postmasterMock.WaitForCancellationRequest()).ProcessId;
                Assert.That(processId, Is.EqualTo(conn.ProcessID));

                await pgMock
                    .WriteErrorResponse(PostgresErrorCodes.QueryCanceled)
                    .WriteReadyForQuery()
                    .FlushAsync();

                var exception = Assert.ThrowsAsync<OperationCanceledException>(async () => await task)!;
                Assert.That(exception.InnerException,
                    Is.TypeOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo(PostgresErrorCodes.QueryCanceled));
                Assert.That(exception.CancellationToken, Is.EqualTo(cancellationSource.Token));

                Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Open | ConnectionState.Fetching));
            }

            await pgMock.WriteScalarResponseAndFlush(1);
            Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));
        }

        [Test, Description("Cancels NextResultAsync via the cancellation token, with successful PG cancellation")]
        public async Task NextResult_cancel_soft()
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);

            // Write responses to the query we're about to send, only for the first resultset (we'll attempt to read two)
            var pgMock = await postmasterMock.WaitForServerConnection();
            await pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Int4))
                .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
                .WriteCommandComplete()
                .FlushAsync();

            using var cmd = new OpenGaussCommand("SELECT 1; SELECT 2", conn);
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                // Successfully read the first resultset
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.That(reader.GetInt32(0), Is.EqualTo(1));

                // Attempt to advance to the second resultset - simulate blocking and cancellation
                var cancellationSource = new CancellationTokenSource();
                var task = reader.NextResultAsync(cancellationSource.Token);
                cancellationSource.Cancel();

                var processId = (await postmasterMock.WaitForCancellationRequest()).ProcessId;
                Assert.That(processId, Is.EqualTo(conn.ProcessID));

                await pgMock
                    .WriteErrorResponse(PostgresErrorCodes.QueryCanceled)
                    .WriteReadyForQuery()
                    .FlushAsync();

                var exception = Assert.ThrowsAsync<OperationCanceledException>(async () => await task)!;
                Assert.That(exception.InnerException,
                    Is.TypeOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo(PostgresErrorCodes.QueryCanceled));
                Assert.That(exception.CancellationToken, Is.EqualTo(cancellationSource.Token));

                Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Open | ConnectionState.Fetching));
            }

            await pgMock.WriteScalarResponseAndFlush(1);
            Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));
        }

        [Test, Description("Cancels ReadAsync via the cancellation token, with unsuccessful PG cancellation (socket break)")]
        public async Task ReadAsync_cancel_hard([Values(true, false)] bool passCancelledToken)
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);

            // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
            var pgMock = await postmasterMock.WaitForServerConnection();
            await pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Int4))
                .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
                .FlushAsync();

            using var cmd = new OpenGaussCommand("SELECT some_int FROM some_table", conn);
            await using var reader = await cmd.ExecuteReaderAsync(Behavior);

            // Successfully read the first row
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));

            // Attempt to read the second row - simulate blocking and cancellation
            var cancellationSource = new CancellationTokenSource();
            if (passCancelledToken)
                cancellationSource.Cancel();
            var task = reader.ReadAsync(cancellationSource.Token);
            cancellationSource.Cancel();

            var processId = (await postmasterMock.WaitForCancellationRequest()).ProcessId;
            Assert.That(processId, Is.EqualTo(conn.ProcessID));

            // Send no response from server, wait for the cancellation attempt to time out
            var exception = Assert.ThrowsAsync<OperationCanceledException>(async () => await task)!;
            Assert.That(exception.InnerException, Is.TypeOf<TimeoutException>());
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellationSource.Token));

            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Broken));
        }

        [Test, Description("Cancels NextResultAsync via the cancellation token, with unsuccessful PG cancellation (socket break)")]
        public async Task NextResultAsync_cancel_hard([Values(true, false)] bool passCancelledToken)
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);

            // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
            var pgMock = await postmasterMock.WaitForServerConnection();
            await pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Int4))
                .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
                .WriteCommandComplete()
                .FlushAsync();

            using var cmd = new OpenGaussCommand("SELECT some_int FROM some_table", conn);
            await using var reader = await cmd.ExecuteReaderAsync(Behavior);

            // Successfully read the first resultset
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));

            // Attempt to read the second row - simulate blocking and cancellation
            var cancellationSource = new CancellationTokenSource();
            if (passCancelledToken)
                cancellationSource.Cancel();
            var task = reader.NextResultAsync(cancellationSource.Token);
            cancellationSource.Cancel();

            var processId = (await postmasterMock.WaitForCancellationRequest()).ProcessId;
            Assert.That(processId, Is.EqualTo(conn.ProcessID));

            // Send no response from server, wait for the cancellation attempt to time out
            var exception = Assert.ThrowsAsync<OperationCanceledException>(async () => await task)!;
            Assert.That(exception.InnerException, Is.TypeOf<TimeoutException>());
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellationSource.Token));

            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Broken));
        }

        [Test, Description("Cancels sequential ReadAsGetFieldValueAsync")]
        public async Task GetFieldValueAsync_sequential_cancel([Values(true, false)] bool passCancelledToken)
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            if (!IsSequential)
                return;

            await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);

            // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
            var pgMock = await postmasterMock.WaitForServerConnection();
            await pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Bytea))
                .WriteDataRowWithFlush(new byte[10000]);

            using var cmd = new OpenGaussCommand("SELECT some_bytea FROM some_table", conn);
            await using var reader = await cmd.ExecuteReaderAsync(Behavior);

            await reader.ReadAsync();

            using var cts = new CancellationTokenSource();
            if (passCancelledToken)
                cts.Cancel();
            var task = reader.GetFieldValueAsync<byte[]>(0, cts.Token);
            cts.Cancel();

            var exception = Assert.ThrowsAsync<OperationCanceledException>(async () => await task)!;
            Assert.That(exception.InnerException, Is.Null);

            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Broken));
        }

        [Test, Description("Cancels sequential ReadAsGetFieldValueAsync")]
        public async Task IsDBNullAsync_sequential_cancel([Values(true, false)] bool passCancelledToken)
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            if (!IsSequential)
                return;

            await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);

            // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
            var pgMock = await postmasterMock.WaitForServerConnection();
            await pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Bytea), new FieldDescription(PostgresTypeOIDs.Int4))
                .WriteDataRowWithFlush(new byte[10000], new byte[4]);

            using var cmd = new OpenGaussCommand("SELECT some_bytea, some_int FROM some_table", conn);
            await using var reader = await cmd.ExecuteReaderAsync(Behavior);

            await reader.ReadAsync();

            using var cts = new CancellationTokenSource();
            if (passCancelledToken)
                cts.Cancel();
            var task = reader.IsDBNullAsync(1, cts.Token);
            cts.Cancel();

            var exception = Assert.ThrowsAsync<OperationCanceledException>(async () => await task)!;
            Assert.That(exception.InnerException, Is.Null);

            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Broken));
        }

        [Test, Description("Cancellation does not work with the multiplexing")]
        public async Task Cancel_multiplexing_disabled()
        {
            if (!IsMultiplexing)
                return;

            using var _ = CreateTempPool(ConnectionString, out var connString);
            await using var conn = await OpenConnectionAsync(connString);
            await using var cmd = new OpenGaussCommand("SELECT generate_series(1, 100); SELECT generate_series(1, 100)", conn);
            using var cts = new CancellationTokenSource();
            await using var reader = await cmd.ExecuteReaderAsync(Behavior);
            Assert.That(await reader.ReadAsync(), Is.True);
            cts.Cancel();
            while (await reader.ReadAsync(cts.Token)) { }
            Assert.That(await reader.NextResultAsync(cts.Token), Is.True);
            while (await reader.ReadAsync(cts.Token)) { }
            Assert.That(conn.Connector!.UserCancellationRequested, Is.False);
        }

        #endregion Cancellation

        #region Timeout

        [Test, Description("Timeouts sequential ReadAsGetFieldValueAsync")]
        [CancelAfter(10000)]
        public async Task GetFieldValueAsync_sequential_timeout()
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            if (!IsSequential)
                return;

            var csb = new OpenGaussConnectionStringBuilder(ConnectionString);
            csb.CommandTimeout = 3;
            csb.CancellationTimeout = 15000;

            await using var postmasterMock = PgPostmasterMock.Start(csb.ToString());
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);

            // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
            var pgMock = await postmasterMock.WaitForServerConnection();
            await pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Bytea))
                .WriteDataRowWithFlush(new byte[10000]);

            using var cmd = new OpenGaussCommand("SELECT some_bytea FROM some_table", conn);
            await using var reader = await cmd.ExecuteReaderAsync(Behavior);

            await reader.ReadAsync();

            var task = reader.GetFieldValueAsync<byte[]>(0);

            var exception = Assert.ThrowsAsync<OpenGaussException>(async () => await task)!;
            Assert.That(exception.InnerException, Is.TypeOf<TimeoutException>());

            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Broken));
        }

        [Test, Description("Timeouts sequential IsDBNullAsync")]
        [CancelAfter(10000)]
        public async Task IsDBNullAsync_sequential_timeout()
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            if (!IsSequential)
                return;

            var csb = new OpenGaussConnectionStringBuilder(ConnectionString);
            csb.CommandTimeout = 3;
            csb.CancellationTimeout = 15000;

            await using var postmasterMock = PgPostmasterMock.Start(csb.ToString());
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);

            // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
            var pgMock = await postmasterMock.WaitForServerConnection();
            await pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Bytea), new FieldDescription(PostgresTypeOIDs.Int4))
                .WriteDataRowWithFlush(new byte[10000], new byte[4]);

            using var cmd = new OpenGaussCommand("SELECT some_bytea, some_int FROM some_table", conn);
            await using var reader = await cmd.ExecuteReaderAsync(Behavior);

            await reader.ReadAsync();

            var task = reader.GetFieldValueAsync<byte[]>(0);

            var exception = Assert.ThrowsAsync<OpenGaussException>(async () => await task)!;
            Assert.That(exception.InnerException, Is.TypeOf<TimeoutException>());

            Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Broken));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/3446")]
        public async Task Bug3446()
        {
            if (IsMultiplexing)
                return; // Multiplexing, cancellation

            await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
            using var _ = CreateTempPool(postmasterMock.ConnectionString, out var connectionString);
            await using var conn = await OpenConnectionAsync(connectionString);

            var pgMock = await postmasterMock.WaitForServerConnection();
            await pgMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(PostgresTypeOIDs.Int4))
                .WriteDataRow(new byte[4])
                .FlushAsync();

            using var cmd = new OpenGaussCommand("SELECT some_int FROM some_table", conn);
            await using (var reader = await cmd.ExecuteReaderAsync(Behavior))
            {
                await reader.ReadAsync();
                cmd.Cancel();
                await postmasterMock.WaitForCancellationRequest();
                await pgMock
                        .WriteErrorResponse(PostgresErrorCodes.QueryCanceled)
                        .WriteReadyForQuery()
                        .FlushAsync();
            }

            Assert.That(conn.Connector!.State, Is.EqualTo(ConnectorState.Ready));
        }

        #endregion

        #region Initialization / setup / teardown

        // ReSharper disable InconsistentNaming
        readonly bool IsSequential;
        readonly CommandBehavior Behavior;
        // ReSharper restore InconsistentNaming

        public ReaderTests(MultiplexingMode multiplexingMode, CommandBehavior behavior) : base(multiplexingMode)
        {
            Behavior = behavior;
            IsSequential = (Behavior & CommandBehavior.SequentialAccess) != 0;
        }

        #endregion
    }

    #region Mock Type Handlers

    class ExplodingTypeHandlerResolverFactory : TypeHandlerResolverFactory
    {
        readonly bool _safe;
        public ExplodingTypeHandlerResolverFactory(bool safe) => _safe = safe;
        public override TypeHandlerResolver Create(OpenGaussConnector connector) => new ExplodingTypeHandlerResolver(_safe);

        public override TypeMappingInfo GetMappingByDataTypeName(string dataTypeName) => throw new NotSupportedException();
        public override string? GetDataTypeNameByClrType(Type clrType) => throw new NotSupportedException();
        public override string? GetDataTypeNameByValueDependentValue(object value) => throw new NotSupportedException();

        class ExplodingTypeHandlerResolver : TypeHandlerResolver
        {
            readonly bool _safe;

            public ExplodingTypeHandlerResolver(bool safe) => _safe = safe;

            public override OpenGaussTypeHandler? ResolveByDataTypeName(string typeName) =>
                typeName == "integer" ? new ExplodingTypeHandler(null!, _safe) : null;
            public override OpenGaussTypeHandler? ResolveByClrType(Type type) => null;
            public override TypeMappingInfo GetMappingByDataTypeName(string dataTypeName) => throw new NotImplementedException();
        }
    }

    class ExplodingTypeHandler : OpenGaussSimpleTypeHandler<int>
    {
        readonly bool _safe;

        internal ExplodingTypeHandler(PostgresType postgresType, bool safe) : base(postgresType) => _safe = safe;

        public override int Read(OpenGaussReadBuffer buf, int len, FieldDescription? fieldDescription = null)
        {
            buf.ReadInt32();

            throw _safe
                ? new Exception("Safe read exception as requested")
                : buf.Connector.Break(new Exception("Non-safe read exception as requested"));
        }

        public override int ValidateAndGetLength(int value, OpenGaussParameter? parameter) => throw new NotSupportedException();
        public override int ValidateObjectAndGetLength(object? value, ref OpenGaussLengthCache? lengthCache, OpenGaussParameter? parameter)
            => throw new NotSupportedException();
        public override void Write(int value, OpenGaussWriteBuffer buf, OpenGaussParameter? parameter) => throw new NotSupportedException();

        public override Task WriteObjectWithLength(
            object? value,
            OpenGaussWriteBuffer buf,
            OpenGaussLengthCache? lengthCache,
            OpenGaussParameter? parameter,
            bool async,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    #endregion
}
