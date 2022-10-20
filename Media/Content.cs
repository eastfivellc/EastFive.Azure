using System;
using System.Threading.Tasks;

using EastFive;
using System.Security.Claims;
using System.Net;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using System.IO;

namespace EastFive.Api.Azure
{
    public static class Content
    {
        public static Task<TResult> CreateContentAsync<TResult>(Guid contentId, string contentType, byte[] content,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyExists)
        {
            return Persistence.Content.CreateAsync(contentId, contentType, content,
                onCreated, onAlreadyExists);
        }

        public static Task<TResult> FindContentByContentIdAsync<TResult>(Guid contentId,
            Func<string, byte[], TResult> onFound,
            Func<TResult> onNotFound,
            Func<TResult> onNotAuthorized)
        {
            return Persistence.Content.FindContentByIdAsync(contentId,
                onFound,
                onNotFound);
        }

        public static Task<TResult> FindContentByContentIdAsync<TResult>(Guid contentId,
                Security security,
            Func<string, byte[], TResult> onFound,
            Func<TResult> onNotFound,
            Func<TResult> onNotAuthorized)
        {
            return Persistence.Content.FindContentByIdAsync(contentId,
                onFound,
                onNotFound);
        }
    }
}
