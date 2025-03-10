using System;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using OpenGauss.NET.Internal;
using OpenGauss.NET.Internal.TypeHandling;

namespace OpenGauss.NetTopologySuite.Internal
{
    public class NetTopologySuiteTypeHandlerResolverFactory : TypeHandlerResolverFactory
    {
        readonly CoordinateSequenceFactory _coordinateSequenceFactory;
        readonly PrecisionModel _precisionModel;
        readonly Ordinates _handleOrdinates;
        readonly bool _geographyAsDefault;

        public NetTopologySuiteTypeHandlerResolverFactory(
            CoordinateSequenceFactory? coordinateSequenceFactory,
            PrecisionModel? precisionModel,
            Ordinates handleOrdinates,
            bool geographyAsDefault)
        {
            _coordinateSequenceFactory = coordinateSequenceFactory ?? NtsGeometryServices.Instance.DefaultCoordinateSequenceFactory;;
            _precisionModel = precisionModel ?? NtsGeometryServices.Instance.DefaultPrecisionModel;
            _handleOrdinates = handleOrdinates == Ordinates.None ? _coordinateSequenceFactory.Ordinates : handleOrdinates;
            _geographyAsDefault = geographyAsDefault;
        }

        public override TypeHandlerResolver Create(OpenGaussConnector connector)
            => new NetTopologySuiteTypeHandlerResolver(connector, _coordinateSequenceFactory, _precisionModel, _handleOrdinates,
                _geographyAsDefault);

        public override string? GetDataTypeNameByClrType(Type type)
            => NetTopologySuiteTypeHandlerResolver.ClrTypeToDataTypeName(type, _geographyAsDefault);

        public override TypeMappingInfo? GetMappingByDataTypeName(string dataTypeName)
            => NetTopologySuiteTypeHandlerResolver.DoGetMappingByDataTypeName(dataTypeName);
    }
}
