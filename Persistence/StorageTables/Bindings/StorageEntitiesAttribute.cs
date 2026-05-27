using System;
using System.Linq;
using System.Reflection;

using EastFive.Api.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// V3 selection attribute that opts an <see cref="IQueryable{T}"/> parameter
    /// into the per-datastore storage pipeline. Selection always succeeds:
    /// resolves the driver eagerly via
    /// <see cref="StorageDriverScope.Resolve"/> (parameter → method →
    /// declaring type → assembly) and dispatches a
    /// <see cref="StorageBoundSource"/> (carrying the driver, no body) via
    /// <c>onObject</c>; <see cref="StorageQueryBinder"/> then constructs
    /// <c>new StorageQuery&lt;T&gt;(driver)</c>.
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

            var provider = StorageDriverScope.Resolve(parameter);
            var driver = provider.GetDriver();
            var storageSrc = new StorageBoundSource(inner: null, driver: driver, rawBody: null);
            call = StorableEntityFromResourceAttribute.MakeStorageObjectCall(storageSrc);
            return true;
        }

        private static bool IsQueryable(Type t) =>
            t is { IsGenericType: true } && t.GetGenericTypeDefinition() == typeof(IQueryable<>);
    }
}
