﻿using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoJSON.Net;
using GeoJSON.Net.Converters;
using GeoJSON.Net.CoordinateReferenceSystem;
using GeoJSON.Net.Geometry;
using Newtonsoft.Json;
using OpenGauss.GeoJSON.Internal;
using OpenGauss.Tests;
using NUnit.Framework;

#if TestGeoJSONTests
namespace OpenGauss.PluginTests
{
    public class GeoJSONTests : TestBase
    {
        public struct TestData
        {
            public GeoJSONObject Geometry;
            public string CommandText;
        }

        public static readonly TestData[] Tests =
        {
            new()
            {
                Geometry = new Point(
                    new Position(longitude: 1d, latitude: 2d))
                { BoundingBoxes = new[] { 1d, 2d, 1d, 2d } },
                CommandText = "st_makepoint(1,2)"
            },
            new()
            {
                Geometry = new LineString(new[] {
                    new Position(longitude: 1d, latitude: 1d),
                    new Position(longitude: 1d, latitude: 2d)
                })
                { BoundingBoxes = new[] { 1d, 1d, 1d, 2d } },
                CommandText = "st_makeline(st_makepoint(1,1), st_makepoint(1,2))"
            },
            new()
            {
                Geometry = new Polygon(new[] {
                    new LineString(new[] {
                        new Position(longitude: 1d, latitude: 1d),
                        new Position(longitude: 2d, latitude: 2d),
                        new Position(longitude: 3d, latitude: 3d),
                        new Position(longitude: 1d, latitude: 1d)
                    })
                })
                { BoundingBoxes = new[] { 1d, 1d, 3d, 3d } },
                CommandText = "st_makepolygon(st_makeline(ARRAY[st_makepoint(1,1), st_makepoint(2,2), st_makepoint(3,3), st_makepoint(1,1)]))"
            },
            new()
            {
                Geometry = new MultiPoint(new[] {
                    new Point(new Position(longitude: 1d, latitude: 1d))
                })
                { BoundingBoxes = new[] { 1d, 1d, 1d, 1d } },
                CommandText = "st_multi(st_makepoint(1, 1))"
            },
            new()
            {
                Geometry = new MultiLineString(new[] {
                    new LineString(new[] {
                        new Position(longitude: 1d, latitude: 1d),
                        new Position(longitude: 1d, latitude: 2d)
                    })
                })
                { BoundingBoxes = new[] { 1d, 1d, 1d, 2d } },
                CommandText = "st_multi(st_makeline(st_makepoint(1,1), st_makepoint(1,2)))"
            },
            new()
            {
                Geometry = new MultiPolygon(new[] {
                    new Polygon(new[] {
                        new LineString(new[] {
                            new Position(longitude: 1d, latitude: 1d),
                            new Position(longitude: 2d, latitude: 2d),
                            new Position(longitude: 3d, latitude: 3d),
                            new Position(longitude: 1d, latitude: 1d)
                        })
                    })
                })
                { BoundingBoxes = new[] { 1d, 1d, 3d, 3d } },
                CommandText = "st_multi(st_makepolygon(st_makeline(ARRAY[st_makepoint(1,1), st_makepoint(2,2), st_makepoint(3,3), st_makepoint(1,1)])))"
            },
            new()
            {
                Geometry = new GeometryCollection(new IGeometryObject[] {
                    new Point(new Position(longitude: 1d, latitude: 1d)),
                    new MultiPolygon(new[] {
                        new Polygon(new[] {
                            new LineString(new[] {
                            new Position(longitude: 1d, latitude: 1d),
                            new Position(longitude: 2d, latitude: 2d),
                            new Position(longitude: 3d, latitude: 3d),
                            new Position(longitude: 1d, latitude: 1d)
                            })
                        })
                    })
                })
                { BoundingBoxes = new[] { 1d, 1d, 3d, 3d } },
                CommandText = "st_collect(st_makepoint(1,1),st_multi(st_makepolygon(st_makeline(ARRAY[st_makepoint(1,1), st_makepoint(2,2), st_makepoint(3,3), st_makepoint(1,1)]))))"
            },
        };

