using EastFive.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
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

        internal string ResolveRoute(string requestPath)
        {
            var route = this;
            var subPath = requestPath.Substring(route.routePrefix.Length);
            if (subPath.IsDefaultNullOrEmpty())
                return route.indexFile;
            if (subPath == "/")
                return route.indexFile;
            return $"{route.contentPath}{subPath}";
        }

        internal string ResolveLocation(string filePath)
        {
            var route = this;
            var subPath = filePath.Substring(route.contentPath.Length);
            if (subPath.IsDefaultNullOrEmpty())
                return filePath.Split('/').Last();
            if (subPath == "/")
                return route.indexFile.Split('/').Last();
            return $"{route.routePrefix.TrimEnd('/')}/{subPath.TrimStart('/')}";
        }
    }

    public struct MimeType
    {
        public string extension;
        public string mimeType;
    }
}
