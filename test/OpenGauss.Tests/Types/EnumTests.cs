﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenGauss.NET.NameTranslation;
using OpenGauss.NET;
using OpenGauss.NET.Types;
using NUnit.Framework;
using static OpenGauss.NET.Util.Statics;
using static OpenGauss.Tests.TestUtil;
using OpenGauss.NET.PostgresTypes;

namespace OpenGauss.Tests.Types
{
    [NonParallelizable]
    public class EnumTests : TestBase
    {
        enum Mood { Sad, Ok, Happy }

        [PgName("explicitly_named_mood")]
        enum MoodUnmapped { Sad, Ok, Happy };

        [Test]
        public async Task Unmapped_enum()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Unmapped_enum),
                Pooling = false
            };
            using var conn = await OpenConnectionAsync(csb);
            await using var _ = await GetTempTypeName(conn, out var type);
            await conn.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')");
            conn.ReloadTypes();

            using var cmd = new OpenGaussCommand("SELECT @scalar1, @scalar2, @scalar3, @scalar4", conn);
            cmd.Parameters.Add(new OpenGaussParameter
            {
                ParameterName = "scalar1",
                Value = Mood.Happy,
                DataTypeName = type
            });
            cmd.Parameters.Add(new OpenGaussParameter
            {
                ParameterName = "scalar2",
                Value = "happy",
                DataTypeName = type
            });
            cmd.Parameters.Add(new OpenGaussParameter<Mood>
            {
                ParameterName = "scalar3",
                TypedValue = Mood.Happy,
                DataTypeName = type
            });
            cmd.Parameters.Add(new OpenGaussParameter<string>
            {
                ParameterName = "scalar4",
                TypedValue = "happy",
                DataTypeName = type
            });
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();

            for (var i = 0; i < 4; i++)
            {
                Assert.That(reader.GetDataTypeName(i), Is.EqualTo($"{conn.UserName}.{type}"));
                Assert.That(reader.GetFieldValue<Mood>(i), Is.EqualTo(Mood.Happy));
                Assert.That(reader.GetFieldValue<string>(i), Is.EqualTo("happy"));
                Assert.That(reader.GetValue(i), Is.EqualTo("happy"));
            }
        }

        [Test, Description("Resolves an enum type handler via the different pathways, with global mapping")]
        public async Task Enum_resolution_with_global_mapping()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Enum_resolution_with_global_mapping),  // Prevent backend type caching in TypeHandlerRegistry
                Pooling = false
            };

            using var conn = await OpenConnectionAsync(csb);
            await using var _ = await GetTempTypeName(conn, out var type);
            await conn.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')");
            OpenGaussConnection.GlobalTypeMapper.MapEnum<Mood>(type);
            try
            {
                conn.ReloadTypes();

                // Resolve type by DataTypeName
                using (var cmd = new OpenGaussCommand("SELECT @p", conn))
                {
                    cmd.Parameters.Add(new OpenGaussParameter
                    {
                        ParameterName = "p",
                        DataTypeName = type,
                        Value = DBNull.Value
                    });
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        reader.Read();
                        Assert.That(reader.GetDataTypeName(0), Is.EqualTo($"{conn.UserName}.{type}"));
                        Assert.That(reader.IsDBNull(0), Is.True);
                    }
                }

                // Resolve type by ClrType (type inference)
                conn.ReloadTypes();
                using (var cmd = new OpenGaussCommand("SELECT @p", conn))
                {
                    cmd.Parameters.Add(new OpenGaussParameter { ParameterName = "p", Value = Mood.Ok });
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        reader.Read();
                        Assert.That(reader.GetDataTypeName(0), Is.EqualTo($"{conn.UserName}.{type}"));
                    }
                }

                // Resolve type by OID (read)
                conn.ReloadTypes();
                using (var cmd = new OpenGaussCommand($"SELECT 'happy'::{type}", conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    reader.Read();
                    Assert.That(reader.GetDataTypeName(0), Is.EqualTo($"{conn.UserName}.{type}"));
                }
            }
            finally
            {
                OpenGaussConnection.GlobalTypeMapper.UnmapEnum<Mood>(type);
            }
        }

        [Test, Description("Resolves an enum type handler via the different pathways, with late mapping")]
        public async Task Enum_resolution_with_late_mapping()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Enum_resolution_with_late_mapping),  // Prevent backend type caching in TypeHandlerRegistry
                Pooling = false
            };

            using var conn = await OpenConnectionAsync(csb);
            await using var _ = await GetTempTypeName(conn, out var type);
            await conn.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')");

            // Resolve type by OpenGaussDbType
            conn.ReloadTypes();
            conn.TypeMapper.MapEnum<Mood>(type);
            using (var cmd = new OpenGaussCommand("SELECT @p", conn))
            {
                cmd.Parameters.Add(new OpenGaussParameter
                {
                    ParameterName = "p",
                    DataTypeName = type,
                    Value = DBNull.Value
                });
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    reader.Read();
                    Assert.That(reader.GetDataTypeName(0), Is.EqualTo($"{conn.UserName}.{type}"));
                    Assert.That(reader.IsDBNull(0), Is.True);
                }
            }

            // Resolve type by ClrType (type inference)
            conn.ReloadTypes();
            conn.TypeMapper.MapEnum<Mood>(type);
            using (var cmd = new OpenGaussCommand("SELECT @p", conn))
            {
                cmd.Parameters.Add(new OpenGaussParameter { ParameterName = "p", Value = Mood.Ok });
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    reader.Read();
                    Assert.That(reader.GetDataTypeName(0), Is.EqualTo($"{conn.UserName}.{type}"));
                }
            }

            // Resolve type by OID (read)
            conn.ReloadTypes();
            conn.TypeMapper.MapEnum<Mood>(type);
            using (var cmd = new OpenGaussCommand($"SELECT 'happy'::{type}", conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                reader.Read();
                Assert.That(reader.GetDataTypeName(0), Is.EqualTo($"{conn.UserName}.{type}"));
            }
        }

        [Test]
        public async Task Late_mapping()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTypeName(conn, out var type);
            await conn.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')");
            conn.ReloadTypes();
            conn.TypeMapper.MapEnum<Mood>(type);
            const Mood expected = Mood.Ok;
            var cmd = new OpenGaussCommand($"SELECT @p1::{type}, @p2::{type}", conn);
            var p1 = new OpenGaussParameter
            {
                ParameterName = "p1",
                DataTypeName = type,
                Value = expected
            };
            var p2 = new OpenGaussParameter { ParameterName = "p2", Value = expected };
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            var reader = await cmd.ExecuteReaderAsync();
            reader.Read();

            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                Assert.That(reader.GetFieldType(i), Is.EqualTo(typeof(Mood)));
                Assert.That(reader.GetFieldValue<Mood>(i), Is.EqualTo(expected));
                Assert.That(reader.GetValue(i), Is.EqualTo(expected));
            }
        }

        [Test]
        public async Task Dual_enums()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTypeName(conn, out var type1);
            await using var __ = await GetTempTypeName(conn, out var type2);
            await conn.ExecuteNonQueryAsync($@"
CREATE TYPE {type1} AS ENUM ('sad', 'ok', 'happy');
CREATE TYPE {type2} AS ENUM ('label1', 'label2', 'label3')");
            conn.ReloadTypes();
            conn.TypeMapper.MapEnum<Mood>(type1);
            conn.TypeMapper.MapEnum<TestEnum>(type2);
            var cmd = new OpenGaussCommand("SELECT @p1", conn);
            var expected = new[] { Mood.Ok, Mood.Sad };
            var p = new OpenGaussParameter
            {
                ParameterName = "p1",
                DataTypeName = $"{type1}[]",
                Value = expected
            };
            cmd.Parameters.Add(p);
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public async Task Global_mapping()
        {
            using var adminConn = await OpenConnectionAsync();
            await using var _ = await GetTempTypeName(adminConn, out var type);

            using (var conn = await OpenConnectionAsync())
            {
                await conn.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')");
                OpenGaussConnection.GlobalTypeMapper.MapEnum<Mood>(type);
                conn.ReloadTypes();
                const Mood expected = Mood.Ok;
                using (var cmd = new OpenGaussCommand($"SELECT @p::{type}", conn))
                {
                    var p = new OpenGaussParameter { ParameterName = "p", Value = expected };
                    cmd.Parameters.Add(p);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        reader.Read();

                        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(Mood)));
                        Assert.That(reader.GetFieldValue<Mood>(0), Is.EqualTo(expected));
                        Assert.That(reader.GetValue(0), Is.EqualTo(expected));
                    }
                }
            }

            // Unmap
            OpenGaussConnection.GlobalTypeMapper.UnmapEnum<Mood>(type);

            using (var conn = await OpenConnectionAsync())
            {
                // Enum should have been unmapped and so will return as text
                Assert.That(await conn.ExecuteScalarAsync($"SELECT 'ok'::{type}"), Is.EqualTo("ok"));
            }
        }

        [Test]
        public async Task Global_mapping_when_type_not_found()
        {
            using var conn = await OpenConnectionAsync();
            OpenGaussConnection.GlobalTypeMapper.MapEnum<Mood>("unknown_enum");
            try
            {
                Assert.That(conn.ReloadTypes, Throws.Nothing);
            }
            finally
            {
                OpenGaussConnection.GlobalTypeMapper.UnmapEnum<Mood>("unknown_enum");
            }
        }

        [Test]
        public async Task Array()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTypeName(conn, out var type);
            await conn.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')");
            conn.ReloadTypes();
            conn.TypeMapper.MapEnum<Mood>(type);
            var expected = new[] { Mood.Ok, Mood.Happy };
            using var cmd = new OpenGaussCommand($"SELECT @p1::{type}[], @p2::{type}[]", conn);
            var p1 = new OpenGaussParameter
            {
                ParameterName = "p1",
                DataTypeName = $"{type}[]",
                Value = expected
            };
            var p2 = new OpenGaussParameter { ParameterName = "p2", Value = expected };
            cmd.Parameters.Add(p1);
            cmd.Parameters.Add(p2);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();

            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                Assert.That(reader.GetFieldType(i), Is.EqualTo(typeof(Array)));
                Assert.That(reader.GetFieldValue<Mood[]>(i), Is.EqualTo(expected));
                Assert.That(reader.GetValue(i), Is.EqualTo(expected));
            }
        }

        [Test]
        public async Task Read_unmapped_enum_as_string()
        {
            using var conn = new OpenGaussConnection(ConnectionString);
            conn.Open();
            await using var _ = await GetTempTypeName(conn, out var type);

            await conn.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('Sad', 'Ok', 'Happy')");
            conn.ReloadTypes();
            using var cmd = new OpenGaussCommand($"SELECT 'Sad'::{type}, ARRAY['Ok', 'Happy']::{type}[]", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader[0], Is.EqualTo("Sad"));
            Assert.That(reader.GetDataTypeName(0), Is.EqualTo($"{conn.UserName}.{type}"));
            Assert.That(reader[1], Is.EqualTo(new[] { "Ok", "Happy" }));
        }

        [Test, Description("Test that a c# string can be written to a backend enum when DbType is unknown")]
        public async Task Write_string_to_backend_enum()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTypeName(conn, out var type);
            await using var __ = await GetTempTableName(conn, out var table);
            await conn.ExecuteNonQueryAsync($@"
CREATE TYPE {type} AS ENUM ('Banana', 'Apple', 'Orange');
CREATE TABLE {table} (id SERIAL, value1 {type}, value2 {type});");
            conn.ReloadTypes();
            const string expected = "Banana";
            using var cmd = new OpenGaussCommand($"INSERT INTO {table} (id, value1, value2) VALUES (default, @p1, @p2);", conn);
            cmd.Parameters.AddWithValue("p2", OpenGaussDbType.Unknown, expected);
            var p2 = new OpenGaussParameter("p1", OpenGaussDbType.Unknown) { Value = expected };
            cmd.Parameters.Add(p2);
            cmd.ExecuteNonQuery();
        }

        [Test]
        public async Task Write_unmapped_denum()
        {
            await using var conn = await OpenConnectionAsync();
            await using var _ = DeferAsync(() => conn.ExecuteNonQueryAsync("DROP TYPE IF EXISTS explicitly_named_mood"));
            await conn.ExecuteNonQueryAsync($"CREATE TYPE explicitly_named_mood AS ENUM ('sad', 'ok', 'happy')");

            conn.ReloadTypes();

            await using var cmd = new OpenGaussCommand($"SELECT @p::text", conn)
            {
                Parameters = { new("p", MoodUnmapped.Happy) }
            };

            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();

            Assert.That(reader.GetFieldValue<string>(0), Is.EqualTo("happy"));
        }

        [Test, Description("Tests that a a C# enum an be written to an enum backend when passed as dbUnknown")]
        public async Task Write_enum_as_OpenGaussDbType_Unknown()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTypeName(conn, out var type);
            await using var __ = await GetTempTableName(conn, out var table);
            await conn.ExecuteNonQueryAsync($@"
CREATE TYPE {type} AS ENUM ('Sad', 'Ok', 'Happy');
CREATE TABLE {table} (value1 {type})");
            conn.ReloadTypes();
            var expected = Mood.Happy;
            using var cmd = new OpenGaussCommand($"INSERT INTO {table} (value1) VALUES (@p1);", conn);
            cmd.Parameters.AddWithValue("p1", OpenGaussDbType.Unknown, expected);
            cmd.ExecuteNonQuery();
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/859")]
        public async Task Name_translation_default_snake_case()
        {
            // Per-connection mapping
            using (var conn = await OpenConnectionAsync())
            await using (var _ = await GetTempTypeName(conn, out var enumName1))
            {
                await conn.ExecuteNonQueryAsync($"CREATE TYPE {enumName1} AS ENUM ('simple', 'two_words', 'some_database_name')");
                conn.ReloadTypes();
                conn.TypeMapper.MapEnum<NameTranslationEnum>(enumName1);
                using (var cmd = new OpenGaussCommand("SELECT @p1, @p2, @p3", conn))
                {
                    cmd.Parameters.AddWithValue("p1", NameTranslationEnum.Simple);
                    cmd.Parameters.AddWithValue("p2", NameTranslationEnum.TwoWords);
                    cmd.Parameters.AddWithValue("p3", NameTranslationEnum.SomeClrName);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        reader.Read();
                        Assert.That(actual: reader.GetFieldValue<NameTranslationEnum>(0), Is.EqualTo(NameTranslationEnum.Simple));
                        Assert.That(reader.GetFieldValue<NameTranslationEnum>(1), Is.EqualTo(NameTranslationEnum.TwoWords));
                        Assert.That(reader.GetFieldValue<NameTranslationEnum>(2), Is.EqualTo(NameTranslationEnum.SomeClrName));
                    }
                }
            }

            // Global mapping
            using var dropConn = await OpenConnectionAsync();
            await using var __ = await GetTempTypeName(dropConn, out var enumName2);
            OpenGaussConnection.GlobalTypeMapper.MapEnum<NameTranslationEnum>(enumName2);
            try
            {
                using var conn = await OpenConnectionAsync();
                await conn.ExecuteNonQueryAsync($"CREATE TYPE {enumName2} AS ENUM ('simple', 'two_words', 'some_database_name')");
                conn.ReloadTypes();
                using var cmd = new OpenGaussCommand("SELECT @p1, @p2, @p3", conn);
                cmd.Parameters.AddWithValue("p1", NameTranslationEnum.Simple);
                cmd.Parameters.AddWithValue("p2", NameTranslationEnum.TwoWords);
                cmd.Parameters.AddWithValue("p3", NameTranslationEnum.SomeClrName);
                using var reader = await cmd.ExecuteReaderAsync();
                reader.Read();
                Assert.That(reader.GetFieldValue<NameTranslationEnum>(0), Is.EqualTo(NameTranslationEnum.Simple));
                Assert.That(reader.GetFieldValue<NameTranslationEnum>(1), Is.EqualTo(NameTranslationEnum.TwoWords));
                Assert.That(reader.GetFieldValue<NameTranslationEnum>(2), Is.EqualTo(NameTranslationEnum.SomeClrName));
            }
            finally
            {
                OpenGaussConnection.GlobalTypeMapper.UnmapEnum<NameTranslationEnum>();
            }
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/859")]
        public async Task Name_translation_null()
        {
            // Per-connection mapping
            using var conn = await OpenConnectionAsync();
            await conn.ExecuteNonQueryAsync(@"CREATE TYPE pg_temp.""NameTranslationEnum"" AS ENUM ('Simple', 'TwoWords', 'some_database_name')");
            conn.ReloadTypes();
            conn.TypeMapper.MapEnum<NameTranslationEnum>(nameTranslator: new OpenGaussNullNameTranslator());
            using var cmd = new OpenGaussCommand("SELECT @p1, @p2, @p3", conn);
            cmd.Parameters.AddWithValue("p1", NameTranslationEnum.Simple);
            cmd.Parameters.AddWithValue("p2", NameTranslationEnum.TwoWords);
            cmd.Parameters.AddWithValue("p3", NameTranslationEnum.SomeClrName);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetFieldValue<NameTranslationEnum>(0), Is.EqualTo(NameTranslationEnum.Simple));
            Assert.That(reader.GetFieldValue<NameTranslationEnum>(1),
                Is.EqualTo(NameTranslationEnum.TwoWords));
            Assert.That(reader.GetFieldValue<NameTranslationEnum>(2),
                Is.EqualTo(NameTranslationEnum.SomeClrName));
        }

        enum NameTranslationEnum
        {
            Simple,
            TwoWords,
            [PgName("some_database_name")]
            SomeClrName
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/632")]
        public async Task Schemas()
        {
            using var adminConn = await OpenConnectionAsync();
            await using var _ = await CreateTempSchema(adminConn, out var schema1);
            await using var __ = await CreateTempSchema(adminConn, out var schema2);

            try
            {
                using (var conn = await OpenConnectionAsync())
                {
                    await conn.ExecuteNonQueryAsync($@"
CREATE TYPE {schema1}.my_enum AS ENUM ('one');
CREATE TYPE {schema2}.my_enum AS ENUM ('alpha');");
                    conn.ReloadTypes();
                    conn.TypeMapper
                        .MapEnum<Enum1>($"{schema1}.my_enum")
                        .MapEnum<Enum2>($"{schema2}.my_enum");
                    using (var cmd = new OpenGaussCommand("SELECT @p1, @p2", conn))
                    {
                        cmd.Parameters.AddWithValue("p1", Enum1.One);
                        cmd.Parameters.AddWithValue("p2", Enum2.Alpha);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            reader.Read();
                            Assert.That(reader[0], Is.EqualTo(Enum1.One));
                            Assert.That(reader.GetDataTypeName(0), Is.EqualTo($"{schema1}.my_enum"));
                            Assert.That(reader[1], Is.EqualTo(Enum2.Alpha));
                            Assert.That(reader.GetDataTypeName(1), Is.EqualTo($"{schema2}.my_enum"));
                        }
                    }
                }

                // Global mapping
                OpenGaussConnection.GlobalTypeMapper.MapEnum<Enum1>($"{schema1}.my_enum");
                OpenGaussConnection.GlobalTypeMapper.MapEnum<Enum2>($"{schema2}.my_enum");
                using (var conn = await OpenConnectionAsync())
                {
                    using (var cmd = new OpenGaussCommand("SELECT @p1, @p2", conn))
                    {
                        cmd.Parameters.AddWithValue("p1", Enum1.One);
                        cmd.Parameters.AddWithValue("p2", Enum2.Alpha);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            reader.Read();
                            Assert.That(reader[0], Is.EqualTo(Enum1.One));
                            Assert.That(reader.GetDataTypeName(0), Is.EqualTo($"{schema1}.my_enum"));
                            Assert.That(reader[1], Is.EqualTo(Enum2.Alpha));
                            Assert.That(reader.GetDataTypeName(1), Is.EqualTo($"{schema2}.my_enum"));
                        }
                    }
                }
            }
            finally
            {
                OpenGaussConnection.GlobalTypeMapper.UnmapEnum<Enum1>($"{schema1}.my_enum");
                OpenGaussConnection.GlobalTypeMapper.UnmapEnum<Enum2>($"{schema2}.my_enum");
            }
        }

        enum Enum1 { One }
        enum Enum2 { Alpha }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1017")]
        public async Task Global_mappings_and_pooling()
        {
            using var adminConn = await OpenConnectionAsync();
            using var _ = CreateTempPool(ConnectionString, out var connectionString);
            await using var __ = await GetTempTypeName(adminConn, out var type);

            int serverId;
            using (var conn = await OpenConnectionAsync(connectionString))
            {
                serverId = conn.ProcessID;
                await conn.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')");
                conn.ReloadTypes();
            }
            // At this point the backend type for the enum is loaded, but no global mapping
            // has been made. Reopening the same pooled connector should learn about the new
            // global mapping
            OpenGaussConnection.GlobalTypeMapper.MapEnum<Mood>(type);
            try
            {
                using var conn = await OpenConnectionAsync(connectionString);
                Assert.That(conn.ProcessID, Is.EqualTo(serverId));
                Assert.That(await conn.ExecuteScalarAsync($"SELECT 'sad'::{type}"), Is.EqualTo(Mood.Sad));
            }
            finally
            {
                OpenGaussConnection.GlobalTypeMapper.UnmapEnum<Mood>("mood1");
            }
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1779")]
        public async Task GetPostgresType()
        {
            using var _ = CreateTempPool(ConnectionString, out var connectionString);
            using var conn = await OpenConnectionAsync(connectionString);
            await using var __ = await GetTempTypeName(conn, out var type);
            await conn.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')");
            conn.ReloadTypes();

            using var cmd = new OpenGaussCommand($"SELECT 'ok'::{type}", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            var enumType = (PostgresEnumType)reader.GetPostgresType(0);
            Assert.That(enumType.Name, Is.EqualTo(type));
            Assert.That(enumType.Labels, Is.EqualTo(new List<string> { "sad", "ok", "happy" }));
        }

        enum TestEnum
        {
            label1,
            label2,
            [PgName("label3")]
            Label3
        }
    }
}