        [Test, TestCaseSource(nameof(Tests))]
        public void Read(TestData data)
        {
            using var conn = OpenConnection(option: GeoJSONOptions.BoundingBox);
            using var cmd = new OpenGaussCommand($"SELECT {data.CommandText}, st_asgeojson({data.CommandText},options:=1)", conn);
            using var reader = cmd.ExecuteReader();
            Assert.That(reader.Read());
            Assert.That(reader.GetFieldValue<GeoJSONObject>(0), Is.EqualTo(data.Geometry));
            Assert.That(reader.GetFieldValue<GeoJSONObject>(0), Is.EqualTo(JsonConvert.DeserializeObject<IGeometryObject>(reader.GetFieldValue<string>(1), new GeometryConverter())));
        }

        [Test, TestCaseSource(nameof(Tests))]
        public void Write(TestData data)
        {
            using var conn = OpenConnection();
            using var cmd = new OpenGaussCommand($"SELECT st_asewkb(@p) = st_asewkb({data.CommandText})", conn);
            cmd.Parameters.AddWithValue("p", data.Geometry);
            Assert.That(cmd.ExecuteScalar(), Is.True);
        }

        [Test]
        public void IgnoreM()
        {
            using var conn = OpenConnection();
            using var cmd = new OpenGaussCommand("SELECT st_makepointm(1,1,1)", conn);
            using var reader = cmd.ExecuteReader();
            Assert.That(reader.Read());
            Assert.That(reader.GetFieldValue<Point>(0), Is.EqualTo(new Point(new Position(1d, 1d))));
        }

        public static readonly TestData[] NotAllZSpecifiedTests =
        {
            new()
            {
                Geometry = new LineString(new[] {
                    new Position(1d, 1d, 0d),
                    new Position(2d, 2d)
                })
            },
            new()
            {
                Geometry =  new LineString(new[] {
                    new Position(1d, 1d, 0d),
                    new Position(2d, 2d),
                    new Position(3d, 3d),
                    new Position(4d, 4d)
                })
            }
        };

        [Test, TestCaseSource(nameof(NotAllZSpecifiedTests))]
        public void Not_all_Z_specified(TestData data)
        {
            using var conn = OpenConnection();
            using var cmd = new OpenGaussCommand("SELECT @p", conn);
            cmd.Parameters.AddWithValue("p", data.Geometry);
            Assert.That(() => cmd.ExecuteScalar(), Throws.ArgumentException);
        }

        [Test]
        public void Read_unknown_CRS()
        {
            using var conn = OpenConnection(option: GeoJSONOptions.ShortCRS);
            using var cmd = new OpenGaussCommand("SELECT st_setsrid(st_makepoint(0,0), 1)", conn);
            using var reader = cmd.ExecuteReader();
            Assert.That(reader.Read());
            Assert.That(() => reader.GetValue(0), Throws.InvalidOperationException);
        }

        [Test]
        public void Read_unspecified_CRS()
        {
            using var conn = OpenConnection(option: GeoJSONOptions.ShortCRS);
            using var cmd = new OpenGaussCommand("SELECT st_setsrid(st_makepoint(0,0), 0)", conn);
            using var reader = cmd.ExecuteReader();
            Assert.That(reader.Read());
            Assert.That(reader.GetFieldValue<Point>(0).CRS, Is.Null);
        }

        [Test]
        public void Read_short_CRS()
        {
            using var conn = OpenConnection(option: GeoJSONOptions.ShortCRS);
            using var cmd = new OpenGaussCommand("SELECT st_setsrid(st_makepoint(0,0), 4326)", conn);
            var point = (Point)cmd.ExecuteScalar()!;
            var crs = point.CRS as NamedCRS;

            Assert.That(crs, Is.Not.Null);
            Assert.That(crs!.Properties["name"], Is.EqualTo("EPSG:4326"));
        }

