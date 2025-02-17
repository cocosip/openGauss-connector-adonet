
using OpenGauss.NET;
using OpenGauss.NET.Types;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

namespace OpenGauss.Tests
{
    public class OpenGaussParameterTest : TestBase
    {
        [Test, Description("Makes sure that when OpenGaussDbType or Value/OpenGaussValue are set, DbType and OpenGaussDbType are set accordingly")]
        public void Implicit_setting_of_DbType()
        {
            var p = new OpenGaussParameter("p", DbType.Int32);
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer));

            // As long as OpenGaussDbType/DbType aren't set explicitly, infer them from Value
            p = new OpenGaussParameter("p", 8);
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer));
            Assert.That(p.DbType, Is.EqualTo(DbType.Int32));

            p.Value = 3.0;
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Double));
            Assert.That(p.DbType, Is.EqualTo(DbType.Double));

            p.OpenGaussDbType = OpenGaussDbType.Bytea;
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Bytea));
            Assert.That(p.DbType, Is.EqualTo(DbType.Binary));

            p.Value = "dont_change";
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Bytea));
            Assert.That(p.DbType, Is.EqualTo(DbType.Binary));

            p = new OpenGaussParameter("p", new int[0]);
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Array | OpenGaussDbType.Integer));
            Assert.That(p.DbType, Is.EqualTo(DbType.Object));
        }

        [Test]
        public void DataTypeName()
        {
            using var conn = OpenConnection();
            using var cmd = new OpenGaussCommand("SELECT @p", conn);
            var p1 = new OpenGaussParameter { ParameterName = "p", Value = 8, DataTypeName = "integer" };
            cmd.Parameters.Add(p1);
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(8));
            // Purposefully try to send int as string, which should fail. This makes sure
            // the above doesn't work simply because of type inference from the CLR type.
            p1.DataTypeName = "text";
            Assert.That(() => cmd.ExecuteScalar(), Throws.Exception.TypeOf<InvalidCastException>());

            cmd.Parameters.Clear();

            var p2 = new OpenGaussParameter<int> { ParameterName = "p", TypedValue = 8, DataTypeName = "integer" };
            cmd.Parameters.Add(p2);
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(8));
            // Purposefully try to send int as string, which should fail. This makes sure
            // the above doesn't work simply because of type inference from the CLR type.
            p2.DataTypeName = "text";
            Assert.That(() => cmd.ExecuteScalar(), Throws.Exception.TypeOf<InvalidCastException>());
        }

        [Test]
        public void Positional_parameter_is_positional()
        {
            var p = new OpenGaussParameter(OpenGaussParameter.PositionalName, 1);
            Assert.That(p.IsPositional, Is.True);

            var p2 = new OpenGaussParameter(null, 1);
            Assert.That(p2.IsPositional, Is.True);
        }

        [Test]
        public void Infer_data_type_name_from_OpenGaussDbType()
        {
            var p = new OpenGaussParameter("par_field1", OpenGaussDbType.Varchar, 50);
            Assert.That(p.DataTypeName, Is.EqualTo("character varying"));
        }

        [Test]
        public void Infer_data_type_name_from_DbType()
        {
            var p = new OpenGaussParameter("par_field1", DbType.String, 50);
            Assert.That(p.DataTypeName, Is.EqualTo("text"));
        }

        [Test]
        public void Infer_data_type_name_from_OpenGaussDbType_for_array()
        {
            var p = new OpenGaussParameter("int_array", OpenGaussDbType.Array | OpenGaussDbType.Integer);
            Assert.That(p.DataTypeName, Is.EqualTo("integer[]"));
        }

        [Test]
        public void Infer_data_type_name_from_OpenGaussDbType_for_built_in_range()
        {
            var p = new OpenGaussParameter("numeric_range", OpenGaussDbType.Range | OpenGaussDbType.Numeric);
            Assert.That(p.DataTypeName, Is.EqualTo("numrange"));
        }

        [Test]
        public void Cannot_infer_data_type_name_from_OpenGaussDbType_for_unknown_range()
        {
            var p = new OpenGaussParameter("text_range", OpenGaussDbType.Range | OpenGaussDbType.Text);
            Assert.That(p.DataTypeName, Is.EqualTo(null));
        }

        [Test]
        public void Infer_data_type_name_from_ClrType()
        {
            var p = new OpenGaussParameter("p1", new Dictionary<string, string>());
            Assert.That(p.DataTypeName, Is.EqualTo("hstore"));
        }

        [Test]
        public void Setting_DbType_sets_OpenGaussDbType()
        {
            var p = new OpenGaussParameter();
            p.DbType = DbType.Binary;
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Bytea));
        }

        [Test]
        public void Setting_OpenGaussDbType_sets_DbType()
        {
            var p = new OpenGaussParameter();
            p.OpenGaussDbType = OpenGaussDbType.Bytea;
            Assert.That(p.DbType, Is.EqualTo(DbType.Binary));
        }

        [Test]
        public void Setting_value_does_not_change_DbType()
        {
            var p = new OpenGaussParameter { DbType = DbType.String, OpenGaussDbType = OpenGaussDbType.Bytea };
            p.Value = 8;
            Assert.That(p.DbType, Is.EqualTo(DbType.Binary));
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Bytea));
        }

        // Older tests

        #region Constructors

        [Test]
        public void Constructor1()
        {
            var p = new OpenGaussParameter();
            Assert.That(p.DbType, Is.EqualTo(DbType.Object), "DbType");
            Assert.That(p.Direction, Is.EqualTo(ParameterDirection.Input), "Direction");
            Assert.That(p.IsNullable, Is.False, "IsNullable");
            Assert.That(p.ParameterName, Is.EqualTo(string.Empty), "ParameterName");
            Assert.That(p.Precision, Is.EqualTo(0), "Precision");
            Assert.That(p.Scale, Is.EqualTo(0), "Scale");
            Assert.That(p.Size, Is.EqualTo(0), "Size");
            Assert.That(p.SourceColumn, Is.EqualTo(string.Empty), "SourceColumn");
            Assert.That(p.SourceVersion, Is.EqualTo(DataRowVersion.Current), "SourceVersion");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown), "OpenGaussDbType");
            Assert.That(p.Value, Is.Null, "Value");
        }

        [Test]
        public void Constructor2_Value_DateTime()
        {
            var value = new DateTime(2004, 8, 24);

            var p = new OpenGaussParameter("address", value);
            Assert.That(p.DbType, Is.EqualTo(DbType.DateTime2), "B:DbType");
            Assert.That(p.Direction, Is.EqualTo(ParameterDirection.Input), "B:Direction");
            Assert.That(p.IsNullable, Is.False, "B:IsNullable");
            Assert.That(p.ParameterName, Is.EqualTo("address"), "B:ParameterName");
            Assert.That(p.Precision, Is.EqualTo(0), "B:Precision");
            Assert.That(p.Scale, Is.EqualTo(0), "B:Scale");
            //Assert.AreEqual (0, p.Size, "B:Size");
            Assert.That(p.SourceColumn, Is.EqualTo(string.Empty), "B:SourceColumn");
            Assert.That(p.SourceVersion, Is.EqualTo(DataRowVersion.Current), "B:SourceVersion");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp), "B:OpenGaussDbType");
            Assert.That(p.Value, Is.EqualTo(value), "B:Value");
        }

        [Test]
        public void Constructor2_Value_DBNull()
        {
            var p = new OpenGaussParameter("address", DBNull.Value);
            Assert.That(p.DbType, Is.EqualTo(DbType.Object), "B:DbType");
            Assert.That(p.Direction, Is.EqualTo(ParameterDirection.Input), "B:Direction");
            Assert.That(p.IsNullable, Is.False, "B:IsNullable");
            Assert.That(p.ParameterName, Is.EqualTo("address"), "B:ParameterName");
            Assert.That(p.Precision, Is.EqualTo(0), "B:Precision");
            Assert.That(p.Scale, Is.EqualTo(0), "B:Scale");
            Assert.That(p.Size, Is.EqualTo(0), "B:Size");
            Assert.That(p.SourceColumn, Is.EqualTo(string.Empty), "B:SourceColumn");
            Assert.That(p.SourceVersion, Is.EqualTo(DataRowVersion.Current), "B:SourceVersion");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown), "B:OpenGaussDbType");
            Assert.That(p.Value, Is.EqualTo(DBNull.Value), "B:Value");
        }

        [Test]
        public void Constructor2_Value_null()
        {
            var p = new OpenGaussParameter("address", null);
            Assert.That(p.DbType, Is.EqualTo(DbType.Object), "A:DbType");
            Assert.That(p.Direction, Is.EqualTo(ParameterDirection.Input), "A:Direction");
            Assert.That(p.IsNullable, Is.False, "A:IsNullable");
            Assert.That(p.ParameterName, Is.EqualTo("address"), "A:ParameterName");
            Assert.That(p.Precision, Is.EqualTo(0), "A:Precision");
            Assert.That(p.Scale, Is.EqualTo(0), "A:Scale");
            Assert.That(p.Size, Is.EqualTo(0), "A:Size");
            Assert.That(p.SourceColumn, Is.EqualTo(string.Empty), "A:SourceColumn");
            Assert.That(p.SourceVersion, Is.EqualTo(DataRowVersion.Current), "A:SourceVersion");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown), "A:OpenGaussDbType");
            Assert.That(p.Value, Is.Null, "A:Value");
        }

        [Test]
        //.ctor (String, OpenGaussDbType, Int32, String, ParameterDirection, bool, byte, byte, DataRowVersion, object)
        public void Constructor7()
        {
            var p1 = new OpenGaussParameter("p1Name", OpenGaussDbType.Varchar, 20,
                "srcCol", ParameterDirection.InputOutput, false, 0, 0,
                DataRowVersion.Original, "foo");
            Assert.That(p1.DbType, Is.EqualTo(DbType.String), "DbType");
            Assert.That(p1.Direction, Is.EqualTo(ParameterDirection.InputOutput), "Direction");
            Assert.That(p1.IsNullable, Is.EqualTo(false), "IsNullable");
            //Assert.AreEqual (999, p1.LocaleId, "#");
            Assert.That(p1.ParameterName, Is.EqualTo("p1Name"), "ParameterName");
            Assert.That(p1.Precision, Is.EqualTo(0), "Precision");
            Assert.That(p1.Scale, Is.EqualTo(0), "Scale");
            Assert.That(p1.Size, Is.EqualTo(20), "Size");
            Assert.That(p1.SourceColumn, Is.EqualTo("srcCol"), "SourceColumn");
            Assert.That(p1.SourceColumnNullMapping, Is.EqualTo(false), "SourceColumnNullMapping");
            Assert.That(p1.SourceVersion, Is.EqualTo(DataRowVersion.Original), "SourceVersion");
            Assert.That(p1.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Varchar), "OpenGaussDbType");
            //Assert.AreEqual (3210, p1.OpenGaussValue, "#");
            Assert.That(p1.Value, Is.EqualTo("foo"), "Value");
            //Assert.AreEqual ("database", p1.XmlSchemaCollectionDatabase, "XmlSchemaCollectionDatabase");
            //Assert.AreEqual ("name", p1.XmlSchemaCollectionName, "XmlSchemaCollectionName");
            //Assert.AreEqual ("schema", p1.XmlSchemaCollectionOwningSchema, "XmlSchemaCollectionOwningSchema");
        }

        [Test]
        public void Clone()
        {
            var expected = new OpenGaussParameter
            {
                Value = 42,
                ParameterName = "TheAnswer",

                DbType = DbType.Int32,
                OpenGaussDbType = OpenGaussDbType.Integer,
                DataTypeName = "integer",

                Direction = ParameterDirection.InputOutput,
                IsNullable = true,
                Precision = 1,
                Scale = 2,
                Size = 4,

                SourceVersion = DataRowVersion.Proposed,
                SourceColumn = "source",
                SourceColumnNullMapping = true,
            };
            var actual = expected.Clone();

            Assert.That(actual.Value, Is.EqualTo(expected.Value));
            Assert.That(actual.ParameterName, Is.EqualTo(expected.ParameterName));

            Assert.That(actual.DbType, Is.EqualTo(expected.DbType));
            Assert.That(actual.OpenGaussDbType, Is.EqualTo(expected.OpenGaussDbType));
            Assert.That(actual.DataTypeName, Is.EqualTo(expected.DataTypeName));

            Assert.That(actual.Direction, Is.EqualTo(expected.Direction));
            Assert.That(actual.IsNullable, Is.EqualTo(expected.IsNullable));
            Assert.That(actual.Precision, Is.EqualTo(expected.Precision));
            Assert.That(actual.Scale, Is.EqualTo(expected.Scale));
            Assert.That(actual.Size, Is.EqualTo(expected.Size));

            Assert.That(actual.SourceVersion, Is.EqualTo(expected.SourceVersion));
            Assert.That(actual.SourceColumn, Is.EqualTo(expected.SourceColumn));
            Assert.That(actual.SourceColumnNullMapping, Is.EqualTo(expected.SourceColumnNullMapping));
        }

        [Test]
        public void Clone_generic()
        {
            var expected = new OpenGaussParameter<int>
            {
                TypedValue = 42,
                ParameterName = "TheAnswer",

                DbType = DbType.Int32,
                OpenGaussDbType = OpenGaussDbType.Integer,
                DataTypeName = "integer",

                Direction = ParameterDirection.InputOutput,
                IsNullable = true,
                Precision = 1,
                Scale = 2,
                Size = 4,

                SourceVersion = DataRowVersion.Proposed,
                SourceColumn = "source",
                SourceColumnNullMapping = true,
            };
            var actual = (OpenGaussParameter<int>)expected.Clone();

            Assert.That(actual.Value, Is.EqualTo(expected.Value));
            Assert.That(actual.TypedValue, Is.EqualTo(expected.TypedValue));
            Assert.That(actual.ParameterName, Is.EqualTo(expected.ParameterName));

            Assert.That(actual.DbType, Is.EqualTo(expected.DbType));
            Assert.That(actual.OpenGaussDbType, Is.EqualTo(expected.OpenGaussDbType));
            Assert.That(actual.DataTypeName, Is.EqualTo(expected.DataTypeName));

            Assert.That(actual.Direction, Is.EqualTo(expected.Direction));
            Assert.That(actual.IsNullable, Is.EqualTo(expected.IsNullable));
            Assert.That(actual.Precision, Is.EqualTo(expected.Precision));
            Assert.That(actual.Scale, Is.EqualTo(expected.Scale));
            Assert.That(actual.Size, Is.EqualTo(expected.Size));

            Assert.That(actual.SourceVersion, Is.EqualTo(expected.SourceVersion));
            Assert.That(actual.SourceColumn, Is.EqualTo(expected.SourceColumn));
            Assert.That(actual.SourceColumnNullMapping, Is.EqualTo(expected.SourceColumnNullMapping));
        }

        #endregion

        [Test]
        [Ignore("")]
        public void InferType_invalid_throws()
        {
            var notsupported = new object[]
            {
                ushort.MaxValue,
                uint.MaxValue,
                ulong.MaxValue,
                sbyte.MaxValue,
                new OpenGaussParameter()
            };

            var param = new OpenGaussParameter();

            for (var i = 0; i < notsupported.Length; i++)
            {
                try
                {
                    param.Value = notsupported[i];
                    Assert.Fail("#A1:" + i);
                }
                catch (FormatException)
                {
                    // appears to be bug in .NET 1.1 while
                    // constructing exception message
                }
                catch (ArgumentException ex)
                {
                    // The parameter data type of ... is invalid
                    Assert.That(ex.GetType(), Is.EqualTo(typeof(ArgumentException)), "#A2");
                    Assert.That(ex.InnerException, Is.Null, "#A3");
                    Assert.That(ex.Message, Is.Not.Null, "#A4");
                    Assert.That(ex.ParamName, Is.Null, "#A5");
                }
            }
        }

        [Test] // bug #320196
        public void Parameter_null()
        {
            var param = new OpenGaussParameter("param", OpenGaussDbType.Numeric);
            Assert.That(param.Scale, Is.EqualTo(0), "#A1");
            param.Value = DBNull.Value;
            Assert.That(param.Scale, Is.EqualTo(0), "#A2");

            param = new OpenGaussParameter("param", OpenGaussDbType.Integer);
            Assert.That(param.Scale, Is.EqualTo(0), "#B1");
            param.Value = DBNull.Value;
            Assert.That(param.Scale, Is.EqualTo(0), "#B2");
        }

        [Test]
        [Ignore("")]
        public void Parameter_type()
        {
            OpenGaussParameter p;

            // If Type is not set, then type is inferred from the value
            // assigned. The Type should be inferred everytime Value is assigned
            // If value is null or DBNull, then the current Type should be reset to Text.
            p = new OpenGaussParameter();
            Assert.That(p.DbType, Is.EqualTo(DbType.String), "#A1");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Text), "#A2");
            p.Value = DBNull.Value;
            Assert.That(p.DbType, Is.EqualTo(DbType.String), "#B1");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Text), "#B2");
            p.Value = 1;
            Assert.That(p.DbType, Is.EqualTo(DbType.Int32), "#C1");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer), "#C2");
            p.Value = DBNull.Value;
            Assert.That(p.DbType, Is.EqualTo(DbType.String), "#D1");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Text), "#D2");
            p.Value = new byte[] { 0x0a };
            Assert.That(p.DbType, Is.EqualTo(DbType.Binary), "#E1");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Bytea), "#E2");
            p.Value = null;
            Assert.That(p.DbType, Is.EqualTo(DbType.String), "#F1");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Text), "#F2");
            p.Value = DateTime.Now;
            Assert.That(p.DbType, Is.EqualTo(DbType.DateTime), "#G1");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp), "#G2");
            p.Value = null;
            Assert.That(p.DbType, Is.EqualTo(DbType.String), "#H1");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Text), "#H2");

            // If DbType is set, then the OpenGaussDbType should not be
            // inferred from the value assigned.
            p = new OpenGaussParameter();
            p.DbType = DbType.DateTime;
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp), "#I1");
            p.Value = 1;
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp), "#I2");
            p.Value = null;
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp), "#I3");
            p.Value = DBNull.Value;
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp), "#I4");

            // If OpenGaussDbType is set, then the DbType should not be
            // inferred from the value assigned.
            p = new OpenGaussParameter();
            p.OpenGaussDbType = OpenGaussDbType.Bytea;
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Bytea), "#J1");
            p.Value = 1;
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Bytea), "#J2");
            p.Value = null;
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Bytea), "#J3");
            p.Value = DBNull.Value;
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Bytea), "#J4");
        }

        [Test]
        [Ignore("")]
        public void ParameterName()
        {
            var p = new OpenGaussParameter();
            p.ParameterName = "name";
            Assert.That(p.ParameterName, Is.EqualTo("name"), "#A:ParameterName");
            Assert.That(p.SourceColumn, Is.EqualTo(string.Empty), "#A:SourceColumn");

            p.ParameterName = null;
            Assert.That(p.ParameterName, Is.EqualTo(string.Empty), "#B:ParameterName");
            Assert.That(p.SourceColumn, Is.EqualTo(string.Empty), "#B:SourceColumn");

            p.ParameterName = " ";
            Assert.That(p.ParameterName, Is.EqualTo(" "), "#C:ParameterName");
            Assert.That(p.SourceColumn, Is.EqualTo(string.Empty), "#C:SourceColumn");

            p.ParameterName = " name ";
            Assert.That(p.ParameterName, Is.EqualTo(" name "), "#D:ParameterName");
            Assert.That(p.SourceColumn, Is.EqualTo(string.Empty), "#D:SourceColumn");

            p.ParameterName = string.Empty;
            Assert.That(p.ParameterName, Is.EqualTo(string.Empty), "#E:ParameterName");
            Assert.That(p.SourceColumn, Is.EqualTo(string.Empty), "#E:SourceColumn");
        }

        [Test]
        public void ResetDbType()
        {
            OpenGaussParameter p;

            //Parameter with an assigned value but no DbType specified
            p = new OpenGaussParameter("foo", 42);
            p.ResetDbType();
            Assert.That(p.DbType, Is.EqualTo(DbType.Int32), "#A:DbType");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer), "#A:OpenGaussDbType");
            Assert.That(p.Value, Is.EqualTo(42), "#A:Value");

            p.DbType = DbType.DateTime; //assigning a DbType
            Assert.That(p.DbType, Is.EqualTo(DbType.DateTime), "#B:DbType1");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.TimestampTz), "#B:SqlDbType1");
            p.ResetDbType();
            Assert.That(p.DbType, Is.EqualTo(DbType.Int32), "#B:DbType2");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer), "#B:SqlDbtype2");

            //Parameter with an assigned OpenGaussDbType but no specified value
            p = new OpenGaussParameter("foo", OpenGaussDbType.Integer);
            p.ResetDbType();
            Assert.That(p.DbType, Is.EqualTo(DbType.Object), "#C:DbType");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown), "#C:OpenGaussDbType");

            p.OpenGaussDbType = OpenGaussDbType.TimestampTz; //assigning a OpenGaussDbType
            Assert.That(p.DbType, Is.EqualTo(DbType.DateTime), "#D:DbType1");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.TimestampTz), "#D:SqlDbType1");
            p.ResetDbType();
            Assert.That(p.DbType, Is.EqualTo(DbType.Object), "#D:DbType2");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown), "#D:SqlDbType2");

            p = new OpenGaussParameter();
            p.Value = DateTime.MaxValue;
            Assert.That(p.DbType, Is.EqualTo(DbType.DateTime2), "#E:DbType1");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp), "#E:SqlDbType1");
            p.Value = null;
            p.ResetDbType();
            Assert.That(p.DbType, Is.EqualTo(DbType.Object), "#E:DbType2");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown), "#E:SqlDbType2");

            p = new OpenGaussParameter("foo", OpenGaussDbType.Varchar);
            p.Value = DateTime.MaxValue;
            p.ResetDbType();
            Assert.That(p.DbType, Is.EqualTo(DbType.DateTime2), "#F:DbType");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp), "#F:OpenGaussDbType");
            Assert.That(p.Value, Is.EqualTo(DateTime.MaxValue), "#F:Value");

            p = new OpenGaussParameter("foo", OpenGaussDbType.Varchar);
            p.Value = DBNull.Value;
            p.ResetDbType();
            Assert.That(p.DbType, Is.EqualTo(DbType.Object), "#G:DbType");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown), "#G:OpenGaussDbType");
            Assert.That(p.Value, Is.EqualTo(DBNull.Value), "#G:Value");

            p = new OpenGaussParameter("foo", OpenGaussDbType.Varchar);
            p.Value = null;
            p.ResetDbType();
            Assert.That(p.DbType, Is.EqualTo(DbType.Object), "#G:DbType");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown), "#G:OpenGaussDbType");
            Assert.That(p.Value, Is.Null, "#G:Value");
        }

        [Test]
        public void ParameterName_retains_prefix()
            => Assert.That(new OpenGaussParameter("@p", DbType.String).ParameterName, Is.EqualTo("@p"));

        [Test]
        [Ignore("")]
        public void SourceColumn()
        {
            var p = new OpenGaussParameter();
            p.SourceColumn = "name";
            Assert.That(p.ParameterName, Is.EqualTo(string.Empty), "#A:ParameterName");
            Assert.That(p.SourceColumn, Is.EqualTo("name"), "#A:SourceColumn");

            p.SourceColumn = null;
            Assert.That(p.ParameterName, Is.EqualTo(string.Empty), "#B:ParameterName");
            Assert.That(p.SourceColumn, Is.EqualTo(string.Empty), "#B:SourceColumn");

            p.SourceColumn = " ";
            Assert.That(p.ParameterName, Is.EqualTo(string.Empty), "#C:ParameterName");
            Assert.That(p.SourceColumn, Is.EqualTo(" "), "#C:SourceColumn");

            p.SourceColumn = " name ";
            Assert.That(p.ParameterName, Is.EqualTo(string.Empty), "#D:ParameterName");
            Assert.That(p.SourceColumn, Is.EqualTo(" name "), "#D:SourceColumn");

            p.SourceColumn = string.Empty;
            Assert.That(p.ParameterName, Is.EqualTo(string.Empty), "#E:ParameterName");
            Assert.That(p.SourceColumn, Is.EqualTo(string.Empty), "#E:SourceColumn");
        }

        [Test]
        public void Bug1011100_OpenGaussDbType()
        {
            var p = new OpenGaussParameter();
            p.Value = DBNull.Value;
            Assert.That(p.DbType, Is.EqualTo(DbType.Object), "#A:DbType");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown), "#A:OpenGaussDbType");

            // Now change parameter value.
            // Note that as we didn't explicitly specified a dbtype, the dbtype property should change when
            // the value changes...

            p.Value = 8;

            Assert.That(p.DbType, Is.EqualTo(DbType.Int32), "#A:DbType");
            Assert.That(p.OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer), "#A:OpenGaussDbType");

            //Assert.AreEqual(3510, p.Value, "#A:Value");
            //p.OpenGaussDbType = OpenGaussDbType.Varchar;
            //Assert.AreEqual(DbType.String, p.DbType, "#B:DbType");
            //Assert.AreEqual(OpenGaussDbType.Varchar, p.OpenGaussDbType, "#B:OpenGaussDbType");
            //Assert.AreEqual(3510, p.Value, "#B:Value");
        }

        [Test]
        public void OpenGaussParameter_Clone()
        {
            var param = new OpenGaussParameter();

            param.Value = 5;
            param.Precision = 1;
            param.Scale = 1;
            param.Size = 1;
            param.Direction = ParameterDirection.Input;
            param.IsNullable = true;
            param.ParameterName = "parameterName";
            param.SourceColumn = "source_column";
            param.SourceVersion = DataRowVersion.Current;
            param.OpenGaussValue = 5;
            param.SourceColumnNullMapping = false;

            var newParam = param.Clone();

            Assert.That(newParam.Value, Is.EqualTo(param.Value));
            Assert.That(newParam.Precision, Is.EqualTo(param.Precision));
            Assert.That(newParam.Scale, Is.EqualTo(param.Scale));
            Assert.That(newParam.Size, Is.EqualTo(param.Size));
            Assert.That(newParam.Direction, Is.EqualTo(param.Direction));
            Assert.That(newParam.IsNullable, Is.EqualTo(param.IsNullable));
            Assert.That(newParam.ParameterName, Is.EqualTo(param.ParameterName));
            Assert.That(newParam.TrimmedName, Is.EqualTo(param.TrimmedName));
            Assert.That(newParam.SourceColumn, Is.EqualTo(param.SourceColumn));
            Assert.That(newParam.SourceVersion, Is.EqualTo(param.SourceVersion));
            Assert.That(newParam.OpenGaussValue, Is.EqualTo(param.OpenGaussValue));
            Assert.That(newParam.SourceColumnNullMapping, Is.EqualTo(param.SourceColumnNullMapping));
            Assert.That(newParam.OpenGaussValue, Is.EqualTo(param.OpenGaussValue));

        }

        [Test]
        public void Precision_via_interface()
        {
            var parameter = new OpenGaussParameter();
            var paramIface = (IDbDataParameter)parameter;

            paramIface.Precision = 42;

            Assert.That(paramIface.Precision, Is.EqualTo((byte)42));
        }

        [Test]
        public void Precision_via_base_class()
        {
            var parameter = new OpenGaussParameter();
            var paramBase = (DbParameter)parameter;

            paramBase.Precision = 42;

            Assert.That(paramBase.Precision, Is.EqualTo((byte)42));
        }

        [Test]
        public void Scale_via_interface()
        {
            var parameter = new OpenGaussParameter();
            var paramIface = (IDbDataParameter)parameter;

            paramIface.Scale = 42;

            Assert.That(paramIface.Scale, Is.EqualTo((byte)42));
        }

        [Test]
        public void Scale_via_base_class()
        {
            var parameter = new OpenGaussParameter();
            var paramBase = (DbParameter)parameter;

            paramBase.Scale = 42;

            Assert.That(paramBase.Scale, Is.EqualTo((byte)42));
        }

        [Test]
        public void Null_value_throws()
        {
            using var connection = OpenConnection();
            using var command = new OpenGaussCommand("SELECT @p", connection)
            {
                Parameters = { new OpenGaussParameter("p", null) }
            };

            Assert.That(() => command.ExecuteReader(), Throws.InvalidOperationException);
        }

        [Test]
        public void Null_value_with_nullable_type()
        {
            using var connection = OpenConnection();
            using var command = new OpenGaussCommand("SELECT @p", connection)
            {
                Parameters = { new OpenGaussParameter<int?>("p", null) }
            };
            using var reader = command.ExecuteReader();

            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetFieldValue<int?>(0), Is.Null);
        }

