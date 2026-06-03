using System;
using System.Reflection;

using EastFive.Api.Binding;
using EastFive.Api.Binding.Scopes;
using EastFive.Serialization.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// V3 selection attribute that materializes a <c>StorableEntity&lt;T&gt;</c>
    /// parameter from the request body. Selection forwards the body root
    /// (<c>JTokenBindingSource</c> for JSON, <c>HttpLookupBindingSources.ForForm</c>
    /// for form posts) through <see cref="BindCalls.FromSource"/>; the
    /// <see cref="StorableEntityBinder"/> resolves the per-parameter
    /// <see cref="EastFive.Persistence.Azure.StorageTables.Driver.AzureTableDriverDynamic"/>
    /// via <see cref="ParameterSlot"/> on the binding context, delegates entity
    /// materialization to <see cref="ITypeBindings"/> (PocoBinder + member
    /// binders walking the <see cref="RequestBody"/> scope), and wraps the
    /// result via <see cref="StorableEntity{T}.FromDeserialized"/>.
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
    public sealed class StorableEntityFromResourceAttribute : Attribute, IBindFromRequest, IProvideMemberScope
    {
        Type IProvideMemberScope.MemberScope => typeof(RequestBody);

        public bool TrySelectSource(IRequestEnvelopeV3 envelope, ParameterInfo parameter,
            out BindCall call)
        {
            if (EnvelopeBodyAccessor.TryGetBodyRoot(envelope, out var inner, out _))
            {
                call = BindCalls.FromSource(inner, string.Empty);
                return true;
            }
            if (parameter.HasDefaultValue) { call = BindCalls.NotPresent; return true; }
            call = null;
            return false;
        }
    }
}
