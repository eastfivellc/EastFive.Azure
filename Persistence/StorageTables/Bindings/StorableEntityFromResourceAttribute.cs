using System;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

using EastFive.Api.Binding;
using EastFive.Extensions;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization.Binding;
using EastFive.Api.Serialization.Binding.Sources;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// V3 selection attribute that materializes a <c>StorableEntity&lt;T&gt;</c>
    /// parameter from the request body. The body's raw shape (JSON
    /// <see cref="JToken"/>, <see cref="IFormCollection"/>, or string) is
    /// stashed on a <see cref="StorageBoundSource"/> alongside the eagerly-
    /// resolved <see cref="AzureTableDriverDynamic"/>; the
    /// <see cref="StorableEntityBinder"/> downcasts and deserializes via
    /// <see cref="StorageBindingHelpers"/>.
    ///
    /// Pair with the write extensions on <see cref="StorableEntityExtensions"/>
    /// (<c>MutateEntity</c>, <c>StorageInsertAsync</c>) to make controllers
    /// explicit about which datastore receives a mutation:
    /// <code>
    /// public static Task&lt;IHttpResponse&gt; CreateAsync(
    ///     [StorableEntityFromResource] StorableEntity&lt;Practice&gt; practiceEntity,
    ///     CreatedResponse onCreated,
    ///     AlreadyExistsResponse onAlreadyExists)
    ///     =&gt; practiceEntity
    ///         .MutateEntity(p =&gt; { /* normalize */ return p; })
    ///         .StorageInsertAsync(() =&gt; onCreated(), () =&gt; onAlreadyExists());
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class StorableEntityFromResourceAttribute : Attribute, IBindFromRequest
    {
        public bool TrySelectSource(IRequestEnvelopeV3 envelope, ParameterInfo parameter,
            out BindCall call)
        {
            // Probe the body in the order the legacy converter ladder used:
            // JToken (any JSON) → IFormCollection → raw string. First match wins.
            object rawBody = null;
            IBindingSource innerSource = null;
            if(EnvelopeBodyAccessor.TryGetBodyRoot(envelope, out innerSource, out rawBody))
            {
                
            }
            else if (envelope.TryGetBody<string>(out var raw) && !raw.IsNullOrWhiteSpace())
            {
                rawBody = raw;
            }

            if (rawBody is null)
            {
                call = null;
                return false;
            }

            var provider = StorageDriverScope.Resolve(parameter);
            var driver = provider.GetDriver();
            var storageSrc = new StorageBoundSource(innerSource, driver, rawBody);

            // Build a BindCall that dispatches onObject(storageSrc) directly so
            // StorableEntityBinder receives the wrapper (and not whatever shape
            // the inner body source would emit on its own onObject).
            call = MakeStorageObjectCall(storageSrc);
            return true;
        }

        internal static BindCall MakeStorageObjectCall(StorageBoundSource storageSrc) =>
            (path, onNull, onString, onGuid, onBool, onInt64, onDouble, onDateTime,
                onBytes, onObject, onArray, elementTypeHint, onFailure) =>
            {
                if (!string.IsNullOrEmpty(path))
                    return BindingSourceDispatch.FailTask<object>(
                        new BindFailure(new NotPresent(), typeof(object), path), onFailure);
                if (onObject is null)
                    return BindingSourceDispatch.FailTask<object>(
                        new BindFailure(
                            new WrongSourceType("object", "object"),
                            typeof(object),
                            path ?? string.Empty),
                        onFailure);
                return new ValueTask<object>(onObject(storageSrc));
            };
    }
}
