﻿using System;
using System.Data;
using System.Threading.Tasks;
using OpenGauss.NET;
using OpenGauss.NET.Types;
using NUnit.Framework;
using static OpenGauss.Tests.TestUtil;
using static OpenGauss.NET.Util.Statics;
using OpenGauss.NET.PostgresTypes;

namespace OpenGauss.Tests
{
    class CommandBuilderTests : TestBase
    {
        // TODO: REMOVE ME
        bool IsMultiplexing = false;

        [Test, Description("Tests function parameter derivation with IN, OUT and INOUT parameters")]
        public async Task DeriveParameters_function_various()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = GetTempFunctionName(conn, out var function);

            // This function returns record because of the two Out (InOut & Out) parameters
            await conn.ExecuteNonQueryAsync($@"
                    CREATE OR REPLACE FUNCTION {function}(IN param1 INT, OUT param2 text, INOUT param3 INT) RETURNS record AS
                    '
                    BEGIN
                            param2 = ''sometext'';
                            param3 = param1 + param3;
                    END;
                    ' LANGUAGE 'plpgsql';
                ");

            var cmd = new OpenGaussCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(3));
            Assert.That(cmd.Parameters[0].Direction, Is.EqualTo(ParameterDirection.Input));
            Assert.That(cmd.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer));
            Assert.That(cmd.Parameters[0].PostgresType, Is.TypeOf<PostgresBaseType>());
            Assert.That(cmd.Parameters[0].DataTypeName, Is.EqualTo("integer"));
            Assert.That(cmd.Parameters[0].ParameterName, Is.EqualTo("param1"));
            Assert.That(cmd.Parameters[1].Direction, Is.EqualTo(ParameterDirection.Output));
            Assert.That(cmd.Parameters[1].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Text));
            Assert.That(cmd.Parameters[1].PostgresType, Is.TypeOf<PostgresBaseType>());
            Assert.That(cmd.Parameters[1].DataTypeName, Is.EqualTo("text"));
            Assert.That(cmd.Parameters[1].ParameterName, Is.EqualTo("param2"));
            Assert.That(cmd.Parameters[2].Direction, Is.EqualTo(ParameterDirection.InputOutput));
            Assert.That(cmd.Parameters[2].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer));
            Assert.That(cmd.Parameters[2].PostgresType, Is.TypeOf<PostgresBaseType>());
            Assert.That(cmd.Parameters[2].DataTypeName, Is.EqualTo("integer"));
            Assert.That(cmd.Parameters[2].ParameterName, Is.EqualTo("param3"));
            cmd.Parameters[0].Value = 5;
            cmd.Parameters[2].Value = 4;
            cmd.ExecuteNonQuery();
            Assert.That(cmd.Parameters[0].Value, Is.EqualTo(5));
            Assert.That(cmd.Parameters[1].Value, Is.EqualTo("sometext"));
            Assert.That(cmd.Parameters[2].Value, Is.EqualTo(9));

            await conn.ExecuteNonQueryAsync($"DROP FUNCTION IF EXISTS {function}(IN param1 INT, OUT param2 text, INOUT param3 INT)");
        }

        [Test, Description("Tests function parameter derivation with IN-only parameters")]
        public async Task DeriveParameters_function_in_only()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = GetTempFunctionName(conn, out var function);

            // This function returns record because of the two Out (InOut & Out) parameters
            await conn.ExecuteNonQueryAsync($@"
                    CREATE OR REPLACE FUNCTION {function}(IN param1 INT, IN param2 INT) RETURNS int AS
                    '
                    BEGIN
                    RETURN param1 + param2;
                    END;
                    ' LANGUAGE 'plpgsql';
                ");

            var cmd = new OpenGaussCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(2));
            Assert.That(cmd.Parameters[0].Direction, Is.EqualTo(ParameterDirection.Input));
            Assert.That(cmd.Parameters[1].Direction, Is.EqualTo(ParameterDirection.Input));
            cmd.Parameters[0].Value = 5;
            cmd.Parameters[1].Value = 4;
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(9));

            await conn.ExecuteNonQueryAsync($"DROP FUNCTION IF EXISTS {function}(IN param1 INT, IN param2 INT)");
        }

        [Test, Description("Tests function parameter derivation with no parameters")]
        public async Task DeriveParameters_function_no_params()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = GetTempFunctionName(conn, out var function);


            // This function returns record because of the two Out (InOut & Out) parameters
            await conn.ExecuteNonQueryAsync($@"
                    CREATE OR REPLACE FUNCTION {function}() RETURNS int AS
                    '
                    BEGIN
                    RETURN 4;
                    END;
                    ' LANGUAGE 'plpgsql';
                ");

            var cmd = new OpenGaussCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Is.Empty);

            await conn.ExecuteNonQueryAsync($"DROP FUNCTION IF EXISTS {function}()");
        }

        [Test]
        public async Task DeriveParameters_function_with_case_sensitive_name()
        {
            using var conn = await OpenConnectionAsync();
            await conn.ExecuteNonQueryAsync(
                @"CREATE OR REPLACE FUNCTION ""FunctionCaseSensitive""(int4, text) returns int4 as
                              $BODY$
                              begin
                                return 0;
                              end
                              $BODY$
                              language 'plpgsql';");
            await using var _ = DeferAsync(() => conn.ExecuteNonQueryAsync(@"DROP FUNCTION ""FunctionCaseSensitive"""));

            var command = new OpenGaussCommand(@"""FunctionCaseSensitive""", conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(command);
            Assert.That(command.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer));
            Assert.That(command.Parameters[1].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Text));
        }

        [Test]
        public async Task DeriveParameters_function_parameter_name_from_function()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = GetTempFunctionName(conn, out var function);


            await conn.ExecuteNonQueryAsync($@"CREATE OR REPLACE FUNCTION {function}(x int, y int, out sum int, out product int) as 'select $1 + $2, $1 * $2' language 'sql';");
            var command = new OpenGaussCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(command);
            Assert.That(command.Parameters[0].ParameterName, Is.EqualTo("x"));
            Assert.That(command.Parameters[1].ParameterName, Is.EqualTo("y"));

            await conn.ExecuteNonQueryAsync($"DROP FUNCTION IF EXISTS {function}(x int, y int, out sum int, out product int)");
        }

        [Test]
        public async Task DeriveParameters_non_existing_function()
        {
            using var conn = await OpenConnectionAsync();
            var invalidCommandName = new OpenGaussCommand("invalidfunctionname", conn) { CommandType = CommandType.StoredProcedure };
            Assert.That(() => OpenGaussCommandBuilder.DeriveParameters(invalidCommandName),
                Throws.Exception.TypeOf<PostgresException>()
                    .With.Property(nameof(PostgresException.SqlState)).EqualTo("42883"));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1212")]
        public async Task DeriveParameters_function_with_table_parameters()
        {
            using var conn = await OpenConnectionAsync();
            MinimumPgVersion(conn, "9.2.0");
            await using var _ = GetTempFunctionName(conn, out var function);

            // This function returns record because of the two Out (InOut & Out) parameters
            await conn.ExecuteNonQueryAsync($@"
                    CREATE FUNCTION {function}(IN in1 INT) RETURNS TABLE(t1 INT, t2 INT) AS
                      'SELECT in1,in1+1' LANGUAGE 'sql';
                ");

            var cmd = new OpenGaussCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(3));
            Assert.That(cmd.Parameters[0].Direction, Is.EqualTo(ParameterDirection.Input));
            Assert.That(cmd.Parameters[1].Direction, Is.EqualTo(ParameterDirection.Output));
            Assert.That(cmd.Parameters[2].Direction, Is.EqualTo(ParameterDirection.Output));
            cmd.Parameters[0].Value = 5;
            cmd.ExecuteNonQuery();
            Assert.That(cmd.Parameters[1].Value, Is.EqualTo(5));
            Assert.That(cmd.Parameters[2].Value, Is.EqualTo(6));

            await conn.ExecuteNonQueryAsync($"DROP FUNCTION IF EXISTS {function}(t1 INT, t2 INT)");
        }

        [Test, Description("Tests function parameter derivation for quoted functions with double quotes in the name works")]
        public async Task DeriveParameters_quote_characters_in_function_name()
        {
            using var conn = await OpenConnectionAsync();
            var function = @"""""""FunctionQuote""""CharactersInName""""""";
            await conn.ExecuteNonQueryAsync(
                $@"CREATE OR REPLACE FUNCTION {function}(int4, text) returns int4 as
                              $BODY$
                              begin
                                return 0;
                              end
                              $BODY$
                              language 'plpgsql';");
            await using var _ = DeferAsync(() => conn.ExecuteNonQueryAsync("DROP FUNCTION " + function));

            var command = new OpenGaussCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(command);
            Assert.That(command.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer));
            Assert.That(command.Parameters[1].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Text));
        }

        [Test, Description("Tests function parameter derivation for quoted functions with dots in the name works")]
        public async Task DeriveParameters_dot_character_in_function_name()
        {
            using var conn = await OpenConnectionAsync();
            await conn.ExecuteNonQueryAsync(
                @"CREATE OR REPLACE FUNCTION ""My.Dotted.Function""(int4, text) returns int4 as
                              $BODY$
                              begin
                                return 0;
                              end
                              $BODY$
                              language 'plpgsql';");
            await using var _ = DeferAsync(() => conn.ExecuteNonQueryAsync(@"DROP FUNCTION ""My.Dotted.Function"""));

            var command = new OpenGaussCommand(@"""My.Dotted.Function""", conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(command);
            Assert.That(command.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer));
            Assert.That(command.Parameters[1].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Text));
        }

        [Test, Description("Tests if the right function according to search_path is used in function parameter derivation")]
        public async Task DeriveParameters_function_correct_schema_resolution()
        {
            if (IsMultiplexing)
                return;  // Uses search_path

            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempSchema(conn, out var schema1);
            await using var __ = await CreateTempSchema(conn, out var schema2);

            await conn.ExecuteNonQueryAsync(
                $@"
CREATE FUNCTION {schema1}.redundantfunc() RETURNS int AS
$BODY$
BEGIN
    RETURN 1;
END;
$BODY$
LANGUAGE 'plpgsql';

CREATE FUNCTION {schema2}.redundantfunc(IN param1 INT, IN param2 INT) RETURNS int AS
$BODY$
BEGIN
RETURN param1 + param2;
END;
$BODY$
LANGUAGE 'plpgsql';

SET search_path TO {schema2};
");
            var command = new OpenGaussCommand("redundantfunc", conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(command);
            Assert.That(command.Parameters, Has.Count.EqualTo(2));
            Assert.That(command.Parameters[0].Direction, Is.EqualTo(ParameterDirection.Input));
            Assert.That(command.Parameters[1].Direction, Is.EqualTo(ParameterDirection.Input));
            command.Parameters[0].Value = 5;
            command.Parameters[1].Value = 4;
            Assert.That(command.ExecuteScalar(), Is.EqualTo(9));
        }

        [Test, Description("Tests if function parameter derivation throws an exception if the specified function is not in the search_path")]
        public async Task DeriveParameters_throws_for_existing_function_that_is_not_in_search_path()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempSchema(conn, out var schema);

            await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {schema}.schema1func() RETURNS int AS
$BODY$
BEGIN
    RETURN 1;
END;
$BODY$
LANGUAGE 'plpgsql';

RESET search_path;
");
            var command = new OpenGaussCommand("schema1func", conn) { CommandType = CommandType.StoredProcedure };
            Assert.That(() => OpenGaussCommandBuilder.DeriveParameters(command),
                Throws.Exception.TypeOf<PostgresException>()
                    .With.Property(nameof(PostgresException.SqlState)).EqualTo("42883"));
        }

        [Test, Description("Tests if an exception is thrown if multiple functions with the specified name are in the search_path")]
        public async Task DeriveParameters_throws_for_multiple_function_name_hits_in_search_path()
        {
            if (IsMultiplexing)
                return;  // Uses search_path

            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempSchema(conn, out var schema1);
            await using var __ = await CreateTempSchema(conn, out var schema2);

            await conn.ExecuteNonQueryAsync(
                $@"
CREATE FUNCTION {schema1}.redundantfunc() RETURNS int AS
$BODY$
BEGIN
    RETURN 1;
END;
$BODY$
LANGUAGE 'plpgsql';

CREATE OR REPLACE FUNCTION {schema1}.redundantfunc(IN param1 INT, IN param2 INT) RETURNS int AS
$BODY$
BEGIN
RETURN param1 + param2;
END;
$BODY$
LANGUAGE 'plpgsql';

SET search_path TO {schema1}, {schema2};
");
            var command = new OpenGaussCommand("redundantfunc", conn) { CommandType = CommandType.StoredProcedure };
            Assert.That(() => OpenGaussCommandBuilder.DeriveParameters(command),
                Throws.Exception.TypeOf<PostgresException>()
                    .With.Property(nameof(PostgresException.SqlState)).EqualTo("42725"));
        }

        #region Set returning functions

        [Test, Description("Tests parameter derivation for a function that returns SETOF sometype")]
        public async Task DeriveParameters_function_returning_setof_type()
        {
            using var conn = await OpenConnectionAsync();
            MinimumPgVersion(conn, "9.2.0");

            await using var _ = await GetTempTableName(conn, out var table);
            await using var __ = GetTempFunctionName(conn, out var function);

            // This function returns record because of the two Out (InOut & Out) parameters
            await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (fooid int, foosubid int, fooname text);

INSERT INTO {table} VALUES
(1, 1, 'Joe'),
(1, 2, 'Ed'),
(2, 1, 'Mary');

CREATE FUNCTION {function}(int) RETURNS SETOF {table} AS $$
    SELECT * FROM {table} WHERE {table}.fooid = $1 ORDER BY {table}.foosubid;
$$ LANGUAGE SQL;
                ");

            var cmd = new OpenGaussCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(4));
            Assert.That(cmd.Parameters[0].Direction, Is.EqualTo(ParameterDirection.Input));
            Assert.That(cmd.Parameters[1].Direction, Is.EqualTo(ParameterDirection.Output));
            Assert.That(cmd.Parameters[2].Direction, Is.EqualTo(ParameterDirection.Output));
            Assert.That(cmd.Parameters[3].Direction, Is.EqualTo(ParameterDirection.Output));
            cmd.Parameters[0].Value = 1;
            cmd.ExecuteNonQuery();
            Assert.That(cmd.Parameters[0].Value, Is.EqualTo(1));

            await conn.ExecuteNonQueryAsync($"DROP FUNCTION IF EXISTS {function}(int)");
        }

        [Test, Description("Tests parameter derivation for a function that returns TABLE")]
        public async Task DeriveParameters_function_returning_table()
        {
            using var conn = await OpenConnectionAsync();
            MinimumPgVersion(conn, "9.2.0");

            await using var _ = await GetTempTableName(conn, out var table);
            await using var __ = GetTempFunctionName(conn, out var function);

            // This function returns record because of the two Out (InOut & Out) parameters
            await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (fooid int, foosubid int, fooname text);

INSERT INTO {table} VALUES
(1, 1, 'Joe'),
(1, 2, 'Ed'),
(2, 1, 'Mary');

CREATE OR REPLACE FUNCTION {function}(int) RETURNS TABLE(fooid int, foosubid int, fooname text) AS $$
    SELECT * FROM {table} WHERE {table}.fooid = $1 ORDER BY {table}.foosubid;
$$ LANGUAGE SQL;
                ");


            var cmd = new OpenGaussCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(4));
            Assert.That(cmd.Parameters[0].Direction, Is.EqualTo(ParameterDirection.Input));
            Assert.That(cmd.Parameters[1].Direction, Is.EqualTo(ParameterDirection.Output));
            Assert.That(cmd.Parameters[2].Direction, Is.EqualTo(ParameterDirection.Output));
            Assert.That(cmd.Parameters[3].Direction, Is.EqualTo(ParameterDirection.Output));
            cmd.Parameters[0].Value = 1;
            cmd.ExecuteNonQuery();
            Assert.That(cmd.Parameters[0].Value, Is.EqualTo(1));

            await conn.ExecuteNonQueryAsync($"DROP FUNCTION IF EXISTS {function}(int)");
        }

        [Test, Description("Tests parameter derivation for a function that returns SETOF record")]
        public async Task DeriveParameters_function_returning_setof_record()
        {
            using var conn = await OpenConnectionAsync();
            MinimumPgVersion(conn, "9.2.0");

            await using var _ = await GetTempTableName(conn, out var table);
            await using var __ = GetTempFunctionName(conn, out var function);

            // This function returns record because of the two Out (InOut & Out) parameters
            await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (fooid int, foosubid int, fooname text);

INSERT INTO {table} VALUES
(1, 1, 'Joe'),
(1, 2, 'Ed'),
(2, 1, 'Mary');

CREATE FUNCTION {function}(int, OUT fooid int, OUT foosubid int, OUT fooname text) RETURNS SETOF record AS $$
    SELECT * FROM {table} WHERE {table}.fooid = $1 ORDER BY {table}.foosubid;
$$ LANGUAGE SQL;
                ");

            var cmd = new OpenGaussCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(4));
            Assert.That(cmd.Parameters[0].Direction, Is.EqualTo(ParameterDirection.Input));
            Assert.That(cmd.Parameters[1].Direction, Is.EqualTo(ParameterDirection.Output));
            Assert.That(cmd.Parameters[2].Direction, Is.EqualTo(ParameterDirection.Output));
            Assert.That(cmd.Parameters[3].Direction, Is.EqualTo(ParameterDirection.Output));
            cmd.Parameters[0].Value = 1;
            cmd.ExecuteNonQuery();
            Assert.That(cmd.Parameters[0].Value, Is.EqualTo(1));

            await conn.ExecuteNonQueryAsync($"DROP FUNCTION IF EXISTS {function}(int, OUT fooid int, OUT foosubid int, OUT fooname text)");
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/2022")]
        public async Task DeriveParameters_function_returning_setof_type_with_dropped_column()
        {
            using var conn = await OpenConnectionAsync();
            MinimumPgVersion(conn, "9.2.0");

            await using var _ = await GetTempTableName(conn, out var table);
            await using var __ = GetTempFunctionName(conn, out var function);


            await conn.ExecuteNonQueryAsync($@"
                    CREATE TABLE {table} (id serial PRIMARY KEY, t1 text, t2 text);
                    CREATE OR REPLACE FUNCTION {function}() RETURNS SETOF {table} AS $$
                        SELECT * FROM {table}
                    $$LANGUAGE SQL;
                    ALTER TABLE {table} DROP t2;
                ");

            var cmd = new OpenGaussCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(2));
            Assert.That(cmd.Parameters[0].Direction, Is.EqualTo(ParameterDirection.Output));
            Assert.That(cmd.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer));
            Assert.That(cmd.Parameters[1].Direction, Is.EqualTo(ParameterDirection.Output));
            Assert.That(cmd.Parameters[1].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Text));

            await conn.ExecuteNonQueryAsync($"DROP FUNCTION IF EXISTS {function}()");
        }

        #endregion

        #region CommandType.Text

        [Test, Description("Tests parameter derivation for parameterized queries (CommandType.Text)")]
        public async Task DeriveParameters_text_one_parameter_with_same_type()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "id int, val text", out var table);

            var cmd = new OpenGaussCommand(
                $@"INSERT INTO {table} VALUES(:x, 'some value');
                    UPDATE {table} SET val = 'changed value' WHERE id = :x;
                    SELECT val FROM {table} WHERE id = :x;",
                conn);
            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(1));
            Assert.That(cmd.Parameters[0].Direction, Is.EqualTo(ParameterDirection.Input));
            Assert.That(cmd.Parameters[0].ParameterName, Is.EqualTo("x"));
            Assert.That(cmd.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer));
            cmd.Parameters[0].Value = 42;
            var retVal = await cmd.ExecuteScalarAsync();
            Assert.That(retVal, Is.EqualTo("changed value"));
        }

        [Test, Description("Tests parameter derivation for parameterized queries (CommandType.Text) where different types would be inferred for placeholders with the same name.")]
        public async Task DeriveParameters_text_one_parameter_with_different_types()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "id int, val text", out var table);

            var cmd = new OpenGaussCommand(
                $@"INSERT INTO {table} VALUES(:x, 'some value');
                    UPDATE {table} SET val = 'changed value' WHERE id = :x::double precision;
                    SELECT val FROM {table} WHERE id = :x::numeric;",
                conn);
            var ex = Assert.Throws<OpenGaussException>(() => OpenGaussCommandBuilder.DeriveParameters(cmd))!;
            Assert.That(ex.Message, Is.EqualTo("The backend parser inferred different types for parameters with the same name. Please try explicit casting within your SQL statement or batch or use different placeholder names."));
        }

        [Test, Description("Tests parameter derivation for parameterized queries (CommandType.Text) with multiple parameters")]
        public async Task DeriveParameters_multiple_parameters()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await CreateTempTable(conn, "id int, val text", out var table);

            var cmd = new OpenGaussCommand(
                $@"INSERT INTO {table} VALUES(:x, 'some value');
                    UPDATE {table} SET val = 'changed value' WHERE id = @y::double precision;
                    SELECT val FROM {table} WHERE id = :z::numeric;",
                conn);
            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(3));
            Assert.That(cmd.Parameters[0].ParameterName, Is.EqualTo("x"));
            Assert.That(cmd.Parameters[1].ParameterName, Is.EqualTo("y"));
            Assert.That(cmd.Parameters[2].ParameterName, Is.EqualTo("z"));
            Assert.That(cmd.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer));
            Assert.That(cmd.Parameters[1].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Double));
            Assert.That(cmd.Parameters[2].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Numeric));

            cmd.Parameters[0].Value = 42;
            cmd.Parameters[1].Value = 42d;
            cmd.Parameters[2].Value = 42;
            var retVal = await cmd.ExecuteScalarAsync();
            Assert.That(retVal, Is.EqualTo("changed value"));
        }

        [Test, Description("Tests parameter derivation a parameterized query (CommandType.Text) that is already prepared.")]
        public async Task DeriveParameters_text_prepared_statement()
        {
            const string query = "SELECT @p::integer";
            const int answer = 42;
            using var conn = await OpenConnectionAsync();
            using var cmd = new OpenGaussCommand(query, conn);
            cmd.Parameters.AddWithValue("@p", OpenGaussDbType.Integer, answer);
            cmd.Prepare();
            Assert.That(conn.Connector!.PreparedStatementManager.NumPrepared, Is.EqualTo(1));

            var ex = Assert.Throws<OpenGaussException>(() =>
            {
                // Derive parameters for the already prepared statement
                OpenGaussCommandBuilder.DeriveParameters(cmd);

            })!;

            Assert.That(ex.Message, Is.EqualTo("Deriving parameters isn't supported for commands that are already prepared."));

            // We leave the command intact when throwing so it should still be useable
            Assert.That(cmd.Parameters.Count, Is.EqualTo(1));
            Assert.That(cmd.Parameters[0].ParameterName, Is.EqualTo("@p"));
            Assert.That(conn.Connector.PreparedStatementManager.NumPrepared, Is.EqualTo(1));
            cmd.Parameters["@p"].Value = answer;
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(answer));

            conn.UnprepareAll();
        }

        [Test, Description("Tests parameter derivation for array parameters in parameterized queries (CommandType.Text)")]
        public async Task DeriveParameters_text_array()
        {
            using var conn = await OpenConnectionAsync();
            var cmd = new OpenGaussCommand("SELECT :a::integer[]", conn);
            var val = new[] { 7, 42 };

            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(1));
            Assert.That(cmd.Parameters[0].ParameterName, Is.EqualTo("a"));
            Assert.That(cmd.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer | OpenGaussDbType.Array));
            cmd.Parameters[0].Value = val;
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetFieldValue<int[]>(0), Is.EqualTo(val));
        }

        [Test, Description("Tests parameter derivation for domain parameters in parameterized queries (CommandType.Text)")]
        public async Task DeriveParameters_text_domain()
        {
            using var conn = await OpenConnectionAsync();
            MinimumPgVersion(conn, "11.0", "Arrays of domains and domains over arrays were introduced in PostgreSQL 11");
            await using var _ = await GetTempTypeName(conn, out var domainType);
            await using var __ = await GetTempTypeName(conn, out var domainArrayType);
            await conn.ExecuteNonQueryAsync($@"
CREATE DOMAIN {domainType} AS integer CHECK (VALUE > 0);
CREATE DOMAIN {domainArrayType} AS int[] CHECK(array_length(VALUE, 1) = 2);");
            conn.ReloadTypes();

            var cmd = new OpenGaussCommand($"SELECT :a::{domainType}, :b::{domainType}[], :c::{domainArrayType}", conn);
            var val = 23;
            var arrayVal = new[] { 7, 42 };

            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(3));
            Assert.That(cmd.Parameters[0].ParameterName, Is.EqualTo("a"));
            Assert.That(cmd.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer));
            Assert.That(cmd.Parameters[0].DataTypeName, Does.EndWith(domainType));
            Assert.That(cmd.Parameters[1].ParameterName, Is.EqualTo("b"));
            Assert.That(cmd.Parameters[1].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer | OpenGaussDbType.Array));
            Assert.That(cmd.Parameters[1].DataTypeName, Does.EndWith(domainType + "[]"));
            Assert.That(cmd.Parameters[2].ParameterName, Is.EqualTo("c"));
            Assert.That(cmd.Parameters[2].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Integer | OpenGaussDbType.Array));
            Assert.That(cmd.Parameters[2].DataTypeName, Does.EndWith(domainArrayType));
            cmd.Parameters[0].Value = val;
            cmd.Parameters[1].Value = arrayVal;
            cmd.Parameters[2].Value = arrayVal;
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(val));
            Assert.That(reader.GetFieldValue<int[]>(1), Is.EqualTo(arrayVal));
            Assert.That(reader.GetFieldValue<int[]>(2), Is.EqualTo(arrayVal));
        }

        [Test, Description("Tests parameter derivation for unmapped enum parameters in parameterized queries (CommandType.Text)")]
        public async Task DeriveParameters_text_unmapped_enum()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTypeName(conn, out var type);
            await conn.ExecuteNonQueryAsync($@"CREATE TYPE {type} AS ENUM ('Apple', 'Cherry', 'Plum')");
            conn.ReloadTypes();

            var cmd = new OpenGaussCommand($"SELECT :x::{type}", conn);
            const string val1 = "Apple";
            var val2 = new string[] { "Cherry", "Plum" };

            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(1));
            Assert.That(cmd.Parameters[0].ParameterName, Is.EqualTo("x"));
            Assert.That(cmd.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown));
            Assert.That(cmd.Parameters[0].PostgresType, Is.InstanceOf<PostgresEnumType>());
            Assert.That(cmd.Parameters[0].PostgresType!.Name, Is.EqualTo(type));
            Assert.That(cmd.Parameters[0].DataTypeName, Does.EndWith(type));
            cmd.Parameters[0].Value = val1;
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo(val1));
        }

        enum Fruit { Apple, Cherry, Plum }

        [Test, Description("Tests parameter derivation for mapped enum parameters in parameterized queries (CommandType.Text)")]
        public async Task DeriveParameters_text_mapped_enum()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTypeName(conn, out var type);
            await conn.ExecuteNonQueryAsync($@"CREATE TYPE {type} AS ENUM ('apple', 'cherry', 'plum')");
            conn.ReloadTypes();
            conn.TypeMapper.MapEnum<Fruit>(type);

            var cmd = new OpenGaussCommand($"SELECT :x::{type}, :y::{type}[]", conn);
            const Fruit val1 = Fruit.Apple;
            var val2 = new Fruit[] { Fruit.Cherry, Fruit.Plum };

            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(2));
            Assert.That(cmd.Parameters[0].ParameterName, Is.EqualTo("x"));
            Assert.That(cmd.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown));
            Assert.That(cmd.Parameters[0].PostgresType, Is.InstanceOf<PostgresEnumType>());
            Assert.That(cmd.Parameters[0].DataTypeName, Does.EndWith(type));
            Assert.That(cmd.Parameters[1].ParameterName, Is.EqualTo("y"));
            Assert.That(cmd.Parameters[1].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown));
            Assert.That(cmd.Parameters[1].PostgresType, Is.InstanceOf<PostgresArrayType>());
            Assert.That(cmd.Parameters[1].DataTypeName, Does.EndWith(type + "[]"));
            cmd.Parameters[0].Value = val1;
            cmd.Parameters[1].Value = val2;
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetFieldValue<Fruit>(0), Is.EqualTo(val1));
            Assert.That(reader.GetFieldValue<Fruit[]>(1), Is.EqualTo(val2));
        }

        class SomeComposite
        {
            public int X { get; set; }

            [PgName("some_text")]
            public string SomeText { get; set; } = "";
        }

        [Test]
        public async Task DeriveParameters_text_mapped_composite()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTypeName(conn, out var type);
            await conn.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (x int, some_text text)");
            conn.ReloadTypes();
            conn.TypeMapper.MapComposite<SomeComposite>(type);

            var expected1 = new SomeComposite { X = 8, SomeText = "foo" };
            var expected2 = new[] {
                expected1,
                new SomeComposite {X = 9, SomeText = "bar"}
            };

            using var cmd = new OpenGaussCommand($"SELECT @p1::{type}, @p2::{type}[]", conn);
            OpenGaussCommandBuilder.DeriveParameters(cmd);
            Assert.That(cmd.Parameters, Has.Count.EqualTo(2));
            Assert.That(cmd.Parameters[0].ParameterName, Is.EqualTo("p1"));
            Assert.That(cmd.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown));
            Assert.That(cmd.Parameters[0].PostgresType, Is.InstanceOf<PostgresCompositeType>());
            Assert.That(cmd.Parameters[0].DataTypeName, Does.EndWith(type));
            var p1Fields = ((PostgresCompositeType)cmd.Parameters[0].PostgresType!).Fields;
            Assert.That(p1Fields[0].Name, Is.EqualTo("x"));
            Assert.That(p1Fields[1].Name, Is.EqualTo("some_text"));

            Assert.That(cmd.Parameters[1].ParameterName, Is.EqualTo("p2"));
            Assert.That(cmd.Parameters[1].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Unknown));
            Assert.That(cmd.Parameters[1].PostgresType, Is.InstanceOf<PostgresArrayType>());
            Assert.That(cmd.Parameters[1].DataTypeName, Does.EndWith(type + "[]"));
            var p2Element = ((PostgresArrayType)cmd.Parameters[1].PostgresType!).Element;
            Assert.That(p2Element, Is.InstanceOf<PostgresCompositeType>());
            Assert.That(p2Element.Name, Is.EqualTo(type));
            var p2Fields = ((PostgresCompositeType)p2Element).Fields;
            Assert.That(p2Fields[0].Name, Is.EqualTo("x"));
            Assert.That(p2Fields[1].Name, Is.EqualTo("some_text"));

            cmd.Parameters[0].Value = expected1;
            cmd.Parameters[1].Value = expected2;
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetFieldValue<SomeComposite>(0).SomeText, Is.EqualTo(expected1.SomeText));
            Assert.That(reader.GetFieldValue<SomeComposite>(0).X, Is.EqualTo(expected1.X));
            for (var i = 0; i < 2; i++)
            {
                Assert.That(reader.GetFieldValue<SomeComposite[]>(1)[i].SomeText, Is.EqualTo(expected2[i].SomeText));
                Assert.That(reader.GetFieldValue<SomeComposite[]>(1)[i].X, Is.EqualTo(expected2[i].X));
            }
        }

        #endregion

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/1591")]
        public async Task Get_update_command_infers_parameters_with_NpgsqDbType()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTableName(conn, out var table);
            await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (
    Cod varchar(5) NOT NULL,
    Descr varchar(40),
    Data date,
    DataOra timestamp,
    Intero smallInt NOT NULL,
    Decimale money,
    Singolo float,
    Booleano bit,
    Nota varchar(255),
    BigIntArr bigint[],
    VarCharArr character varying(20)[],
    PRIMARY KEY (Cod)
);
INSERT INTO {table} VALUES('key1', 'description', '2018-07-03', '2018-07-03 07:02:00', 123, 123.4, 1234.5, B'1', 'note')");

            var daDataAdapter =
                new OpenGaussDataAdapter(
                    $"SELECT Cod, Descr, Data, DataOra, Intero, Decimale, Singolo, Booleano, Nota, BigIntArr, VarCharArr FROM {table}", conn);

            var cbCommandBuilder = new OpenGaussCommandBuilder(daDataAdapter);
            var dtTable = new DataTable();

            daDataAdapter.InsertCommand = cbCommandBuilder.GetInsertCommand();
            daDataAdapter.UpdateCommand = cbCommandBuilder.GetUpdateCommand();
            daDataAdapter.DeleteCommand = cbCommandBuilder.GetDeleteCommand();

            Assert.That(daDataAdapter.UpdateCommand.Parameters[0].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Varchar));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[1].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Varchar));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[2].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[3].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[4].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Smallint));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[5].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Money));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[6].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Double));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[7].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Bit));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[8].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Varchar));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[9].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Array | OpenGaussDbType.Bigint));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[10].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Array | OpenGaussDbType.Varchar));

            Assert.That(daDataAdapter.UpdateCommand.Parameters[11].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Varchar));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[13].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Varchar));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[15].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[17].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Timestamp));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[18].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Smallint));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[20].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Money));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[22].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Double));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[24].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Bit));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[26].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Varchar));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[28].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Array | OpenGaussDbType.Bigint));
            Assert.That(daDataAdapter.UpdateCommand.Parameters[30].OpenGaussDbType, Is.EqualTo(OpenGaussDbType.Array | OpenGaussDbType.Varchar));

            daDataAdapter.Fill(dtTable);

            var row = dtTable.Rows[0];

            Assert.That(row[0], Is.EqualTo("key1"));
            Assert.That(row[1], Is.EqualTo("description"));
            Assert.That(row[2], Is.EqualTo(new DateTime(2018, 7, 3)));
            Assert.That(row[3], Is.EqualTo(new DateTime(2018, 7, 3, 7, 2, 0)));
            Assert.That(row[4], Is.EqualTo(123));
            Assert.That(row[5], Is.EqualTo(123.4));
            Assert.That(row[6], Is.EqualTo(1234.5));
            Assert.That(row[7], Is.EqualTo(true));
            Assert.That(row[8], Is.EqualTo("note"));

            dtTable.Rows[0]["Singolo"] = 1.1D;

            Assert.That(daDataAdapter.Update(dtTable), Is.EqualTo(1));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/2560")]
        public async Task Get_update_command_with_column_aliases()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTableName(conn, out var table);

            await conn.ExecuteNonQueryAsync($@"
CREATE TEMP TABLE {table} (
    Cod varchar(5) NOT NULL,
    Descr varchar(40),
    Data date,
    CONSTRAINT PK_test_Cod PRIMARY KEY (Cod)
)");

            using var cmd = new OpenGaussCommand($"SELECT Cod as CodAlias, Descr as DescrAlias, Data as DataAlias FROM {table}", conn);
            using var daDataAdapter = new OpenGaussDataAdapter(cmd);
            using var cbCommandBuilder = new OpenGaussCommandBuilder(daDataAdapter);

            daDataAdapter.UpdateCommand = cbCommandBuilder.GetUpdateCommand();
            Assert.That(daDataAdapter.UpdateCommand.CommandText.Contains("SET \"cod\" = @p1, \"descr\" = @p2, \"data\" = @p3 WHERE ((\"cod\" = @p4) AND ((@p5 = 1 AND \"descr\" IS NULL) OR (\"descr\" = @p6)) AND ((@p7 = 1 AND \"data\" IS NULL) OR (\"data\" = @p8)))"), Is.True);
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/2846")]
        public async Task Get_update_command_with_array_column_type()
        {
            using var conn = await OpenConnectionAsync();
            await using var _ = await GetTempTableName(conn, out var table);
            await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (
Cod varchar(5) NOT NULL,
Vettore character varying(20)[],
CONSTRAINT PK_test_Cod PRIMARY KEY (Cod)
)");
            using var daDataAdapter = new OpenGaussDataAdapter($"SELECT cod, vettore FROM {table} ORDER By cod", conn);
            using var cbCommandBuilder = new OpenGaussCommandBuilder(daDataAdapter);
            var dtTable = new DataTable();

            cbCommandBuilder.SetAllValues = true;

            daDataAdapter.UpdateCommand = cbCommandBuilder.GetUpdateCommand();

            daDataAdapter.Fill(dtTable);
            dtTable.Rows.Add();
            dtTable.Rows[0]["cod"] = '0';
            dtTable.Rows[0]["vettore"] = new[] { "aaa", "bbb" };

            daDataAdapter.Update(dtTable);
        }
    }
}