#if NeedsPorting
        [Test]
        [Category ("NotWorking")]
        public void InferType_Char()
        {
            Char value = 'X';

            String string_value = "X";

            OpenGaussParameter p = new OpenGaussParameter ();
            p.Value = value;
            Assert.AreEqual (OpenGaussDbType.Text, p.OpenGaussDbType, "#A:OpenGaussDbType");
            Assert.AreEqual (DbType.String, p.DbType, "#A:DbType");
            Assert.AreEqual (string_value, p.Value, "#A:Value");

            p = new OpenGaussParameter ();
            p.Value = value;
            Assert.AreEqual (value, p.Value, "#B:Value1");
            Assert.AreEqual (OpenGaussDbType.Text, p.OpenGaussDbType, "#B:OpenGaussDbType");
            Assert.AreEqual (string_value, p.Value, "#B:Value2");

            p = new OpenGaussParameter ();
            p.Value = value;
            Assert.AreEqual (value, p.Value, "#C:Value1");
            Assert.AreEqual (DbType.String, p.DbType, "#C:DbType");
            Assert.AreEqual (string_value, p.Value, "#C:Value2");

            p = new OpenGaussParameter ("name", value);
            Assert.AreEqual (value, p.Value, "#D:Value1");
            Assert.AreEqual (DbType.String, p.DbType, "#D:DbType");
            Assert.AreEqual (OpenGaussDbType.Text, p.OpenGaussDbType, "#D:OpenGaussDbType");
            Assert.AreEqual (string_value, p.Value, "#D:Value2");

            p = new OpenGaussParameter ("name", 5);
            p.Value = value;
            Assert.AreEqual (value, p.Value, "#E:Value1");
            Assert.AreEqual (DbType.String, p.DbType, "#E:DbType");
            Assert.AreEqual (OpenGaussDbType.Text, p.OpenGaussDbType, "#E:OpenGaussDbType");
            Assert.AreEqual (string_value, p.Value, "#E:Value2");

            p = new OpenGaussParameter ("name", OpenGaussDbType.Text);
            p.Value = value;
            Assert.AreEqual (OpenGaussDbType.Text, p.OpenGaussDbType, "#F:OpenGaussDbType");
            Assert.AreEqual (value, p.Value, "#F:Value");
        }

        [Test]
        [Category ("NotWorking")]
        public void InferType_CharArray()
        {
            Char[] value = new Char[] { 'A', 'X' };

            String string_value = "AX";

            OpenGaussParameter p = new OpenGaussParameter ();
            p.Value = value;
            Assert.AreEqual (value, p.Value, "#A:Value1");
            Assert.AreEqual (OpenGaussDbType.Text, p.OpenGaussDbType, "#A:OpenGaussDbType");
            Assert.AreEqual (DbType.String, p.DbType, "#A:DbType");
            Assert.AreEqual (string_value, p.Value, "#A:Value2");

            p = new OpenGaussParameter ();
            p.Value = value;
            Assert.AreEqual (value, p.Value, "#B:Value1");
            Assert.AreEqual (OpenGaussDbType.Text, p.OpenGaussDbType, "#B:OpenGaussDbType");
            Assert.AreEqual (string_value, p.Value, "#B:Value2");

            p = new OpenGaussParameter ();
            p.Value = value;
            Assert.AreEqual (value, p.Value, "#C:Value1");
            Assert.AreEqual (DbType.String, p.DbType, "#C:DbType");
            Assert.AreEqual (string_value, p.Value, "#C:Value2");

            p = new OpenGaussParameter ("name", value);
            Assert.AreEqual (value, p.Value, "#D:Value1");
            Assert.AreEqual (DbType.String, p.DbType, "#D:DbType");
            Assert.AreEqual (OpenGaussDbType.Text, p.OpenGaussDbType, "#D:OpenGaussDbType");
            Assert.AreEqual (string_value, p.Value, "#D:Value2");

            p = new OpenGaussParameter ("name", 5);
            p.Value = value;
            Assert.AreEqual (value, p.Value, "#E:Value1");
            Assert.AreEqual (DbType.String, p.DbType, "#E:DbType");
            Assert.AreEqual (OpenGaussDbType.Text, p.OpenGaussDbType, "#E:OpenGaussDbType");
            Assert.AreEqual (string_value, p.Value, "#E:Value2");

            p = new OpenGaussParameter ("name", OpenGaussDbType.Text);
            p.Value = value;
            Assert.AreEqual (OpenGaussDbType.Text, p.OpenGaussDbType, "#F:OpenGaussDbType");
            Assert.AreEqual (value, p.Value, "#F:Value");
        }

        [Test]
        public void InferType_Object()
        {
            Object value = new Object();

            OpenGaussParameter param = new OpenGaussParameter();
            param.Value = value;
            Assert.AreEqual(OpenGaussDbType.Variant, param.OpenGaussDbType, "#1");
            Assert.AreEqual(DbType.Object, param.DbType, "#2");
        }

        [Test]
        public void LocaleId ()
        {
            OpenGaussParameter parameter = new OpenGaussParameter ();
            Assert.AreEqual (0, parameter.LocaleId, "#1");
            parameter.LocaleId = 15;
            Assert.AreEqual(15, parameter.LocaleId, "#2");
        }
#endif
    }
}
