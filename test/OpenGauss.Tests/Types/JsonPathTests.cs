﻿using System.Threading.Tasks;
using OpenGauss.NET.Types;
using NUnit.Framework;
using static OpenGauss.Tests.TestUtil;
using OpenGauss.NET;

namespace OpenGauss.Tests.Types
{
    public class JsonPathTests : MultiplexingTestBase
    {
        public JsonPathTests(MultiplexingMode multiplexingMode)
            : base(multiplexingMode) { }

        static readonly object[] ReadWriteCases = new[]
        {
            new object[] { "'$'", "$" },
            new object[] { "'$\"varname\"'", "$\"varname\"" },
        };

        [Test]
        [TestCaseSource(nameof(ReadWriteCases))]
        public async Task Read(string query, string expected)
        {
            using var conn = await OpenConnectionAsync();
            MinimumPgVersion(conn, "12.0", "The jsonpath type was introduced in PostgreSQL 12");

            using var cmd = new OpenGaussCommand($"SELECT {query}::jsonpath", conn);
            using var rdr = await cmd.ExecuteReaderAsync();

            rdr.Read();
            Assert.That(rdr.GetFieldValue<string>(0), Is.EqualTo(expected));
            Assert.That(rdr.GetTextReader(0).ReadToEnd(), Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(nameof(ReadWriteCases))]
        public async Task Write(string query, string expected)
        {
            using var conn = await OpenConnectionAsync();
            MinimumPgVersion(conn, "12.0", "The jsonpath type was introduced in PostgreSQL 12");

            using var cmd = new OpenGaussCommand($"SELECT 'Passed' WHERE @p::text = {query}::text", conn) { Parameters = { new OpenGaussParameter("p", OpenGaussDbType.JsonPath) { Value = expected } } };
            using var rdr = await cmd.ExecuteReaderAsync();

            Assert.That(rdr.Read(), Is.True);
        }
    }
}
