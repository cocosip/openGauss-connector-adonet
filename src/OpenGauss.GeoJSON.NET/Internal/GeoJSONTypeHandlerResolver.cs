using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using GeoJSON.Net;
using GeoJSON.Net.Geometry;
using Newtonsoft.Json;
using OpenGauss.NET;
using OpenGauss.NET.Internal;
using OpenGauss.NET.Internal.TypeHandling;
using OpenGauss.NET.PostgresTypes;
using OpenGauss.NET.TypeMapping;
using OpenGauss.NET.Types;

namespace OpenGauss.GeoJSON.NET.Internal
{
    public class GeoJSONTypeHandlerResolver : TypeHandlerResolver
    {
        readonly OpenGaussDatabaseInfo _databaseInfo;
        readonly GeoJsonHandler? _geometryHandler, _geographyHandler;
        readonly bool _geographyAsDefault;

        static readonly ConcurrentDictionary<string, CrsMap> CRSMaps = new();

        internal GeoJSONTypeHandlerResolver(OpenGaussConnector connector, GeoJSONOptions options, bool geographyAsDefault)
        {
            _databaseInfo = connector.DatabaseInfo;
            _geographyAsDefault = geographyAsDefault;

            var crsMap = (options & (GeoJSONOptions.ShortCRS | GeoJSONOptions.LongCRS)) == GeoJSONOptions.None
                ? default : CRSMaps.GetOrAdd(connector.Settings.ConnectionString, _ =>
                 {
                     var builder = new CrsMapBuilder();
                     using (var cmd = connector.CreateCommand(
                             "SELECT min(srid), max(srid), auth_name " +
                             "FROM(SELECT srid, auth_name, srid - rank() OVER(ORDER BY srid) AS range " +
                             "FROM spatial_ref_sys) AS s GROUP BY range, auth_name ORDER BY 1;"))
                     using (var reader = cmd.ExecuteReader())
                         while (reader.Read())
                         {
                             builder.Add(new CrsMapEntry(
                                 reader.GetInt32(0),
                                 reader.GetInt32(1),
                                 reader.GetString(2)));
                         }
                     return builder.Build();
                 });

            var (pgGeometryType, pgGeographyType) = (PgType("geometry"), PgType("geography"));

            if (pgGeometryType is not null)
                _geometryHandler = new GeoJsonHandler(pgGeometryType, options, crsMap);
            if (pgGeographyType is not null)
                _geographyHandler = new GeoJsonHandler(pgGeographyType, options, crsMap);
        }

        public override OpenGaussTypeHandler? ResolveByDataTypeName(string typeName)
            => typeName switch
            {
                "geometry" => _geometryHandler,
                "geography" => _geographyHandler,
                _ => null
            };

        public override OpenGaussTypeHandler? ResolveByClrType(Type type)
            => ClrTypeToDataTypeName(type, _geographyAsDefault) is { } dataTypeName && ResolveByDataTypeName(dataTypeName) is { } handler
                ? handler
                : null;

        internal static string? ClrTypeToDataTypeName(Type type, bool geographyAsDefault)
            => type.BaseType != typeof(GeoJSONObject)
                ? null
                : geographyAsDefault
                    ? "geography"
                    : "geometry";

        public override TypeMappingInfo? GetMappingByDataTypeName(string dataTypeName)
            => DoGetMappingByDataTypeName(dataTypeName);

        internal static TypeMappingInfo? DoGetMappingByDataTypeName(string dataTypeName)
            => dataTypeName switch
            {
                "geometry" => new(OpenGaussDbType.Geometry, "geometry"),
                "geography" => new(OpenGaussDbType.Geography, "geography"),
                _ => null
            };

        PostgresType? PgType(string pgTypeName) => _databaseInfo.TryGetPostgresTypeByName(pgTypeName, out var pgType) ? pgType : null;
    }
}