        [Test]
        public void Read_long_CRS()
        {
            using var conn = OpenConnection(option: GeoJSONOptions.LongCRS);
            using var cmd = new OpenGaussCommand("SELECT st_setsrid(st_makepoint(0,0), 4326)", conn);
            var point = (Point)cmd.ExecuteScalar()!;
            var crs = point.CRS as NamedCRS;

            Assert.That(crs, Is.Not.Null);
            Assert.That(crs!.Properties["name"], Is.EqualTo("urn:ogc:def:crs:EPSG::4326"));
        }

        [Test]
        public void Write_ill_formed_CRS()
        {
            using var conn = OpenConnection();
            using var cmd = new OpenGaussCommand("SELECT st_srid(@p)", conn);
            cmd.Parameters.AddWithValue("p", new Point(new Position(0d, 0d)) { CRS = new NamedCRS("ill:formed") });
            Assert.That(() => cmd.ExecuteScalar(), Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Write_linked_CRS()
        {
            using var conn = OpenConnection();
            using var cmd = new OpenGaussCommand("SELECT st_srid(@p)", conn);
            cmd.Parameters.AddWithValue("p", new Point(new Position(0d, 0d)) { CRS = new LinkedCRS("href") });
            Assert.That(() => cmd.ExecuteScalar(), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void Write_unspecified_CRS()
        {
            using var conn = OpenConnection();
            using var cmd = new OpenGaussCommand("SELECT st_srid(@p)", conn);
            cmd.Parameters.AddWithValue("p", new Point(new Position(0d, 0d)) { CRS = new UnspecifiedCRS() });
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(0));
        }

        [Test]
        public void Write_short_CRS()
        {
            using var conn = OpenConnection();
            using var cmd = new OpenGaussCommand("SELECT st_srid(@p)", conn);
            cmd.Parameters.AddWithValue("p", new Point(new Position(0d, 0d)) { CRS = new NamedCRS("EPSG:4326") });
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(4326));
        }

        [Test]
        public void Write_long_CRS()
        {
            using var conn = OpenConnection();
            using var cmd = new OpenGaussCommand("SELECT st_srid(@p)", conn);
            cmd.Parameters.AddWithValue("p", new Point(new Position(0d, 0d)) { CRS = new NamedCRS("urn:ogc:def:crs:EPSG::4326") });
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(4326));
        }

        [Test]
        public void Write_CRS84()
        {
            using var conn = OpenConnection();
            using var cmd = new OpenGaussCommand("SELECT st_srid(@p)", conn);
            cmd.Parameters.AddWithValue("p", new Point(new Position(0d, 0d)) { CRS = new NamedCRS("urn:ogc:def:crs:OGC::CRS84") });
            Assert.That(cmd.ExecuteScalar(), Is.EqualTo(4326));
        }

        [Test]
        public void Roundtrip_geometry_geography()
        {
            var point = new Point(new Position(0d, 0d));
            using var conn = OpenConnection();
            conn.ExecuteNonQuery("CREATE TEMP TABLE data (geom GEOMETRY, geog GEOGRAPHY)");
            using (var cmd = new OpenGaussCommand("INSERT INTO data (geom, geog) VALUES (@p, @p)", conn))
            {
                cmd.Parameters.AddWithValue("p", point);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new OpenGaussCommand("SELECT geom, geog FROM data", conn))
            using (var reader = cmd.ExecuteReader())
            {
                reader.Read();
                Assert.That(reader[0], Is.EqualTo(point));
                Assert.That(reader[1], Is.EqualTo(point));
            }
        }

        protected override OpenGaussConnection OpenConnection(string? connectionString = null)
            => OpenConnection(connectionString, GeoJSONOptions.None);

        protected OpenGaussConnection OpenConnection(string? connectionString = null, GeoJSONOptions option = GeoJSONOptions.None)
        {
            var conn = base.OpenConnection(connectionString);
            conn.TypeMapper.UseGeoJson(option);
            return conn;
        }

        [OneTimeSetUp]
        public async Task SetUp()
        {
            await using var conn = await base.OpenConnectionAsync();
            await TestUtil.EnsurePostgis(conn);
        }
    }
}

#endif
