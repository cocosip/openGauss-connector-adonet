﻿using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace OpenGauss.Benchmarks
{
    public class ReadArrays
    {
        [Params(true, false)]
        public bool AllNulls;

        [Params(1, 10, 1000, 100000)]
        public int NumElements;

        OpenGaussConnection _intConn = default!;
        OpenGaussCommand _intCmd = default!;
        OpenGaussDataReader _intReader = default!;

        OpenGaussConnection _nullableIntConn = default!;
        OpenGaussCommand _nullableIntCmd = default!;
        OpenGaussDataReader _nullableIntReader = default!;

        OpenGaussConnection _stringConn = default!;
        OpenGaussCommand _stringCmd = default!;
        OpenGaussDataReader _stringReader = default!;

        [GlobalSetup]
        public void Setup()
        {
            var intArray = new int[NumElements];
            for (var i = 0; i < NumElements; i++)
                intArray[i] = 666;
            _intConn = BenchmarkEnvironment.OpenConnection();
            _intCmd = new OpenGaussCommand("SELECT @p1", _intConn);
            _intCmd.Parameters.AddWithValue("p1", intArray);
            _intReader = _intCmd.ExecuteReader();
            _intReader.Read();

            var nullableIntArray = new int?[NumElements];
            for (var i = 0; i < NumElements; i++)
                nullableIntArray[i] = AllNulls ? (int?)null : 666;
            _nullableIntConn = BenchmarkEnvironment.OpenConnection();
            _nullableIntCmd = new OpenGaussCommand("SELECT @p1", _nullableIntConn);
            _nullableIntCmd.Parameters.AddWithValue("p1", nullableIntArray);
            _nullableIntReader = _nullableIntCmd.ExecuteReader();
            _nullableIntReader.Read();

            var stringArray = new string?[NumElements];
            for (var i = 0; i < NumElements; i++)
                stringArray[i] = AllNulls ? null : "666";
            _stringConn = BenchmarkEnvironment.OpenConnection();
            _stringCmd = new OpenGaussCommand("SELECT @p1", _stringConn);
            _stringCmd.Parameters.AddWithValue("p1", stringArray);
            _stringReader = _stringCmd.ExecuteReader();
            _stringReader.Read();
        }

        protected void Cleanup()
        {
            _intReader.Dispose();
            _nullableIntReader.Dispose();
            _stringReader.Dispose();

            _intCmd.Dispose();
            _nullableIntCmd.Dispose();
            _stringCmd.Dispose();

            _intConn.Dispose();
            _nullableIntConn.Dispose();
            _stringConn.Dispose();
        }

        [Benchmark]
        public int ReadIntArray()
            => _intReader.GetFieldValue<int[]>(0).Length;

        [Benchmark]
        public int ReadNullableIntArray()
            => _nullableIntReader.GetFieldValue<int?[]>(0).Length;

        [Benchmark]
        public int ReadStringArray()
            => _stringReader.GetFieldValue<string[]>(0).Length;
    }
}
