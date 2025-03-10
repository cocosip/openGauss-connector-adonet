﻿using System;
using System.Data;
using System.Dynamic;
using System.Linq;
using OpenGauss.NET;
using OpenGauss.NET.Types;
using NUnit.Framework;
using OpenGauss.NET.PostgresTypes;

namespace OpenGauss.Tests.Types
{
    [NonParallelizable]
    public class CompositeTests : TestBase
    {
        #region Test Types

#pragma warning disable CS8618
        record SomeComposite
        {
            public int X { get; set; }
            public string SomeText { get; set; }
        }

        class SomeCompositeContainer
        {
            public int A { get; set; }
            public SomeComposite Contained { get; set; }
        }

        struct CompositeStruct
        {
            public int X { get; set; }
            public string SomeText { get; set; }
        }
#pragma warning restore CS8618

        #endregion

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1779")]
        public void Composite()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(PostgresType),
                Pooling = false
            };
            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.comp1 AS (x int, some_text text)");
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.comp2 AS (comp comp1, comps comp1[])");
            conn.ReloadTypes();

            using var cmd = new OpenGaussCommand("SELECT ROW(ROW(8, 'foo')::comp1, ARRAY[ROW(9, 'bar')::comp1, ROW(10, 'baz')::comp1])::comp2", conn);
            using var reader = cmd.ExecuteReader();
            reader.Read();
            var comp2Type = (PostgresCompositeType)reader.GetPostgresType(0);
            Assert.That(comp2Type.Name, Is.EqualTo("comp2"));
            Assert.That(comp2Type.FullName, Does.StartWith("pg_temp_") & Does.EndWith(".comp2"));
            Assert.That(comp2Type.Fields, Has.Count.EqualTo(2));
            var field1 = comp2Type.Fields[0];
            var field2 = comp2Type.Fields[1];
            Assert.That(field1.Name, Is.EqualTo("comp"));
            Assert.That(field2.Name, Is.EqualTo("comps"));
            var comp1Type = (PostgresCompositeType)field1.Type;
            Assert.That(comp1Type.Name, Is.EqualTo("comp1"));
            var arrType = (PostgresArrayType)field2.Type;
            Assert.That(arrType.Name, Is.EqualTo("comp1[]"));
            var elemType = arrType.Element;
            Assert.That(elemType, Is.SameAs(comp1Type));
        }

        [Test, Description("Resolves an enum type handler via the different pathways, with global mapping")]
        public void Composite_type_resolution_with_global_mapping()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Composite_type_resolution_with_global_mapping),  // Prevent backend type caching in TypeHandlerRegistry
                Pooling = false
            };

            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.composite1 AS (x int, some_text text)");
            OpenGaussConnection.GlobalTypeMapper.MapComposite<SomeComposite>("composite1");
            try
            {
                conn.ReloadTypes();

                // Resolve type by DataTypeName
                using (var cmd = new OpenGaussCommand("SELECT @p", conn))
                {
                    cmd.Parameters.Add(new OpenGaussParameter
                    {
                        ParameterName = "p",
                        DataTypeName = "composite1",
                        Value = DBNull.Value
                    });
                    using (var reader = cmd.ExecuteReader())
                    {
                        reader.Read();
                        Assert.That(reader.GetDataTypeName(0), Does.StartWith("pg_temp").And.EndWith(".composite1"));
                        Assert.That(reader.IsDBNull(0), Is.True);
                    }
                }

                // Resolve type by ClrType (type inference)
                conn.ReloadTypes();
                using (var cmd = new OpenGaussCommand("SELECT @p", conn))
                {
                    cmd.Parameters.Add(new OpenGaussParameter { ParameterName = "p", Value = new SomeComposite { X = 8, SomeText = "foo" } });
                    using (var reader = cmd.ExecuteReader())
                    {
                        reader.Read();
                        Assert.That(reader.GetDataTypeName(0), Does.StartWith("pg_temp").And.EndWith(".composite1"));
                    }
                }

                // Resolve type by OID (read)
                conn.ReloadTypes();
                using (var cmd = new OpenGaussCommand("SELECT ROW(1, 'foo')::COMPOSITE1", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    reader.Read();
                    Assert.That(reader.GetDataTypeName(0), Does.StartWith("pg_temp").And.EndWith(".composite1"));
                    Assert.That(reader.GetFieldValue<SomeComposite>(0), Is.EqualTo(new SomeComposite { X = 1, SomeText = "foo" }));
                }
            }
            finally
            {
                OpenGaussConnection.GlobalTypeMapper.UnmapComposite<SomeComposite>("composite1");
            }
        }

        [Test, Description("Resolves a composite type handler via the different pathways, with late mapping")]
        public void Composite_type_resolution_with_late_mapping()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Composite_type_resolution_with_late_mapping),  // Prevent backend type caching in TypeHandlerRegistry
                Pooling = false
            };

            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.composite2 AS (x int, some_text text)");
            // Resolve type by OpenGaussDbType
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<SomeComposite>("composite2");
            using (var cmd = new OpenGaussCommand("SELECT @p", conn))
            {
                cmd.Parameters.Add(new OpenGaussParameter
                {
                    ParameterName = "p",
                    DataTypeName = "composite2",
                    Value = DBNull.Value
                });

                using (var reader = cmd.ExecuteReader())
                {
                    reader.Read();
                    Assert.That(reader.GetDataTypeName(0), Does.StartWith("pg_temp").And.EndWith(".composite2"));
                    Assert.That(reader.IsDBNull(0), Is.True);
                }
            }

            // Resolve type by ClrType (type inference)
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<SomeComposite>("composite2");
            using (var cmd = new OpenGaussCommand("SELECT @p", conn))
            {
                cmd.Parameters.Add(new OpenGaussParameter { ParameterName = "p", Value = new SomeComposite { X = 8, SomeText = "foo" } });
                using (var reader = cmd.ExecuteReader())
                {
                    reader.Read();
                    Assert.That(reader.GetDataTypeName(0), Does.StartWith("pg_temp").And.EndWith(".composite2"));
                }
            }

            // Resolve type by OID (read)
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<SomeComposite>("composite2");
            using (var cmd = new OpenGaussCommand("SELECT ROW(1, 'foo')::COMPOSITE2", conn))
            using (var reader = cmd.ExecuteReader())
            {
                reader.Read();
                Assert.That(reader.GetDataTypeName(0), Does.StartWith("pg_temp").And.EndWith(".composite2"));
            }
        }

        [Test, Parallelizable(ParallelScope.None)]
        public void Late_mapping()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Late_mapping),
                Pooling = false
            };
            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.composite3 AS (x int, some_text text)");
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<SomeComposite>("composite3");

            var expected = new SomeComposite { X = 8, SomeText = "foo" };
            using var cmd = new OpenGaussCommand("SELECT @p1::composite3, @p2::composite3", conn);
            cmd.Parameters.Add(new OpenGaussParameter
            {
                ParameterName = "p1",
                DataTypeName = "composite3",
                Value = expected
            });
            cmd.Parameters.AddWithValue("p2", expected);
            using var reader = cmd.ExecuteReader();
            reader.Read();
            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                var actual = reader.GetFieldValue<SomeComposite>(i);
                Assert.That(actual.X, Is.EqualTo(8));
                Assert.That(actual.SomeText, Is.EqualTo("foo"));
            }
        }

        [Test]
        public void Global_mapping()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Late_mapping),
                Pooling = false
            };
            try
            {
                using (var conn = OpenConnection(csb))
                {
                    conn.ExecuteNonQuery("DROP TYPE IF EXISTS composite4");
                    conn.ExecuteNonQuery("CREATE TYPE composite4 AS (x int, some_text text)");
                    OpenGaussConnection.GlobalTypeMapper.MapComposite<SomeComposite>("composite4");
                    conn.ReloadTypes();

                    var expected = new SomeComposite { X = 8, SomeText = "foo" };
                    using (var cmd = new OpenGaussCommand($"SELECT @p::composite4", conn))
                    {
                        cmd.Parameters.AddWithValue("p", expected);
                        using (var reader = cmd.ExecuteReader())
                        {
                            reader.Read();
                            var actual = reader.GetFieldValue<SomeComposite>(0);
                            Assert.That(actual.X, Is.EqualTo(8));
                            Assert.That(actual.SomeText, Is.EqualTo("foo"));
                        }
                    }
                }

                // Unmap
                OpenGaussConnection.GlobalTypeMapper.UnmapComposite<SomeComposite>("composite4");

                using (var conn = OpenConnection(csb))
                {
                    Assert.That(() => conn.ExecuteScalar("SELECT '(8, \"foo\")'::composite4"), Throws.TypeOf<NotSupportedException>());
                }
            }
            finally
            {
                using var conn = OpenConnection(csb);
                conn.ExecuteNonQuery("DROP TYPE IF EXISTS composite4");
            }
        }

        [Test, Description("Tests a composite within another composite")]
        public void Recursive()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Recursive),
                Pooling = false
            };
            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.composite_contained AS (x int, some_text text)");
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.composite_container AS (a int, contained composite_contained)");
            conn.ReloadTypes();
            // Registration in inverse dependency order should work
            conn.TypeMapper.MapComposite<SomeCompositeContainer>("composite_container");
            conn.TypeMapper.MapComposite<SomeComposite>("composite_contained");

            var expected = new SomeCompositeContainer
            {
                A = 4,
                Contained = new SomeComposite { X = 8, SomeText = "foo" }
            };

            using var cmd = new OpenGaussCommand("SELECT @p::composite_container", conn);
            cmd.Parameters.AddWithValue("p", expected);
            using var reader = cmd.ExecuteReader();
            reader.Read();
            var actual = reader.GetFieldValue<SomeCompositeContainer>(0);
            Assert.That(actual.A, Is.EqualTo(4));
            Assert.That(actual.Contained.X, Is.EqualTo(8));
            Assert.That(actual.Contained.SomeText, Is.EqualTo("foo"));
        }

        [Test]
        public void Struct()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Struct),
                Pooling = false
            };
            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.composite_struct AS (x int, some_text text)");
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<CompositeStruct>("composite_struct");

            var expected = new CompositeStruct { X = 8, SomeText = "foo" };
            using var cmd = new OpenGaussCommand("SELECT @p::composite_struct", conn);
            cmd.Parameters.AddWithValue("p", expected);
            using var reader = cmd.ExecuteReader();
            reader.Read();
            var actual = reader.GetFieldValue<CompositeStruct>(0);
            Assert.That(actual.X, Is.EqualTo(8));
            Assert.That(actual.SomeText, Is.EqualTo("foo"));
        }

        [Test]
        public void Array()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Array),
                Pooling = false
            };
            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.composite5 AS (x int, some_text text)");
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<SomeComposite>("composite5");

            var expected = new[] {
                new SomeComposite {X = 8, SomeText = "foo"},
                new SomeComposite {X = 9, SomeText = "bar"}
            };

            using var cmd = new OpenGaussCommand("SELECT @p1::composite5[], @p2::composite5[]", conn);
            cmd.Parameters.Add(new OpenGaussParameter
            {
                ParameterName = "p1",
                DataTypeName = "composite5[]",
                Value = expected
            });
            cmd.Parameters.AddWithValue("p2", expected); // Infer
            using var reader = cmd.ExecuteReader();
            reader.Read();
            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                var actual = reader.GetFieldValue<SomeComposite[]>(i);
                Assert.That(actual[0].X, Is.EqualTo(expected[0].X));
                Assert.That(actual[0].SomeText, Is.EqualTo(expected[0].SomeText));
                Assert.That(actual[1].X, Is.EqualTo(expected[1].X));
                Assert.That(actual[1].SomeText, Is.EqualTo(expected[1].SomeText));
            }
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/859")]
        public void Name_translation()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Late_mapping),
                Pooling = false
            };
            var expected = new NameTranslationComposite { Simple = 2, TwoWords = 3, SomeClrName = 4 };
            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.name_translation_composite AS (simple int, two_words int, some_database_name int)");
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<NameTranslationComposite>();

            using var cmd = new OpenGaussCommand("SELECT @p", conn);
            cmd.Parameters.AddWithValue("p", expected);
            var actual = (NameTranslationComposite)cmd.ExecuteScalar()!;
            Assert.That(actual.Simple, Is.EqualTo(expected.Simple));
            Assert.That(actual.TwoWords, Is.EqualTo(expected.TwoWords));
            Assert.That(actual.SomeClrName, Is.EqualTo(expected.SomeClrName));
        }

        class NameTranslationComposite
        {
            public int Simple { get; set; }
            public int TwoWords { get; set; }
            [PgName("some_database_name")]
            public int SomeClrName { get; set; }
        }

        //[Test, IssueLink("https://github.com/opengauss/opengauss/issues/856")]
        public void Domain()
        {
            var setupSql = @"SET search_path=pg_temp;

CREATE DOMAIN us_postal_code AS TEXT
CHECK
(
    VALUE ~ '^\d{5}$'
    OR VALUE ~ '^\d{5}-\d{4}$'
);

CREATE TYPE address AS
(
    street TEXT,
    postal_code us_postal_code
)";
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(Domain),
                Pooling = false
            };
            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery(setupSql);
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<Address>();

            var expected = new Address { PostalCode = "12345", Street = "Main St." };
            using var cmd = new OpenGaussCommand(@"SELECT @p", conn);
            cmd.Parameters.Add(new OpenGaussParameter { ParameterName = "p", Value = expected });
            using var reader = cmd.ExecuteReader();
            reader.Read();
            var actual = reader.GetFieldValue<Address>(0);
            Assert.That(actual.Street, Is.EqualTo(expected.Street));
            Assert.That(actual.PostalCode, Is.EqualTo(expected.PostalCode));
        }

        public class Address
        {
            public string Street { get; set; } = default!;
            public string PostalCode { get; set; } = default!;
        }

        class TableAsCompositeType
        {
            public int Foo { get; set; }
        }

        #region Table as Composite

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/990")]
        public void Table_as_composite_not_supported_by_default()
        {
            using var conn = OpenConnection();
            conn.ExecuteNonQuery("CREATE TEMP TABLE table_as_composite (foo int); INSERT INTO table_as_composite (foo) VALUES (8)");
            conn.ReloadTypes();
            Assert.That(() => conn.TypeMapper.MapComposite<TableAsCompositeType>("table_as_composite"), Throws.Exception.TypeOf<ArgumentException>());
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/990")]
        public void Table_as_composite()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                Pooling = false,
                ApplicationName = nameof(Table_as_composite),
                LoadTableComposites = true
            };
            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery(
                "CREATE TEMP TABLE table_as_composite (foo int);" +
                "INSERT INTO table_as_composite (foo) VALUES (8)");
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<TableAsCompositeType>("table_as_composite");
            var value = (TableAsCompositeType)conn.ExecuteScalar(@"SELECT t.*::table_as_composite FROM table_as_composite AS t")!;
            Assert.That(value.Foo, Is.EqualTo(8));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1267")]
        public void Table_as_composite_with_delete_columns()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                Pooling = false,
                ApplicationName = nameof(Table_as_composite_with_delete_columns),
                LoadTableComposites = true
            };

            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery(@"
                    CREATE TEMP TABLE table_as_composite2 (foo int, bar int);
                    ALTER TABLE table_as_composite2 DROP COLUMN bar;
                    INSERT INTO table_as_composite2 (foo) VALUES (8)");
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<TableAsCompositeType>("table_as_composite2");
            var value = (TableAsCompositeType)conn.ExecuteScalar(@"SELECT t.*::table_as_composite2 FROM table_as_composite2 AS t")!;
            Assert.That(value.Foo, Is.EqualTo(8));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/2668")]
        public void Table_composites_not_loaded_if_not_requested()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                Pooling = false,
                ApplicationName = nameof(Table_composites_not_loaded_if_not_requested)
            };

            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("CREATE TEMP TABLE table_as_composite3 (foo int, bar int)");
            conn.ReloadTypes();

            Assert.Throws<ArgumentException>(() => conn.TypeMapper.MapComposite<TableAsCompositeType>("table_as_composite3"));
            Assert.That(conn.Connector!.DatabaseInfo.CompositeTypes.SingleOrDefault(c => c.Name.Contains("table_as_composite3")), Is.Null);
            Assert.That(conn.Connector!.DatabaseInfo.ArrayTypes.SingleOrDefault(c => c.Name.Contains("table_as_composite3")), Is.Null);
        }

        #endregion Table as Composite

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1125")]
        public void Nullable_property_in_class_composite()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                Pooling = false,
                ApplicationName = nameof(Nullable_property_in_class_composite)
            };
            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.nullable_property_type AS (foo INT)");
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<ClassWithNullableProperty>("nullable_property_type");

            var expected1 = new ClassWithNullableProperty { Foo = 8 };
            var expected2 = new ClassWithNullableProperty { Foo = null };
            using var cmd = new OpenGaussCommand(@"SELECT @p1, @p2", conn);
            cmd.Parameters.AddWithValue("p1", expected1);
            cmd.Parameters.AddWithValue("p2", expected2);

            using var reader = cmd.ExecuteReader();
            reader.Read();
            Assert.That(reader.GetFieldValue<ClassWithNullableProperty>(0).Foo, Is.EqualTo(8));
            Assert.That(reader.GetFieldValue<ClassWithNullableProperty>(1).Foo, Is.Null);
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1125")]
        public void Nullable_property_in_struct_composite()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                Pooling = false,
                ApplicationName = nameof(Nullable_property_in_struct_composite)
            };
            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("CREATE TYPE pg_temp.nullable_property_type AS (foo INT)");
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<StructWithNullableProperty>("nullable_property_type");

            var expected1 = new StructWithNullableProperty { Foo = 8 };
            var expected2 = new StructWithNullableProperty { Foo = null };
            using var cmd = new OpenGaussCommand(@"SELECT @p1, @p2", conn);
            cmd.Parameters.AddWithValue("p1", expected1);
            cmd.Parameters.AddWithValue("p2", expected2);

            using var reader = cmd.ExecuteReader();
            reader.Read();
            Assert.That(reader.GetFieldValue<StructWithNullableProperty>(0).Foo, Is.EqualTo(8));
            Assert.That(reader.GetFieldValue<StructWithNullableProperty>(1).Foo, Is.Null);
        }

        class ClassWithNullableProperty
        {
            public int? Foo { get; set; }
        }

        struct StructWithNullableProperty
        {
            public int? Foo { get; set; }
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1168")]
        public void With_schema()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(With_schema),  // Prevent backend type caching in TypeHandlerRegistry
                Pooling = false
            };

            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("DROP SCHEMA IF EXISTS composite_schema CASCADE; CREATE SCHEMA composite_schema");
            try
            {
                conn.ExecuteNonQuery("CREATE TYPE composite_schema.composite AS (foo int)");
                conn.ReloadTypes();
                conn.TypeMapper.MapComposite<Composite1>("composite_schema.composite");
                using var cmd = new OpenGaussCommand("SELECT @p", conn);
                cmd.Parameters.AddWithValue("p", new Composite1 { Foo = 8 });
                using var reader = cmd.ExecuteReader();
                reader.Read();
                Assert.That(reader.GetDataTypeName(0), Is.EqualTo("composite_schema.composite"));
                Assert.That(reader.GetFieldValue<Composite1>(0).Foo, Is.EqualTo(8));
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                    conn.ExecuteNonQuery("DROP SCHEMA IF EXISTS composite_schema CASCADE");
            }
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1168")]
        public void In_different_schemas()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = nameof(In_different_schemas),  // Prevent backend type caching in TypeHandlerRegistry
                Pooling = false
            };

            using var conn = OpenConnection(csb);
            conn.ExecuteNonQuery("DROP SCHEMA IF EXISTS composite_schema1 CASCADE; CREATE SCHEMA composite_schema1");
            conn.ExecuteNonQuery("DROP SCHEMA IF EXISTS composite_schema2 CASCADE; CREATE SCHEMA composite_schema2");
            try
            {
                conn.ExecuteNonQuery("CREATE TYPE composite_schema1.composite AS (foo int)");
                conn.ExecuteNonQuery("CREATE TYPE composite_schema2.composite AS (bar int)");
                conn.ReloadTypes();
                // Attempting to map without a fully-qualified name should fail
                Assert.That(() => conn.TypeMapper.MapComposite<Composite1>("composite"),
                    Throws.Exception.TypeOf<ArgumentException>()
                );
                conn.TypeMapper
                    .MapComposite<Composite1>("composite_schema1.composite")
                    .MapComposite<Composite2>("composite_schema2.composite");
                using var cmd = new OpenGaussCommand("SELECT @p1, @p2", conn);
                cmd.Parameters.AddWithValue("p1", new Composite1 { Foo = 8 });
                cmd.Parameters.AddWithValue("p2", new Composite2 { Bar = 9 });
                using var reader = cmd.ExecuteReader();
                reader.Read();
                Assert.That(reader.GetDataTypeName(0), Is.EqualTo("composite_schema1.composite"));
                Assert.That(reader.GetFieldValue<Composite1>(0).Foo, Is.EqualTo(8));
                Assert.That(reader.GetDataTypeName(1), Is.EqualTo("composite_schema2.composite"));
                Assert.That(reader.GetFieldValue<Composite2>(1).Bar, Is.EqualTo(9));
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                {
                    conn.ExecuteNonQuery("DROP SCHEMA IF EXISTS composite_schema1 CASCADE");
                    conn.ExecuteNonQuery("DROP SCHEMA IF EXISTS composite_schema2 CASCADE");
                    conn.ReloadTypes();
                }
            }
        }

        class Composite1 { public int Foo { get; set; } }
        class Composite2 { public int Bar { get; set; } }
        class Composite3 { public int Bar { get; set; } }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1612")]
        public void Local_mapping_do_not_leak()
        {
            var csb = new OpenGaussConnectionStringBuilder(ConnectionString)
            {
                Pooling = false,
                ApplicationName = nameof(Local_mapping_do_not_leak)
            };
            OpenGaussConnection.GlobalTypeMapper.MapComposite<Composite2>("composite");
            try
            {
                using (var conn = OpenConnection(csb))
                {
                    conn.ExecuteNonQuery("CREATE TYPE composite AS (bar int)");
                    conn.ReloadTypes();
                    Assert.That(conn.ExecuteScalar("SELECT '(8)'::composite"), Is.TypeOf<Composite2>());
                    conn.TypeMapper.MapComposite<Composite3>("composite");
                    Assert.That(conn.ExecuteScalar("SELECT '(8)'::composite"), Is.TypeOf<Composite3>());
                }
                using (var conn = OpenConnection(csb))
                    Assert.That(conn.ExecuteScalar("SELECT '(8)'::composite"), Is.TypeOf<Composite2>());
            }
            finally
            {
                OpenGaussConnection.GlobalTypeMapper.UnmapComposite<Composite2>("composite");
                using var conn = OpenConnection(csb);
                conn.ExecuteNonQuery("DROP TYPE IF EXISTS composite");
                OpenGaussConnection.ClearPool(conn);
            }
        }
    }
}
