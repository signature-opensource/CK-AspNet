using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.AspNet.Tester.Tests
{
    public class StupidService
    {
        public string GetText() => $"It is {DateTime.UtcNow}.";
    }
}
