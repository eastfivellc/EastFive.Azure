﻿using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq.Async;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Persistence
{
    public static class ApiExtensions
    {
        public static IHttpResponse HttpResponse<TResource>(
            this IEnumerableAsync<TResource> resources,
            MultipartAsyncResponse<TResource> responseDelegate)
        {
            return responseDelegate(resources);
        }

        public static Task<IHttpResponse> HttpGetAsync<TResource>(
                this IRef<TResource> resourceRef,
            ContentTypeResponse<TResource> onFound,
            NotFoundResponse onNotFound,
                Func<TResource, TResource> mutation = default)
            where TResource : IReferenceable
        {
            return resourceRef.StorageGetAsync(
                (resource) =>
                {
                    if(mutation.IsDefaultOrNull())
                        return onFound(resource);
                    var updatedResource = mutation(resource);
                    return onFound(updatedResource);
                },
                () => onNotFound());
        }

        public static IHttpResponse HttpGetAll<TResource>(
                this IQueryable<TResource> resources,
            MultipartAsyncResponse<TResource> onFound,
                Func<TResource, TResource> select = default)
            where TResource : IReferenceable, new()
        {
            return resources
                .StorageGet()
                .IfThen(!select.IsDefaultOrNull(),
                        ress => ress.Select(select))
                .HttpResponse(onFound);
        }

        public static Task<IHttpResponse> HttpPostAsync<TResource>(
                this TResource resource,
            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists)
            where TResource : IReferenceable
        {
            return resource.StorageCreateAsync(
                discard => onCreated(),
                onAlreadyExists: () => onAlreadyExists());
        }

        public static Task<IHttpResponse> HttpPostAsync<TResource>(
                this TResource resource,
            CreatedBodyResponse<TResource> onCreated,
            AlreadyExistsResponse onAlreadyExists)
            where TResource : IReferenceable
        {
            return resource.StorageCreateAsync(
                discard => onCreated(resource),
                onAlreadyExists: () => onAlreadyExists());
        }

        public static Task<IHttpResponse> HttpPatchAsync<TResource>(
                this IRef<TResource> resourceRef,
                MutateResource<TResource> modifyResource,
            ContentTypeResponse<TResource> onUpdated,
            NotFoundResponse onNotFound,
                Func<TResource, TResource> additionalMutations = default,
                Func<TResource, Func<Task<IHttpResponse>>, Task<IHttpResponse>> conditional = default)
            where TResource : IReferenceable
        {
            return resourceRef.StorageUpdateAsync(
                (resource, saveAsync) =>
                {
                    if (conditional.IsDefaultOrNull())
                        return Mutate();

                    return conditional(
                        resource,
                        () => Mutate());

                    async Task<IHttpResponse> Mutate()
                    {
                        var resourceToSave = modifyResource(resource);
                        if (additionalMutations.IsNotDefaultOrNull())
                            resourceToSave = additionalMutations(resourceToSave);

                        await saveAsync(resourceToSave);
                        return onUpdated(resourceToSave);
                    }
                },
                () => onNotFound());
        }

        public static Task<IHttpResponse> HttpDeleteAsync<TResource>(
                this IRef<TResource> resourceRef,
            NoContentResponse onDeleted,
            NotFoundResponse onNotFound,
                Func<TResource, Func<Task<IHttpResponse>>, Task<IHttpResponse>> conditional = default)
            where TResource : IReferenceable
        {
            return resourceRef.StorageDeleteIfAsync(
                onFound:async (resource, deleteAsync) =>
                {
                    if (conditional.IsDefaultOrNull())
                    {
                        await deleteAsync();
                        return onDeleted();
                    }
                    return await conditional(resource,
                        async () =>
                        {
                            await deleteAsync();
                            return onDeleted();
                        });
                },
                () => onNotFound());
        }
    }
}
