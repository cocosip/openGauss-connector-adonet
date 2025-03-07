﻿using System;
using System.Data;
using System.Threading.Tasks;
using OpenGauss.NET.Types;
using NUnit.Framework;
using OpenGauss.NET;

namespace OpenGauss.Tests.Types
{
    public class MoneyTests : MultiplexingTestBase
    {
        static readonly object[] ReadWriteCases = new[]
        {
            new object[] { "1.22::money", 1.22M },
            new object[] { "1000.22::money", 1000.22M },
            new object[] { "1000000.22::money", 1000000.22M },
            new object[] { "1000000000.22::money", 1000000000.22M },
            new object[] { "1000000000000.22::money", 1000000000000.22M },
            new object[] { "1000000000000000.22::money", 1000000000000000.22M },

            new object[] { "(+92233720368547758.07::numeric)::money", +92233720368547758.07M },
            new object[] { "(-92233720368547758.08::numeric)::money", -92233720368547758.08M },
        };

        [Test]
        [TestCaseSource(nameof(ReadWriteCases))]
        public async Task Read(string query, decimal expected)
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT " + query, conn);
            Assert.That(
                decimal.GetBits((decimal)(await cmd.ExecuteScalarAsync())!),
                Is.EqualTo(decimal.GetBits(expected)));
        }

        [Test]
        [TestCaseSource(nameof(ReadWriteCases))]
        public async Task Write(string query, decimal expected)
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p, @p = " + query, conn);
            cmd.Parameters.Add(new OpenGaussParameter("p", OpenGaussDbType.Money) { Value = expected });
            using var rdr = await cmd.ExecuteReaderAsync();
            rdr.Read();
            Assert.That(decimal.GetBits(rdr.GetFieldValue<decimal>(0)), Is.EqualTo(decimal.GetBits(expected)));
            Assert.That(rdr.GetFieldValue<bool>(1));
        }

        static readonly object[] WriteWithLargeScaleCases = new[]
        {
            new object[] { "0.004::money", 0.004M, 0.00M },
            new object[] { "0.005::money", 0.005M, 0.01M },
        };

        [Test]
        [TestCaseSource(nameof(WriteWithLargeScaleCases))]
        public async Task Write_with_large_scale(string query, decimal parameter, decimal expected)
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p, @p = " + query, conn);
            cmd.Parameters.Add(new OpenGaussParameter("p", OpenGaussDbType.Money) { Value = parameter });
            using var rdr = await cmd.ExecuteReaderAsync();
            rdr.Read();
            Assert.That(decimal.GetBits(rdr.GetFieldValue<decimal>(0)), Is.EqualTo(decimal.GetBits(expected)));
            Assert.That(rdr.GetFieldValue<bool>(1));
        }

        [Test]
        public async Task Mapping()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p1, @p2", conn);
            cmd.Parameters.Add(new OpenGaussParameter("p1", OpenGaussDbType.Money) { Value = 8M });
            cmd.Parameters.Add(new OpenGaussParameter("p2", DbType.Currency) { Value = 8M });

            using var rdr = await cmd.ExecuteReaderAsync();
            rdr.Read();
            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                Assert.That(rdr.GetFieldType(i), Is.EqualTo(typeof(decimal)));
                Assert.That(rdr.GetDataTypeName(i), Is.EqualTo("money"));
                Assert.That(rdr.GetValue(i), Is.EqualTo(8M));
                Assert.That(rdr.GetProviderSpecificValue(i), Is.EqualTo(8M));
                Assert.That(rdr.GetFieldValue<decimal>(i), Is.EqualTo(8M));
                Assert.That(() => rdr.GetFieldValue<byte>(i), Throws.InstanceOf<InvalidCastException>());
                Assert.That(() => rdr.GetFieldValue<short>(i), Throws.InstanceOf<InvalidCastException>());
                Assert.That(() => rdr.GetFieldValue<int>(i), Throws.InstanceOf<InvalidCastException>());
                Assert.That(() => rdr.GetFieldValue<long>(i), Throws.InstanceOf<InvalidCastException>());
                Assert.That(() => rdr.GetFieldValue<float>(i), Throws.InstanceOf<InvalidCastException>());
                Assert.That(() => rdr.GetFieldValue<double>(i), Throws.InstanceOf<InvalidCastException>());
            }
        }

        public MoneyTests(MultiplexingMode multiplexingMode) : base(multiplexingMode) {}
    }
}
