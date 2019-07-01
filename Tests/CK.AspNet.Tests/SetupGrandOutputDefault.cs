using CK.Core;
using CK.Monitoring;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;

namespace CK.AspNet.Tests
{
    [SetUpFixture]
    public class SetupGrandOutputDefault
    {
        [OneTimeSetUp]
        public void GrandOutput_Default_should_be_configured_with_default_values()
        {
            using( var client = GrandOutputWebHostTests.CreateServerWithUseMonitoring( null ) )
            {
                LogFile.RootLogPath.Should().NotBeNull().And.EndWith( Path.DirectorySeparatorChar + "Logs" + Path.DirectorySeparatorChar );
                GrandOutput.Default.Should().NotBeNull();
            }
        }

    }
}
