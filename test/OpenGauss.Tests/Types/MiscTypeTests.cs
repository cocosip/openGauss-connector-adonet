﻿using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using OpenGauss.NET.Types;
using NUnit.Framework;
using static OpenGauss.Tests.TestUtil;
using OpenGauss.NET;

namespace OpenGauss.Tests.Types
{
    /// <summary>
    /// Tests on PostgreSQL types which don't fit elsewhere
    /// </summary>
    class MiscTypeTests : MultiplexingTestBase
    {
        [Test, Description("Resolves a base type handler via the different pathways")]
        public async Task Base_type_resolution()
        {
            if (IsMultiplexing)
                Assert.Ignore("Multiplexing, ReloadTypes");

            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Base_type_resolution),  // Prevent backend type caching in TypeHandlerRegistry
                Pooling = false
            };

            using var conn = await OpenConnectionAsync(csb);

            // Resolve type by OpenGaussDbType
            using (var cmd = new OpenGaussCommand("SELECT @p", conn))
            {
                cmd.Parameters.AddWithValue("p", OpenGaussDbType.Integer, DBNull.Value);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    reader.Read();
                    Assert.That(reader.GetDataTypeName(0), Is.EqualTo("integer"));
                }
            }

            // Resolve type by DbType
            conn.ReloadTypes();
            using (var cmd = new OpenGaussCommand("SELECT @p", conn))
            {
                cmd.Parameters.Add(new OpenGaussParameter("p", DbType.Int32) { Value = DBNull.Value });
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    reader.Read();
                    Assert.That(reader.GetDataTypeName(0), Is.EqualTo("integer"));
                }
            }

            // Resolve type by ClrType (type inference)
            conn.ReloadTypes();
            using (var cmd = new OpenGaussCommand("SELECT @p", conn))
            {
                cmd.Parameters.Add(new OpenGaussParameter { ParameterName = "p", Value = 8 });
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    reader.Read();
                    Assert.That(reader.GetDataTypeName(0), Is.EqualTo("integer"));
                    Assert.That(reader.GetInt32(0), Is.EqualTo(8));
                }
            }

            // Resolve type by DataTypeName
            conn.ReloadTypes();
            using (var cmd = new OpenGaussCommand("SELECT @p", conn))
            {
                cmd.Parameters.Add(new OpenGaussParameter { ParameterName = "p", DataTypeName = "integer", Value = DBNull.Value });
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    reader.Read();
                    Assert.That(reader.GetDataTypeName(0), Is.EqualTo("integer"));
                }
            }

            // Resolve type by OID (read)
            conn.ReloadTypes();
            using (var cmd = new OpenGaussCommand("SELECT 8", conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                reader.Read();
                Assert.That(reader.GetDataTypeName(0), Is.EqualTo("integer"));
                Assert.That(reader.GetInt32(0), Is.EqualTo(8));
            }
        }

        /// <summary>
        /// https://www.postgresql.org/docs/current/static/datatype-boolean.html
        /// </summary>
        [Test, Description("Roundtrips a bool")]
        public async Task Bool()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p1, @p2, @p3, @p4", conn);
            var p1 = new OpenGaussParameter("p1", OpenGaussDbType.Boolean);
            var p2 = new OpenGaussParameter("p2", OpenGaussDbType.Boolean);
            var p3 = new OpenGaussParameter("p3", DbType.Boolean);
            var p4 = new OpenGaussParameter { ParameterName = "p4", Value = true };
            Assert.That(p4.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Boolean));
            Assert.That(p4.DbType, Is.EqualTo(DbType.Boolean));
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            cmd.Parameters.Add(p3);
            cmd.Parameters.Add(p4);
            p1.Value = false;
            p2.Value = p3.Value = true;
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();

            Assert.That(reader.GetBoolean(0), Is.False);

            for (var i = 1; i < cmd.Parameters.Count; i++)
            {
                Assert.That(reader.GetBoolean(i), Is.True);
                Assert.That(reader.GetValue(i), Is.True);
                Assert.That(reader.GetProviderSpecificValue(i), Is.True);
                Assert.That(reader.GetFieldType(i), Is.EqualTo(typeof(bool)));
                Assert.That(reader.GetDataTypeName(i), Is.EqualTo("boolean"));
            }
        }

        /// <summary>
        /// https://www.postgresql.org/docs/current/static/datatype-uuid.html
        /// </summary>
        [Test, Description("Roundtrips a UUID")]
        public async Task Uuid()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p1, @p2, @p3", conn);
            var expected = new Guid("a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11");
            var p1 = new OpenGaussParameter("p1", OpenGaussDbType.Uuid);
            var p2 = new OpenGaussParameter("p2", DbType.Guid);
            var p3 = new OpenGaussParameter { ParameterName = "p3", Value = expected };
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            cmd.Parameters.Add(p3);
            p1.Value = p2.Value = expected;
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();

            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                Assert.That(reader.GetGuid(i), Is.EqualTo(expected));
                Assert.That(reader.GetFieldValue<Guid>(i), Is.EqualTo(expected));
                Assert.That(reader.GetValue(i), Is.EqualTo(expected));
                Assert.That(reader.GetFieldType(i), Is.EqualTo(typeof(Guid)));
            }
        }

        [Test, Description("Makes sure that the PostgreSQL 'unknown' type (OID 705) is read properly")]
        public async Task Read_unknown()
        {
            const string expected = "some_text";
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand($"SELECT '{expected}'", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetString(0), Is.EqualTo(expected));
            Assert.That(reader.GetValue(0), Is.EqualTo(expected));
            Assert.That(reader.GetFieldValue<char[]>(0), Is.EqualTo(expected.ToCharArray()));
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(string)));
        }

        [Test, Description("Roundtrips a null value")]
        public async Task Null()
        {
            using var conn = await OpenConnectionAsync();
            using (var cmd = new OpenGaussCommand("SELECT @p1::TEXT, @p2::TEXT, @p3::TEXT", conn))
            {
                cmd.Parameters.AddWithValue("p1", DBNull.Value);
                cmd.Parameters.Add(new OpenGaussParameter<string?>("p2", null));
                cmd.Parameters.Add(new OpenGaussParameter<DBNull>("p3", DBNull.Value));
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    reader.Read();
                    for (var i = 0; i < cmd.Parameters.Count; i++)
                    {
                        Assert.That(reader.IsDBNull(i));
                        Assert.That(reader.GetFieldType(i), Is.EqualTo(typeof(string)));
                    }
                }
            }

            // Setting non-generic OpenGaussParameter.Value is not allowed, only DBNull.Value
            using (var cmd = new OpenGaussCommand("SELECT @p::TEXT", conn))
            {
                cmd.Parameters.AddWithValue("p4", OpenGaussDbType.Text, null!);
                Assert.That(async () => await cmd.ExecuteReaderAsync(), Throws.Exception.TypeOf<InvalidCastException>());
            }
        }

        [Test, Description("PostgreSQL records should be returned as arrays of objects")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/724")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/1980")]
        public async Task Record()
        {
            var recordLiteral = "(1,'foo'::text)::record";
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand($"SELECT {recordLiteral}, ARRAY[{recordLiteral}, {recordLiteral}]", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            var record = (object[])reader[0];
            Assert.That(record[0], Is.EqualTo(1));
            Assert.That(record[1], Is.EqualTo("foo"));

            var arr = (object[][])reader[1];
            Assert.That(arr.Length, Is.EqualTo(2));
            Assert.That(arr[0][0], Is.EqualTo(1));
            Assert.That(arr[1][0], Is.EqualTo(1));
        }

        [Test, Description("Makes sure that setting DbType.Object makes OpenGauss infer the type")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/694")]
        public async Task DbType_causes_inference()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p", conn);
            cmd.Parameters.Add(new OpenGaussParameter { ParameterName = "p", DbType = DbType.Object, Value = 3 });
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(3));
        }

        #region Unrecognized types

        [Test, Description("Retrieves a type as an unknown type, i.e. untreated string")]
        public async Task AllResultTypesAreUnknown()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT TRUE", conn);
            cmd.AllResultTypesAreUnknown = true;
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(string)));
            Assert.That(reader.GetString(0), Is.EqualTo("t"));
        }

        [Test, Description("Mixes and matches an unknown type with a known type")]
        public async Task UnknownResultTypeList()
        {
            if (IsMultiplexing)
                return;

            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT TRUE, 8", conn);
            cmd.UnknownResultTypeList = new[] { true, false };
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(string)));
            Assert.That(reader.GetString(0), Is.EqualTo("t"));
            Assert.That(reader.GetInt32(1), Is.EqualTo(8));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/711")]
        public async Task Known_type_as_unknown()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT 8", conn);
            cmd.AllResultTypesAreUnknown = true;
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("8"));
        }

        [Test, Description("Sends a null value parameter with no OpenGaussDbType or DbType, but with context for the backend to handle it")]
        public async Task Unrecognized_null()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p::TEXT", conn);
            var p = new OpenGaussParameter("p", DBNull.Value);
            cmd.Parameters.Add(p);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.IsDBNull(0));
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(string)));
        }

        [Test, Description("Sends a value parameter with an explicit OpenGaussDbType.Unknown, but with context for the backend to handle it")]
        public async Task Send_unknown()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p::INT4", conn);
            var p = new OpenGaussParameter("p", "8");
            cmd.Parameters.Add(p);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(int)));
            Assert.That(reader.GetInt32(0), Is.EqualTo(8));
        }

        #endregion

        [Test]
        public async Task Int2Vector()
        {
            var expected = new short[] { 4, 5, 6 };
            using var conn = await OpenConnectionAsync();
            using var cmd = conn.CreateCommand();
            TestUtil.MinimumPgVersion(conn, "9.1.0");
            cmd.CommandText = "SELECT @p::int2vector";
            cmd.Parameters.AddWithValue("p", OpenGaussDbType.Int2Vector, expected);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetFieldValue<short[]>(0), Is.EqualTo(expected));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1138")]
        public async Task Void()
        {
            using var conn = await OpenConnectionAsync();
            Assert.That(await conn.ExecuteScalarAsync("SELECT pg_sleep(0)"), Is.SameAs(DBNull.Value));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1364")]
        public async Task Unsupported_DbType()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p", conn);
            Assert.That(() => cmd.Parameters.Add(new OpenGaussParameter("p", DbType.UInt32) { Value = 8u }),
                Throws.Exception.TypeOf<NotSupportedException>());
        }

        // Older tests

        [Test]
        public async Task Bug1011085()
        {
            // Money format is not set in accordance with the system locale format
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand("select :moneyvalue", conn);
            var expectedValue = 8.99m;
            command.Parameters.Add("moneyvalue", OpenGaussDbType.Money).Value = expectedValue;
            var result = (decimal?)await command.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(expectedValue));

            expectedValue = 100m;
            command.Parameters[0].Value = expectedValue;
            result = (decimal?)await command.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(expectedValue));

            expectedValue = 72.25m;
            command.Parameters[0].Value = expectedValue;
            result = (decimal?)await command.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(expectedValue));
        }

        [Test]
        public async Task UUIDDataType()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTableName(conn, out var table);
            var createTable = $@"
