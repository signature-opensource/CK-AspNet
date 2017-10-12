using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.AspNet.Tests
{
    public class StupidService
    {
        public string GetText() => $"It is {DateTime.UtcNow}.";
    }
}
