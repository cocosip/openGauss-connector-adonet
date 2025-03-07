using System;
using System.Data;
using System.Threading.Tasks;
using OpenGauss.NET.Types;
using NUnit.Framework;
using static OpenGauss.Tests.TestUtil;
using OpenGauss.NET;

namespace OpenGauss.Tests
{
    public class DataAdapterTests : TestBase
    {
        [Test]
        public async Task DataAdapter_SelectCommand()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand("SELECT 1", conn);
            var da = new OpenGaussDataAdapter();
            da.SelectCommand = command;
            var ds = new DataSet();
            da.Fill(ds);
            //ds.WriteXml("TestUseDataAdapter.xml");
        }

        [Test]
        public async Task DataAdapter_OpenGaussCommand_in_constructor()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand("SELECT 1", conn);
            command.Connection = conn;
            var da = new OpenGaussDataAdapter(command);
            var ds = new DataSet();
            da.Fill(ds);
            //ds.WriteXml("TestUseDataAdapterOpenGaussConnectionConstructor.xml");
        }

        [Test]
        public async Task DataAdapter_string_command_in_constructor()
        {
            using var conn = await OpenConnectionAsync();
            var da = new OpenGaussDataAdapter("SELECT 1", conn);
            var ds = new DataSet();
            da.Fill(ds);
            //ds.WriteXml("TestUseDataAdapterStringOpenGaussConnectionConstructor.xml");
        }

        [Test]
        public void DataAdapter_connection_string_in_constructor()
        {
            var da = new OpenGaussDataAdapter("SELECT 1", ConnectionString);
            var ds = new DataSet();
            da.Fill(ds);
            //ds.WriteXml("TestUseDataAdapterStringStringConstructor.xml");
        }

        [Test]
        [MonoIgnore("Bug in mono, submitted pull request: https://github.com/mono/mono/pull/1172")]
        public async Task Insert_with_DataSet()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);
            var ds = new DataSet();
            var da = new OpenGaussDataAdapter($"SELECT * FROM {table}", conn);

            da.InsertCommand = new OpenGaussCommand($"INSERT INTO {table} (field_int2, field_timestamp, field_numeric) VALUES (:a, :b, :c)", conn);

            da.InsertCommand.Parameters.Add(new OpenGaussParameter("a", DbType.Int16));
            da.InsertCommand.Parameters.Add(new OpenGaussParameter("b", DbType.DateTime2));
            da.InsertCommand.Parameters.Add(new OpenGaussParameter("c", DbType.Decimal));

            da.InsertCommand.Parameters[0].Direction = ParameterDirection.Input;
            da.InsertCommand.Parameters[1].Direction = ParameterDirection.Input;
            da.InsertCommand.Parameters[2].Direction = ParameterDirection.Input;

            da.InsertCommand.Parameters[0].SourceColumn = "field_int2";
            da.InsertCommand.Parameters[1].SourceColumn = "field_timestamp";
            da.InsertCommand.Parameters[2].SourceColumn = "field_numeric";

            da.Fill(ds);

            var dt = ds.Tables[0];
            var dr = dt.NewRow();
            dr["field_int2"] = 4;
            dr["field_timestamp"] = new DateTime(2003, 01, 30, 14, 0, 0);
            dr["field_numeric"] = 7.3M;
            dt.Rows.Add(dr);

            var ds2 = ds.GetChanges()!;
            da.Update(ds2);

            ds.Merge(ds2);
            ds.AcceptChanges();

            var dr2 = new OpenGaussCommand($"SELECT field_int2, field_numeric, field_timestamp FROM {table}", conn).ExecuteReader();
            dr2.Read();

            Assert.That(dr2[0], Is.EqualTo(4));
            Assert.That(dr2[1], Is.EqualTo(7.3000000M));
            dr2.Close();
        }

        [Test]
        public async Task DataAdapter_update_return_value()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);
            var ds = new DataSet();
            var da = new OpenGaussDataAdapter($"SELECT * FROM {table}", conn);

            da.InsertCommand = new OpenGaussCommand($@"INSERT INTO {table} (field_int2, field_timestamp, field_numeric) VALUES (:a, :b, :c)", conn);

            da.InsertCommand.Parameters.Add(new OpenGaussParameter("a", DbType.Int16));
            da.InsertCommand.Parameters.Add(new OpenGaussParameter("b", DbType.DateTime2));
            da.InsertCommand.Parameters.Add(new OpenGaussParameter("c", DbType.Decimal));

            da.InsertCommand.Parameters[0].Direction = ParameterDirection.Input;
            da.InsertCommand.Parameters[1].Direction = ParameterDirection.Input;
            da.InsertCommand.Parameters[2].Direction = ParameterDirection.Input;

            da.InsertCommand.Parameters[0].SourceColumn = "field_int2";
            da.InsertCommand.Parameters[1].SourceColumn = "field_timestamp";
            da.InsertCommand.Parameters[2].SourceColumn = "field_numeric";

            da.Fill(ds);

            var dt = ds.Tables[0];
            var dr = dt.NewRow();
            dr["field_int2"] = 4;
            dr["field_timestamp"] = new DateTime(2003, 01, 30, 14, 0, 0);
            dr["field_numeric"] = 7.3M;
            dt.Rows.Add(dr);

            dr = dt.NewRow();
            dr["field_int2"] = 4;
            dr["field_timestamp"] = new DateTime(2003, 01, 30, 14, 0, 0);
            dr["field_numeric"] = 7.3M;
            dt.Rows.Add(dr);

            var ds2 = ds.GetChanges()!;
            var daupdate = da.Update(ds2);

            Assert.That(daupdate, Is.EqualTo(2));
        }

        [Test]
        [Ignore("")]
        public async Task DataAdapter_update_return_value2()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);

            var cmd = conn.CreateCommand();
            var da = new OpenGaussDataAdapter($"select * from {table}", conn);
            var cb = new OpenGaussCommandBuilder(da);
            var ds = new DataSet();
            da.Fill(ds);

            //## Insert a new row with id = 1
            ds.Tables[0].Rows.Add(0.4, 0.5);
            da.Update(ds);

            //## change id from 1 to 2
            cmd.CommandText = $"update {table} set field_float4 = 0.8";
            cmd.ExecuteNonQuery();

            //## change value to newvalue
            ds.Tables[0].Rows[0][1] = 0.7;
            //## update should fail, and make a DBConcurrencyException
            var count = da.Update(ds);
            //## count is 1, even if the isn't updated in the database
            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public async Task Fill_with_empty_resultset()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);

            var ds = new DataSet();
            var da = new OpenGaussDataAdapter($"SELECT field_serial, field_int2, field_timestamp, field_numeric FROM {table} WHERE field_serial = -1", conn);

            da.Fill(ds);

            Assert.That(ds.Tables.Count, Is.EqualTo(1));
            Assert.That(ds.Tables[0].Columns.Count, Is.EqualTo(4));
            Assert.That(ds.Tables[0].Columns[0].ColumnName, Is.EqualTo("field_serial"));
            Assert.That(ds.Tables[0].Columns[1].ColumnName, Is.EqualTo("field_int2"));
            Assert.That(ds.Tables[0].Columns[2].ColumnName, Is.EqualTo("field_timestamp"));
            Assert.That(ds.Tables[0].Columns[3].ColumnName, Is.EqualTo("field_numeric"));
        }

        [Test]
        [Ignore("")]
        public async Task Fill_add_with_key()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);

            var ds = new DataSet();
            var da = new OpenGaussDataAdapter($"select field_serial, field_int2, field_timestamp, field_numeric from {table}", conn);

            da.MissingSchemaAction = MissingSchemaAction.AddWithKey;
            da.Fill(ds);

            var field_serial = ds.Tables[0].Columns[0];
            var field_int2 = ds.Tables[0].Columns[1];
            var field_timestamp = ds.Tables[0].Columns[2];
            var field_numeric = ds.Tables[0].Columns[3];

            Assert.That(field_serial.AllowDBNull, Is.False);
            Assert.That(field_serial.AutoIncrement, Is.True);
            Assert.That(field_serial.ColumnName, Is.EqualTo("field_serial"));
            Assert.That(field_serial.DataType, Is.EqualTo(typeof(int)));
            Assert.That(field_serial.Ordinal, Is.EqualTo(0));
            Assert.That(field_serial.Unique, Is.True);

            Assert.That(field_int2.AllowDBNull, Is.True);
            Assert.That(field_int2.AutoIncrement, Is.False);
            Assert.That(field_int2.ColumnName, Is.EqualTo("field_int2"));
            Assert.That(field_int2.DataType, Is.EqualTo(typeof(short)));
            Assert.That(field_int2.Ordinal, Is.EqualTo(1));
            Assert.That(field_int2.Unique, Is.False);

            Assert.That(field_timestamp.AllowDBNull, Is.True);
            Assert.That(field_timestamp.AutoIncrement, Is.False);
            Assert.That(field_timestamp.ColumnName, Is.EqualTo("field_timestamp"));
            Assert.That(field_timestamp.DataType, Is.EqualTo(typeof(DateTime)));
            Assert.That(field_timestamp.Ordinal, Is.EqualTo(2));
            Assert.That(field_timestamp.Unique, Is.False);

            Assert.That(field_numeric.AllowDBNull, Is.True);
            Assert.That(field_numeric.AutoIncrement, Is.True);
            Assert.That(field_numeric.ColumnName, Is.EqualTo("field_numeric"));
            Assert.That(field_numeric.DataType, Is.EqualTo(typeof(decimal)));
            Assert.That(field_numeric.Ordinal, Is.EqualTo(3));
            Assert.That(field_numeric.Unique, Is.False);
        }

        [Test]
        public async Task Fill_add_columns()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);

            var ds = new DataSet();
            var da = new OpenGaussDataAdapter($"SELECT field_serial, field_int2, field_timestamp, field_numeric FROM {table}", conn);

            da.MissingSchemaAction = MissingSchemaAction.Add;
            da.Fill(ds);

            var field_serial = ds.Tables[0].Columns[0];
            var field_int2 = ds.Tables[0].Columns[1];
            var field_timestamp = ds.Tables[0].Columns[2];
            var field_numeric = ds.Tables[0].Columns[3];

            Assert.That(field_serial.ColumnName, Is.EqualTo("field_serial"));
            Assert.That(field_serial.DataType, Is.EqualTo(typeof(int)));
            Assert.That(field_serial.Ordinal, Is.EqualTo(0));

            Assert.That(field_int2.ColumnName, Is.EqualTo("field_int2"));
            Assert.That(field_int2.DataType, Is.EqualTo(typeof(short)));
            Assert.That(field_int2.Ordinal, Is.EqualTo(1));

            Assert.That(field_timestamp.ColumnName, Is.EqualTo("field_timestamp"));
            Assert.That(field_timestamp.DataType, Is.EqualTo(typeof(DateTime)));
            Assert.That(field_timestamp.Ordinal, Is.EqualTo(2));

            Assert.That(field_numeric.ColumnName, Is.EqualTo("field_numeric"));
            Assert.That(field_numeric.DataType, Is.EqualTo(typeof(decimal)));
            Assert.That(field_numeric.Ordinal, Is.EqualTo(3));
        }

        [Test]
        [MonoIgnore("Bug in mono, submitted pull request: https://github.com/mono/mono/pull/1172")]
        public async Task Update_letting_null_field_falue()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);

            var command = new OpenGaussCommand($"INSERT INTO {table} (field_int2) VALUES (2)", conn);
            command.ExecuteNonQuery();

            var ds = new DataSet();

            var da = new OpenGaussDataAdapter($"SELECT * FROM {table}", conn);
            da.InsertCommand = new OpenGaussCommand(";", conn);
            da.UpdateCommand = new OpenGaussCommand($"UPDATE {table} SET field_int2 = :a, field_timestamp = :b, field_numeric = :c WHERE field_serial = :d", conn);

            da.UpdateCommand.Parameters.Add(new OpenGaussParameter("a", DbType.Int16));
            da.UpdateCommand.Parameters.Add(new OpenGaussParameter("b", DbType.DateTime));
            da.UpdateCommand.Parameters.Add(new OpenGaussParameter("c", DbType.Decimal));
            da.UpdateCommand.Parameters.Add(new OpenGaussParameter("d", OpenGaussDbType.Bigint));

            da.UpdateCommand.Parameters[0].Direction = ParameterDirection.Input;
            da.UpdateCommand.Parameters[1].Direction = ParameterDirection.Input;
            da.UpdateCommand.Parameters[2].Direction = ParameterDirection.Input;
            da.UpdateCommand.Parameters[3].Direction = ParameterDirection.Input;

            da.UpdateCommand.Parameters[0].SourceColumn = "field_int2";
            da.UpdateCommand.Parameters[1].SourceColumn = "field_timestamp";
            da.UpdateCommand.Parameters[2].SourceColumn = "field_numeric";
            da.UpdateCommand.Parameters[3].SourceColumn = "field_serial";

            da.Fill(ds);

            var dt = ds.Tables[0];
            Assert.That(dt, Is.Not.Null);

            var dr = ds.Tables[0].Rows[ds.Tables[0].Rows.Count - 1];
            dr["field_int2"] = 4;

            var ds2 = ds.GetChanges()!;
            da.Update(ds2);
            ds.Merge(ds2);
            ds.AcceptChanges();

            using var dr2 = new OpenGaussCommand($"SELECT field_int2 FROM {table}", conn).ExecuteReader();
            dr2.Read();
            Assert.That(dr2["field_int2"], Is.EqualTo(4));
        }

        [Test]
        public async Task Fill_with_duplicate_column_name()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);

            var ds = new DataSet();
            var da = new OpenGaussDataAdapter($"SELECT field_serial, field_serial FROM {table}", conn);
            da.Fill(ds);
        }

        [Test]
        [Ignore("")]
        public Task Update_with_DataSet() => DoUpdateWithDataSet();

        public async Task DoUpdateWithDataSet()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);

            var command = new OpenGaussCommand($"insert into {table} (field_int2) values (2)", conn);
            command.ExecuteNonQuery();

            var ds = new DataSet();
            var da = new OpenGaussDataAdapter($"select * from {table}", conn);
            var cb = new OpenGaussCommandBuilder(da);
            Assert.That(cb, Is.Not.Null);

            da.Fill(ds);

            var dt = ds.Tables[0];
            Assert.That(dt, Is.Not.Null);

            var dr = ds.Tables[0].Rows[ds.Tables[0].Rows.Count - 1];

            dr["field_int2"] = 4;

            var ds2 = ds.GetChanges()!;
            da.Update(ds2);
            ds.Merge(ds2);
            ds.AcceptChanges();

            using var dr2 = new OpenGaussCommand($"select * from {table}", conn).ExecuteReader();
            dr2.Read();
            Assert.That(dr2["field_int2"], Is.EqualTo(4));
        }

        [Test]
        [Ignore("")]
        public async Task Insert_with_CommandBuilder_case_sensitive()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);

            var ds = new DataSet();
            var da = new OpenGaussDataAdapter($"select * from {table}", conn);
            var builder = new OpenGaussCommandBuilder(da);
            Assert.That(builder, Is.Not.Null);

            da.Fill(ds);

            var dt = ds.Tables[0];
            var dr = dt.NewRow();
            dr["Field_Case_Sensitive"] = 4;
            dt.Rows.Add(dr);

            var ds2 = ds.GetChanges()!;
            da.Update(ds2);
            ds.Merge(ds2);
            ds.AcceptChanges();

            using var dr2 = new OpenGaussCommand($"select * from {table}", conn).ExecuteReader();
            dr2.Read();
            Assert.That(dr2[1], Is.EqualTo(4));
        }

        [Test]
        public async Task Interval_as_TimeSpan()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTableName(conn, out var table);
            await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (
    pk SERIAL PRIMARY KEY,
    interval INTERVAL
);
INSERT INTO {table} (interval) VALUES ('1 hour'::INTERVAL);");

            var dt = new DataTable("data");
            var command = new OpenGaussCommand
            {
                CommandType = CommandType.Text,
                CommandText = $"SELECT interval FROM {table}",
                Connection = conn
            };
            var da = new OpenGaussDataAdapter { SelectCommand = command };
            da.Fill(dt);
        }

        [Test]
        public async Task Interval_as_TimeSpan2()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTableName(conn, out var table);
            await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (
    pk SERIAL PRIMARY KEY,
    interval INTERVAL
);
INSERT INTO {table} (interval) VALUES ('1 hour'::INTERVAL);");

            var dt = new DataTable("data");
            //DataColumn c = dt.Columns.Add("dauer", typeof(TimeSpan));
            // DataColumn c = dt.Columns.Add("dauer", typeof(OpenGaussInterval));
            //c.AllowDBNull = true;
            var command = new OpenGaussCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = $"SELECT interval FROM {table}";
            command.Connection = conn;
            var da = new OpenGaussDataAdapter();
            da.SelectCommand = command;
            da.Fill(dt);
        }

        [Test]
        public async Task DataAdapter_command_access()
        {
            using var conn = await OpenConnectionAsync();
            using var command = new OpenGaussCommand("SELECT CAST('1 hour' AS interval) AS dauer", conn);
            var da = new OpenGaussDataAdapter();
            da.SelectCommand = command;
            System.Data.Common.DbDataAdapter common = da;
            Assert.That(common.SelectCommand, Is.Not.Null);
        }

        [Test, Description("Makes sure that the INSERT/UPDATE/DELETE commands are auto-populated on OpenGaussDataAdapter")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/179")]
        [Ignore("Somehow related to us using a temporary table???")]
        public async Task Auto_populate_adapter_commands()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);

            var da = new OpenGaussDataAdapter($"SELECT field_pk,field_int4 FROM {table}", conn);
            var builder = new OpenGaussCommandBuilder(da);
            var ds = new DataSet();
            da.Fill(ds);

            var t = ds.Tables[0];
            var row = t.NewRow();
            row["field_pk"] = 1;
            row["field_int4"] = 8;
            t.Rows.Add(row);
            da.Update(ds);
            Assert.That(await conn.ExecuteScalarAsync($"SELECT field_int4 FROM {table}"), Is.EqualTo(8));

            row["field_int4"] = 9;
            da.Update(ds);
            Assert.That(await conn.ExecuteScalarAsync($"SELECT field_int4 FROM {table}"), Is.EqualTo(9));

            row.Delete();
            da.Update(ds);
            Assert.That(await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}"), Is.EqualTo(0));
        }

        [Test]
        public void Command_builder_quoting()
        {
            var cb = new OpenGaussCommandBuilder();
            const string orig = "some\"column";
            var quoted = cb.QuoteIdentifier(orig);
            Assert.That(quoted, Is.EqualTo("\"some\"\"column\""));
            Assert.That(cb.UnquoteIdentifier(quoted), Is.EqualTo(orig));
        }

        [Test, Description("Makes sure a correct SQL string is built with GetUpdateCommand(true) using correct parameter names and placeholders")]
        [IssueLink("https://github.com/opengauss/opengauss/issues/397")]
        [Ignore("Somehow related to us using a temporary table???")]
        public async Task Get_UpdateCommand()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await SetupTempTable(conn, out var table);

            using var da = new OpenGaussDataAdapter($"SELECT field_pk, field_int4 FROM {table}", conn);
            using var cb = new OpenGaussCommandBuilder(da);
            var updateCommand = cb.GetUpdateCommand(true);
            da.UpdateCommand = updateCommand;

            var ds = new DataSet();
            da.Fill(ds);

            var t = ds.Tables[0];
            var row = t.Rows.Add();
            row["field_pk"] = 1;
            row["field_int4"] = 1;
            da.Update(ds);

            row["field_int4"] = 2;
            da.Update(ds);

            row.Delete();
            da.Update(ds);
        }

        [Test]
        public async Task Load_DataTable()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "char5 CHAR(5), varchar5 VARCHAR(5)", out var table);
            using var command = new OpenGaussCommand($"SELECT char5, varchar5 FROM {table}", conn);
            using var dr = command.ExecuteReader();
            var dt = new DataTable();
            dt.Load(dr);
            dr.Close();

            Assert.That(dt.Columns[0].MaxLength, Is.EqualTo(5));
            Assert.That(dt.Columns[1].MaxLength, Is.EqualTo(5));
        }

        public Task<IAsyncDisposable> SetupTempTable(OpenGaussConnection conn, out string table)
            => CreateTempTable(conn, @"
field_pk SERIAL PRIMARY KEY,
field_serial SERIAL,
field_int2 SMALLINT,
field_int4 INTEGER,
field_numeric NUMERIC,
field_timestamp TIMESTAMP", out table);
    }
}
