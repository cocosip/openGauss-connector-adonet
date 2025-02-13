using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Scriban;

namespace OpenGauss.SourceGenerators
{
    [Generator]
    public class OpenGaussConnectionStringBuilderSourceGenerator : IIncrementalGenerator
    {
        static readonly DiagnosticDescriptor InternalError = new(
            id: "PGXXXX",
            title: "Internal issue when source-generating OpenGaussConnectionStringBuilder",
            messageFormat: "{0}",
            category: "Internal",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // ×¢²á±àÒëÌá¹©Æ÷
            var compilationProvider = context.CompilationProvider;

            // ×¢²áÉú³ÉÂß¼­
            context.RegisterSourceOutput(compilationProvider, (spc, compilation) =>
            {
                if (compilation.Assembly.GetTypeByMetadataName("OpenGauss.NET.OpenGaussConnectionStringBuilder") is not { } type)
                    return;

                if (compilation.Assembly.GetTypeByMetadataName("OpenGauss.NET.OpenGaussConnectionStringPropertyAttribute") is not
                    { } connectionStringPropertyAttribute)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        InternalError,
                        location: null,
                        "Could not find OpenGauss.NET.OpenGaussConnectionStringPropertyAttribute"));
                    return;
                }

                var obsoleteAttribute = compilation.GetTypeByMetadataName("System.ObsoleteAttribute");
                var displayNameAttribute = compilation.GetTypeByMetadataName("System.ComponentModel.DisplayNameAttribute");
                var defaultValueAttribute = compilation.GetTypeByMetadataName("System.ComponentModel.DefaultValueAttribute");

                if (obsoleteAttribute is null || displayNameAttribute is null || defaultValueAttribute is null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        InternalError,
                        location: null,
                        "Could not find ObsoleteAttribute, DisplayNameAttribute or DefaultValueAttribute"));
                    return;
                }

                var properties = new List<PropertyDetails>();
                var propertiesByKeyword = new Dictionary<string, PropertyDetails>();
                foreach (var member in type.GetMembers())
                {
                    if (member is not IPropertySymbol property ||
                        property.GetAttributes().FirstOrDefault(a => connectionStringPropertyAttribute.Equals(a.AttributeClass, SymbolEqualityComparer.Default)) is not { } propertyAttribute ||
                        property.GetAttributes()
                            .FirstOrDefault(a => displayNameAttribute.Equals(a.AttributeClass, SymbolEqualityComparer.Default))
                            ?.ConstructorArguments[0].Value is not string displayName)
                    {
                        continue;
                    }

                    var explicitDefaultValue = property.GetAttributes()
                        .FirstOrDefault(a => defaultValueAttribute.Equals(a.AttributeClass, SymbolEqualityComparer.Default))
                        ?.ConstructorArguments[0].Value;

                    if (explicitDefaultValue is string s)
                        explicitDefaultValue = '"' + s.Replace("\"", "\"\"") + '"';

                    if (explicitDefaultValue is not null && property.Type.TypeKind == TypeKind.Enum)
                    {
                        explicitDefaultValue = $"({property.Type.Name}){explicitDefaultValue}";
                    }

                    var propertyDetails = new PropertyDetails
                    {
                        Name = property.Name,
                        CanonicalName = displayName,
                        TypeName = property.Type.Name,
                        IsEnum = property.Type.TypeKind == TypeKind.Enum,
                        IsObsolete = property.GetAttributes().Any(a => obsoleteAttribute.Equals(a.AttributeClass, SymbolEqualityComparer.Default)),
                        DefaultValue = explicitDefaultValue
                    };

                    properties.Add(propertyDetails);

                    propertiesByKeyword[displayName.ToUpperInvariant()] = propertyDetails;
                    if (property.Name != displayName)
                        propertiesByKeyword[property.Name.ToUpperInvariant()] = propertyDetails;
                    if (propertyAttribute.ConstructorArguments.Length == 1)
                        foreach (var synonymArg in propertyAttribute.ConstructorArguments[0].Values)
                            if (synonymArg.Value is string synonym)
                                propertiesByKeyword[synonym.ToUpperInvariant()] = propertyDetails;
                }

                var template = Template.Parse(EmbeddedResource.GetContent("OpenGaussConnectionStringBuilder.snbtxt"), "OpenGaussConnectionStringBuilder.snbtxt");

                var output = template.Render(new
                {
                    Properties = properties,
                    PropertiesByKeyword = propertiesByKeyword
                });

                spc.AddSource(type.Name + ".Generated.cs", SourceText.From(output, Encoding.UTF8));
            });
        }

        class PropertyDetails
        {
            public string Name { get; set; } = null!;
            public string CanonicalName { get; set; } = null!;
            public string TypeName { get; set; } = null!;
            public bool IsEnum { get; set; }
            public bool IsObsolete { get; set; }
            public object? DefaultValue { get; set; }
        }
    }
}
