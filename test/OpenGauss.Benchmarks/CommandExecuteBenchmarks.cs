﻿using System;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;

// ReSharper disable UnusedMember.Global

namespace OpenGauss.Benchmarks
{
    [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
    [Config(typeof(Config))]
    public class CommandExecuteBenchmarks
    {
        readonly OpenGaussCommand _executeNonQueryCmd;
        readonly OpenGaussCommand _executeNonQueryWithParamCmd;
        readonly OpenGaussCommand _executeNonQueryPreparedCmd;
        readonly OpenGaussCommand _executeScalarCmd;
        readonly OpenGaussCommand _executeReaderCmd;

        public CommandExecuteBenchmarks()
        {
            var conn = BenchmarkEnvironment.OpenConnection();
            _executeNonQueryCmd = new OpenGaussCommand("SET lock_timeout = 1000", conn);
            _executeNonQueryWithParamCmd = new OpenGaussCommand("SET lock_timeout = 1000", conn);
            _executeNonQueryWithParamCmd.Parameters.AddWithValue("not_used", DBNull.Value);
            _executeNonQueryPreparedCmd = new OpenGaussCommand("SET lock_timeout = 1000", conn);
            _executeNonQueryPreparedCmd.Prepare();
            _executeScalarCmd = new OpenGaussCommand("SELECT 1", conn);
            _executeReaderCmd   = new OpenGaussCommand("SELECT 1", conn);
        }

        [Benchmark]
        public int ExecuteNonQuery() => _executeNonQueryCmd.ExecuteNonQuery();

        [Benchmark]
        public int ExecuteNonQueryWithParam() => _executeNonQueryWithParamCmd.ExecuteNonQuery();

        [Benchmark]
        public int ExecuteNonQueryPrepared() => _executeNonQueryPreparedCmd.ExecuteNonQuery();

        [Benchmark]
        public object ExecuteScalar() => _executeScalarCmd.ExecuteScalar()!;

        [Benchmark]
        public object ExecuteReader()
        {
            using (var reader = _executeReaderCmd.ExecuteReader())
            {
                reader.Read();
                return reader.GetValue(0);
            }
        }

        class Config : ManualConfig
        {
            public Config()
            {
                AddColumn(StatisticColumn.OperationsPerSecond);
            }
        }
    }
}
