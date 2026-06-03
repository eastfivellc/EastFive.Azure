using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive.Api.Binding;
using EastFive.Api.Serialization.Binding.Sources;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// V3 selection attribute that opts an <see cref="IQueryable{T}"/> parameter
    /// into the per-datastore storage pipeline. Selection always succeeds for a
    /// closed <c>IQueryable&lt;T&gt;</c> parameter and emits an empty
    /// <see cref="LookupBindingSource"/> — there is no per-parameter state to
    /// carry, because <see cref="StorageQueryBinder"/> resolves the scoped
    /// driver lazily via <see cref="ParameterSlot"/> at bind time.
    ///
    /// Pair with a queryable-bound write extension (e.g.
    /// <c>StorageInsertAsync</c>) to make controllers explicit about which
    /// datastore receives a mutation:
    /// <code>
    /// public static Task&lt;IHttpResponse&gt; CreateAsync(
    ///     [StorableEntityFromResource] StorableEntity&lt;ACPChat&gt; chatEntity,
    ///     [StorageEntities] IQueryable&lt;ACPChat&gt; chatsInStorage,
    ///     CreatedResponse onCreated,
    ///     AlreadyExistsResponse onAlreadyExists)
    ///     =&gt; chatsInStorage.StorageInsertAsync(chat, _ =&gt; onCreated(), () =&gt; onAlreadyExists());
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class StorageEntitiesAttribute : Attribute, IBindFromRequest
    {
        public string Name { get; set; }

        public bool TrySelectSource(IRequestEnvelopeV3 envelope, ParameterInfo parameter,
            out BindCall call)
        {
            if (!IsQueryable(parameter.ParameterType))
            {
                call = null;
                return false;
            }
            // No body / no key parts: data-free emit. StorageQueryBinder
            // doesn't probe the source — it constructs StorageQuery<T> from a
            // slot-resolved driver. The empty source is here purely to satisfy
            // the dispatcher's per-parameter BindCall contract.
            var emptyLookup = new LookupBindingSource(
                Enumerable.Empty<KeyValuePair<string, string[]>>());
            call = BindCalls.FromSource(emptyLookup, string.Empty);
            return true;
        }

        private static bool IsQueryable(Type t) =>
            t is { IsGenericType: true } && t.GetGenericTypeDefinition() == typeof(IQueryable<>);
    }
}
