using System.Diagnostics;

namespace OpenGauss.NET.PostgresTypes
{
    /// <summary>
    /// Represents a PostgreSQL array data type, which can hold several multiple values in a single column.
    /// </summary>
    /// <remarks>
    /// See https://www.postgresql.org/docs/current/static/arrays.html.
    /// </remarks>
    public class PostgresTableOfType : PostgresType
    {
        /// <summary>
        /// The PostgreSQL data type of the element contained within this array.
        /// </summary>
        public PostgresType Element { get; }

        /// <summary>
        /// Constructs a representation of a PostgreSQL array data type.
        /// </summary>
        protected internal PostgresTableOfType(string ns, string internalName, uint oid, PostgresType elementPostgresType)
            : base(ns, internalName , internalName, oid)
        {
            // Debug.Assert(internalName == '_' + elementPostgresType.InternalName);
            Element = elementPostgresType;
        }
    }
}
