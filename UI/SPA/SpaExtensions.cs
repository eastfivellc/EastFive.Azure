using EastFive.Api;
using Microsoft.AspNetCore.Builder;
using System;
using System.Collections.Generic;
using System.Text;

namespace EastFive.Azure.Spa
{
    public static class ModulesExtensions
    {
        public static IApplicationBuilder UseSpaHandler(
            this IApplicationBuilder builder, IApplication app)
        {
            var success = SpaHandler.SetupSpa(app);
            if (!success)
                return builder;
            return builder.UseMiddleware<SpaHandler>(app);
        }
    }
}