CREATE TABLE {table} (
    person_id serial PRIMARY KEY NOT NULL,
    person_uuid uuid NOT NULL
) WITH(OIDS=FALSE);";
            var command = new OpenGaussCommand(createTable, conn);
            await command.ExecuteNonQueryAsync();

            var uuidDbParam = new OpenGaussParameter(":param1", OpenGaussDbType.Uuid);
            uuidDbParam.Value = Guid.NewGuid();

            command = new OpenGaussCommand($"INSERT INTO {table} (person_uuid) VALUES (:param1);", conn);
            command.Parameters.Add(uuidDbParam);
            await command.ExecuteNonQueryAsync();

            command = new OpenGaussCommand($"SELECT person_uuid::uuid FROM {table} LIMIT 1", conn);
            var result = await command.ExecuteScalarAsync();
            Assert.That(result!.GetType(), Is.EqualTo(typeof(Guid)));
        }

        [Test]
        public async Task OidVector()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "Select '1 2 3'::oidvector, :p1";
            cmd.Parameters.AddWithValue("p1", OpenGaussDbType.Oidvector, new uint[] { 4, 5, 6 });
            using var rdr = await cmd.ExecuteReaderAsync();
            rdr.Read();
            Assert.That(rdr.GetValue(0).GetType(), Is.EqualTo(typeof(uint[])));
            Assert.That(rdr.GetValue(1).GetType(), Is.EqualTo(typeof(uint[])));
            Assert.That(rdr.GetFieldValue<uint[]>(0).SequenceEqual(new uint[] { 1, 2, 3 }), Is.True);
            Assert.That(rdr.GetFieldValue<uint[]>(1).SequenceEqual(new uint[] { 4, 5, 6 }), Is.True);
        }

        public MiscTypeTests(MultiplexingMode multiplexingMode) : base(multiplexingMode) { }
    }
}
