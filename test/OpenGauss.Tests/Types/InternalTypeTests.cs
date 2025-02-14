﻿using System.Threading.Tasks;
using OpenGauss.NET.Types;
using NUnit.Framework;
using OpenGauss.NET;

namespace OpenGauss.Tests.Types
{
    public class InternalTypeTests : MultiplexingTestBase
    {
        [Test]
        public async Task Read_internal_char()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT typdelim FROM pg_type WHERE typname='int4'", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetChar(0), Is.EqualTo(','));
            Assert.That(reader.GetValue(0), Is.EqualTo(','));
            Assert.That(reader.GetProviderSpecificValue(0), Is.EqualTo(','));
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(char)));
        }

        [Test]
        [TestCase(OpenGaussDbType.Oid)]
        [TestCase(OpenGaussDbType.Regtype)]
        [TestCase(OpenGaussDbType.Regconfig)]
        public async Task Internal_uint_types(OpenGaussDbType opengaussDbType)
        {
            var postgresType = opengaussDbType.ToString().ToLowerInvariant();
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand($"SELECT @max, 4294967295::{postgresType}, @eight, 8::{postgresType}", conn);
            cmd.Parameters.AddWithValue("max", opengaussDbType, uint.MaxValue);
            cmd.Parameters.AddWithValue("eight", opengaussDbType, 8u);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();

            for (var i = 0; i < reader.FieldCount; i++)
                Assert.That(reader.GetFieldType(i), Is.EqualTo(typeof(uint)));

            Assert.That(reader.GetValue(0), Is.EqualTo(uint.MaxValue));
            Assert.That(reader.GetValue(1), Is.EqualTo(uint.MaxValue));
            Assert.That(reader.GetValue(2), Is.EqualTo(8u));
            Assert.That(reader.GetValue(3), Is.EqualTo(8u));
        }

        [Test]
        public async Task Tid()
        {
            var expected = new OpenGaussTid(3, 5);
            using var conn = await OpenConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT '(1234,40000)'::tid, @p::tid";
            cmd.Parameters.AddWithValue("p", OpenGaussDbType.Tid, expected);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetFieldValue<OpenGaussTid>(0).BlockNumber, Is.EqualTo(1234));
            Assert.That(reader.GetFieldValue<OpenGaussTid>(0).OffsetNumber, Is.EqualTo(40000));
            Assert.That(reader.GetFieldValue<OpenGaussTid>(1).BlockNumber, Is.EqualTo(expected.BlockNumber));
            Assert.That(reader.GetFieldValue<OpenGaussTid>(1).OffsetNumber, Is.EqualTo(expected.OffsetNumber));
        }

        #region OpenGaussLogSequenceNumber / PgLsn

        static readonly TestCaseData[] EqualsObjectCases = {
            new TestCaseData(new OpenGaussLogSequenceNumber(1ul), null).Returns(false),
            new TestCaseData(new OpenGaussLogSequenceNumber(1ul), new object()).Returns(false),
            new TestCaseData(new OpenGaussLogSequenceNumber(1ul), 1ul).Returns(false), // no implicit cast
            new TestCaseData(new OpenGaussLogSequenceNumber(1ul), "0/0").Returns(false), // no implicit cast/parsing
            new TestCaseData(new OpenGaussLogSequenceNumber(1ul), new OpenGaussLogSequenceNumber(1ul)).Returns(true),
        };

        [Test, TestCaseSource(nameof(EqualsObjectCases))]
        public bool OpenGaussLogSequenceNumber_equals(OpenGaussLogSequenceNumber lsn, object? obj)
            => lsn.Equals(obj);


        // [Test]
        public async Task OpenGaussLogSequenceNumber()
        {
            var expected1 = new OpenGaussLogSequenceNumber(42949672971ul);
            Assert.That(OpenGauss.NET.Types.OpenGaussLogSequenceNumber.Parse("A/B"), Is.EqualTo(expected1));
            await using var conn = await OpenConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 'A/B'::pg_lsn, @p::pg_lsn";
            cmd.Parameters.AddWithValue("p", OpenGaussDbType.PgLsn, expected1);
            await using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            var result1 = reader.GetFieldValue<OpenGaussLogSequenceNumber>(0);
            var result2 = reader.GetFieldValue<OpenGaussLogSequenceNumber>(1);
            Assert.That(result1, Is.EqualTo(expected1));
            Assert.That((ulong)result1, Is.EqualTo(42949672971ul));
            Assert.That(result1.ToString(), Is.EqualTo("A/B"));
            Assert.That(result2, Is.EqualTo(expected1));
            Assert.That((ulong)result2, Is.EqualTo(42949672971ul));
            Assert.That(result2.ToString(), Is.EqualTo("A/B"));
        }

        #endregion OpenGaussLogSequenceNumber / PgLsn

        public InternalTypeTests(MultiplexingMode multiplexingMode) : base(multiplexingMode) { }
    }
}
