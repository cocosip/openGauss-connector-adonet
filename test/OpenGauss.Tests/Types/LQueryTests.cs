﻿using System.Collections;
using System.Threading.Tasks;
using OpenGauss.NET.Types;
using NUnit.Framework;

namespace OpenGauss.Tests.Types
{
    [TestFixture(MultiplexingMode.NonMultiplexing, false)]
    [TestFixture(MultiplexingMode.NonMultiplexing, true)]
    [TestFixture(MultiplexingMode.Multiplexing, false)]
    [TestFixture(MultiplexingMode.Multiplexing, true)]
    public class LQueryTests : TypeHandlerTestBase<string>
    {
        public LQueryTests(MultiplexingMode multiplexingMode, bool useTypeName) : base(
            multiplexingMode,
            useTypeName ? null : OpenGaussDbType.LQuery,
            useTypeName ? "lquery" : null)
        { }

        public static IEnumerable TestCases() => new[]
        {
            new object[] { "'Top.Science.*'::lquery", "Top.Science.*" }
        };

        [OneTimeSetUp]
        public async Task SetUp()
        {
            using var conn = await OpenConnectionAsync();
            TestUtil.MinimumPgVersion(conn, "13.0");
            await TestUtil.EnsureExtensionAsync(conn, "ltree");
        }
    }
}
