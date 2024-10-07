using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.AspNet.Tests;

public class HttpContextDependentService
{
    readonly HttpContext _ctx;

    public HttpContextDependentService( ScopedHttpContext ctx )
    {
        _ctx = ctx.HttpContext;
    }

    public bool HttpContextIsHere => _ctx != null;
}
