using System;
using System.Collections.Generic;
using System.Text;

namespace EastFive.Azure.Spa
{
    public class SpaBuild
    {
        public int buildTimeInSeconds { get; set; }
        public Route[] routes { get; set; }
    }

    public struct Route
    {
        public string routePrefix { get; set; }
        public string contentPath { get; set; }
        public string defaultFile { get; set; }
        public string indexFile { get; set; }
    }
}
