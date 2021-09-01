using System;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Security;

[assembly: CLSCompliant(false)]
[assembly: AllowPartiallyTrustedCallers]
[assembly: AssemblyTrademark("")]

[assembly: InternalsVisibleTo("Npgsql.Tests")]

#if !NETSTANDARD1_3
[assembly: SecurityRules(SecurityRuleSet.Level1)]
#endif
