﻿using System;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using OpenGauss.NET.Types;
using NUnit.Framework;
using static OpenGauss.Tests.TestUtil;
using OpenGauss.NET;

namespace OpenGauss.Tests.Types
{
    /// <summary>
    /// Tests on PostgreSQL text
    /// </summary>
    /// <remarks>
    /// https://www.postgresql.org/docs/current/static/datatype-character.html
    /// </remarks>
    public class TextTests : MultiplexingTestBase
    {
        [Test, Description("Roundtrips a string")]
        public async Task Roundtrip()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p1, @p2, @p3, @p4, @p5, @p6, @p7", conn);
            const string expected = "Something";
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var p1 = new OpenGaussParameter("p1", OpenGaussDbType.Text);
            var p2 = new OpenGaussParameter("p2", OpenGaussDbType.Varchar);
            var p3 = new OpenGaussParameter("p3", DbType.String);
            var p4 = new OpenGaussParameter { ParameterName = "p4", Value = expected };
            var p5 = new OpenGaussParameter("p5", OpenGaussDbType.Text);
            var p6 = new OpenGaussParameter("p6", OpenGaussDbType.Text);
            var p7 = new OpenGaussParameter("p7", OpenGaussDbType.Text);
            Assert.That(p2.DbType, Is.EqualTo(DbType.String));
            Assert.That(p3.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Text));
            Assert.That(p3.DbType, Is.EqualTo(DbType.String));
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            cmd.Parameters.Add(p3);
            cmd.Parameters.Add(p4);
            cmd.Parameters.Add(p5);
            cmd.Parameters.Add(p6);
            cmd.Parameters.Add(p7);
            p1.Value = p2.Value = p3.Value = expected;
            p5.Value = expected.ToCharArray();
            p6.Value = new ArraySegment<char>(("X" + expected).ToCharArray(), 1, expected.Length);
            p7.Value = expectedBytes;
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();

            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                Assert.That(reader.GetFieldType(i), Is.EqualTo(typeof(string)));
                Assert.That(reader.GetString(i), Is.EqualTo(expected));
                Assert.That(reader.GetFieldValue<string>(i), Is.EqualTo(expected));
                Assert.That(reader.GetValue(i), Is.EqualTo(expected));
                Assert.That(reader.GetFieldValue<char[]>(i), Is.EqualTo(expected.ToCharArray()));
                Assert.That(reader.GetFieldValue<byte[]>(i), Is.EqualTo(expectedBytes));
            }
        }

        [Test]
        public async Task Long([Values(CommandBehavior.Default, CommandBehavior.SequentialAccess)] CommandBehavior behavior)
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "name TEXT", out var table);
            var builder = new StringBuilder("ABCDEééé", conn.Settings.WriteBufferSize);
            builder.Append('X', conn.Settings.WriteBufferSize);
            var expected = builder.ToString();
            using (var cmd = new OpenGaussCommand($"INSERT INTO {table} (name) VALUES (@p)", conn))
            {
                cmd.Parameters.Add(new OpenGaussParameter("p", expected));
                await cmd.ExecuteNonQueryAsync();
            }

            using (var cmd = new OpenGaussCommand($"SELECT name, 'foo', name, name, name, name FROM {table}", conn))
            {
                var reader = await cmd.ExecuteReaderAsync(behavior);
                reader.Read();

                var actual = reader[0];
                Assert.That(actual, Is.EqualTo(expected));

                if (behavior.IsSequential())
                    Assert.That(() => reader[0], Throws.Exception.TypeOf<InvalidOperationException>(), "Seek back sequential");
                else
                    Assert.That(reader[0], Is.EqualTo(expected));

                Assert.That(reader.GetString(1), Is.EqualTo("foo"));
                Assert.That(reader.GetFieldValue<string>(2), Is.EqualTo(expected));
                Assert.That(reader.GetValue(3), Is.EqualTo(expected));
                Assert.That(reader.GetFieldValue<string>(4), Is.EqualTo(expected));
                //Assert.That(reader.GetFieldValue<char[]>(5), Is.EqualTo(expected.ToCharArray()));
            }
        }

        [Test, Description("Tests that strings are truncated when the OpenGaussParameter's Size is set")]
        public async Task Truncate()
        {
            const string data = "SomeText";
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p::TEXT", conn);
            var p = new OpenGaussParameter("p", data) { Size = 4 };
            cmd.Parameters.Add(p);
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data.Substring(0, 4)));

            // OpenGaussParameter.Size needs to persist when value is changed
            const string data2 = "AnotherValue";
            p.Value = data2;
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data2.Substring(0, 4)));

            // OpenGaussParameter.Size larger than the value size should mean the value size, as well as 0 and -1
            p.Size = data2.Length + 10;
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data2));
            p.Size = 0;
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data2));
            p.Size = -1;
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data2));

            Assert.That(() => p.Size = -2, Throws.Exception.TypeOf<ArgumentException>());
        }

        //[Test, IssueLink("https://github.com/opengauss/opengauss/issues/488")]
        public async Task Null_character()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p1", conn);
            cmd.Parameters.Add(new OpenGaussParameter("p1", "string with \0\0\0 null \0bytes"));
            Assert.That(async () => await cmd.ExecuteReaderAsync(),
                Throws.Exception.TypeOf<PostgresException>()
                    .With.Property(nameof(PostgresException.SqlState)).EqualTo("22021")
            );
        }

        [Test, Description("Tests some types which are aliased to strings")]
        [TestCase("Varchar")]
        [TestCase("Name")]
        public async Task Aliased_postgres_types(string typename)
        {
            const string expected = "some_text";
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand($"SELECT '{expected}'::{typename}", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetString(0), Is.EqualTo(expected));
            Assert.That(reader.GetFieldValue<char[]>(0), Is.EqualTo(expected.ToCharArray()));
        }


        [Test]
        [TestCase(DbType.AnsiString)]
        [TestCase(DbType.AnsiStringFixedLength)]
        public async Task Aliased_DbTypes(DbType dbType)
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand("SELECT @p", conn);
            command.Parameters.Add(new OpenGaussParameter("p", dbType) { Value = "SomeString" });
            Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo("SomeString"));
        }

        [Test, Description("Tests the PostgreSQL internal \"char\" type")]
        public async Task Internal_char()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = conn.CreateCommand();
            var testArr = new byte[] { (byte)'}', (byte)'"', 3 };
            var testArr2 = new char[] { '}', '"', (char)3 };

            cmd.CommandText = "Select 'a'::\"char\", (-3)::\"char\", :p1, :p2, :p3, :p4, :p5";
            cmd.Parameters.Add(new OpenGaussParameter("p1", OpenGaussDbType.InternalChar) { Value = 'b' });
            cmd.Parameters.Add(new OpenGaussParameter("p2", OpenGaussDbType.InternalChar) { Value = (byte)66 });
            cmd.Parameters.Add(new OpenGaussParameter("p3", OpenGaussDbType.InternalChar) { Value = (byte)230 });
            cmd.Parameters.Add(new OpenGaussParameter("p4", OpenGaussDbType.InternalChar | OpenGaussDbType.Array) { Value = testArr });
            cmd.Parameters.Add(new OpenGaussParameter("p5", OpenGaussDbType.InternalChar | OpenGaussDbType.Array) { Value = testArr2 });
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            var expected = new char[] { 'a', (char)(256 - 3), 'b', (char)66, (char)230 };
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.That(reader.GetChar(i), Is.EqualTo(expected[i]));
            }
            var arr = (char[])reader.GetValue(5);
            var arr2 = (char[])reader.GetValue(6);
            Assert.That(arr.Length, Is.EqualTo(testArr.Length));
            for (var i = 0; i < arr.Length; i++)
            {
                Assert.That(arr[i], Is.EqualTo(testArr[i]));
                Assert.That(arr2[i], Is.EqualTo(testArr2[i]));
            }
        }

        [Test]
        public async Task Char()
        {
            var expected = 'f';
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand("SELECT @p", conn);
            cmd.Parameters.AddWithValue("p", expected);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetChar(0), Is.EqualTo(expected));
            Assert.That(reader.GetString(0), Is.EqualTo(expected.ToString()));
        }

        //[Test, Description("Checks support for the citext contrib type")]
        //[IssueLink("https://github.com/opengauss/opengauss/issues/695")]
        public async Task Citext()
        {
            using var conn = await OpenConnectionAsync();
            await EnsureExtensionAsync(conn, "citext");

            var value = "Foo";
            using (var cmd = new OpenGaussCommand("SELECT @p::CITEXT", conn))
            {
                cmd.Parameters.AddWithValue("p", value);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    reader.Read();
                    Assert.That(reader.GetString(0), Is.EqualTo(value));
                }
            }
            using (var cmd = new OpenGaussCommand("SELECT @p1::CITEXT = @p2::CITEXT", conn))
            {
                cmd.Parameters.AddWithValue("p1", OpenGaussDbType.Citext, "abc");
                cmd.Parameters.AddWithValue("p2", OpenGaussDbType.Citext, "ABC");
                Assert.That(await cmd.ExecuteScalarAsync(), Is.True);
            }
        }

        [Test]
        public async Task Xml()
        {
            await using var conn = await OpenConnectionAsync();
            await IgnoreIfFeatureNotSupportedAsync(conn, "SELECT '<x>t</x>'::xml::text");

            using var cmd = new OpenGaussCommand("SELECT @p1, @p2", conn);
            const string expected = "<root>foo</root>";
            var p1 = new OpenGaussParameter("p1", OpenGaussDbType.Xml);
            var p2 = new OpenGaussParameter("p2", DbType.Xml);
            Assert.That(p1.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Xml));
            Assert.That(p2.DbType, Is.EqualTo(DbType.Xml));
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            p1.Value = p2.Value = expected;
            await using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();

            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                Assert.That(reader.GetFieldType(i), Is.EqualTo(typeof(string)));
                Assert.That(reader.GetDataTypeName(i), Is.EqualTo("xml"));
                Assert.That(reader.GetString(i), Is.EqualTo(expected));
                Assert.That(reader.GetFieldValue<string>(i), Is.EqualTo(expected));
                Assert.That(reader.GetValue(i), Is.EqualTo(expected));
            }
        }

        public TextTests(MultiplexingMode multiplexingMode) : base(multiplexingMode) {}
    }
}
