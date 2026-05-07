using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive.Api;
using EastFive.Api.Bindings;
using EastFive.Api.Resources;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence.Azure.StorageTables.Driver;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Parameter attribute that opts an <see cref="IQueryable{T}"/> parameter into the
    /// per-datastore storage instigation pipeline. Resolves the driver via
    /// <see cref="StorageDriverScope.Resolve"/> (parameter → method → declaring type
    /// → assembly) and produces a <see cref="StorageQuery{T}"/> bound to that driver.
    ///
    /// Pair with a queryable-bound write extension (e.g.
    /// <c>StorageInsertAsync</c>) to make controllers explicit about which datastore
    /// receives a mutation:
    /// <code>
    /// public static Task&lt;IHttpResponse&gt; CreateAsync(
    ///     [Resource] ACPChat chat,
    ///     [StorageEntities] IQueryable&lt;ACPChat&gt; chatsInStorage,
    ///     CreatedResponse onCreated,
    ///     AlreadyExistsResponse onAlreadyExists)
    ///     =&gt; chatsInStorage.StorageInsertAsync(chat, _ =&gt; onCreated(), () =&gt; onAlreadyExists());
    /// </code>
    ///
    /// MIXING CONCERN: this attribute is for the current routing path only. The legacy
    /// <c>StorageQueryInvocationAttribute</c> on <see cref="StorageQuery{T}"/> still
    /// claims bare (un-decorated) <see cref="IQueryable{T}"/> parameters for back-compat;
    /// the two paths coexist until legacy controllers are migrated. New controllers
    /// should always use <see cref="StorageEntitiesAttribute"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class StorageEntitiesAttribute : Attribute,
        IBindApiValue, IDocumentParameter, IProvideBindingRequirements
    {
        public string Name { get; set; }

        public string GetKey(ParameterInfo paramInfo)
            => string.IsNullOrWhiteSpace(this.Name) ? paramInfo.Name : this.Name;

        public (IReadOnlyList<BindingRequirement> requirements, AssembleParameter assemble)
            GetParameterBinding(ParameterInfo parameter)
            => (new[] { GetRequirement(parameter) }, values => (values[0], null));

        private BindingRequirement GetRequirement(ParameterInfo parameter)
        {
            var entityType = ExtractEntityType(parameter);
            return new BindingRequirement(
                    path: GetKey(parameter),
                    // Source.Request + a string converter that ignores `raw` is the
                    // canonical "no input data needed, just produce a value" shape —
                    // already used by [Accepts] / [Hashed]. The v3 envelope dispatch
                    // invokes this converter once per match with raw == string.Empty.
                    source: BindingSource.Request,
                    parameter: parameter,
                    isOptional: false)
                .AddConverter<string>((raw, param, app, req, onParsed, onFailure) =>
                {
                    if (entityType == null)
                        return onFailure(
                            $"[StorageEntities] requires IQueryable<T>; got '{param.ParameterType.FullName}'.");

                    var provider = StorageDriverScope.Resolve(param);
                    var driver = provider.GetDriver();

                    var queryType = typeof(StorageQuery<>).MakeGenericType(entityType);
                    var query = Activator.CreateInstance(queryType, driver);
                    return onParsed(query);
                });
        }

        public SelectParameterResult TryCast(BindingData bindingData)
        {
            // The legacy path is intentionally unsupported. [StorageEntities] only makes
            // sense in v3's BindAndInvokeAsync, which honors IProvideBindingRequirements
            // ahead of legacy IInstigatable* hooks.
            throw new NotSupportedException(
                $"{nameof(StorageEntitiesAttribute)} requires the v3 binding pipeline " +
                $"(MethodDispatcher.BindAndInvokeAsync).");
        }

        public Parameter GetParameter(ParameterInfo paramInfo, HttpApplication httpApp)
        {
            var entityType = ExtractEntityType(paramInfo);
            var typeName = entityType != null
                ? $"IQueryable<{Parameter.GetTypeName(entityType, httpApp)}>"
                : Parameter.GetTypeName(paramInfo.ParameterType, httpApp);
            return new Parameter(paramInfo)
            {
                Name = GetKey(paramInfo),
                Required = true,
                Where = "INSTIGATED",
                Type = typeName,
                OpenApiType = Parameter.GetOpenApiTypeName(paramInfo.ParameterType, httpApp),
            };
        }

        private static Type ExtractEntityType(ParameterInfo parameter)
        {
            var t = parameter.ParameterType;
            if (!t.IsGenericType)
                return null;
            var def = t.GetGenericTypeDefinition();
            if (def != typeof(IQueryable<>))
                return null;
            return t.GenericTypeArguments[0];
        }
    }
}
