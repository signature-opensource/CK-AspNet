using CK.Core;
using NUnitLite;
using System.Globalization;
using System.Reflection;

namespace CK.AspNet.Tester.NetCore.Tests
{
    public static class Program
    {
        public static int Main( string[] args )
        {
            return new AutoRun( Assembly.GetEntryAssembly() ).Execute( args );
        }
    }
}
