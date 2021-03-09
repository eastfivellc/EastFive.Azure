using System;
using System.Collections.Generic;
using System.Text;

namespace EastFive.Azure.Spa
{
    public class SpaBuild
    {
        public double buildTimeInSeconds;
        public Route[] routes;

        public MimeType[] mimeTypes;
    }

    public struct Route
    {
        public string routePrefix;
        public string contentPath;
        public string defaultFile;
        public string indexFile;
    }

    public struct MimeType
    {
        public string extension;
        public string mimeType;
    }
}
