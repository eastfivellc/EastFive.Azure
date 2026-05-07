using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Microsoft.AspNetCore.Http;

using Newtonsoft.Json.Linq;

using EastFive.Api;
using EastFive.Api.Bindings;
using EastFive.Api.Resources;
using EastFive.Api.Serialization;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Reflection;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Write-side counterpart of <see cref="StorageEntityFromQueryIdAttribute"/>:
    /// deserializes the request body into <typeparamref name="T"/> and pairs it
    /// with the <see cref="EastFive.Persistence.Azure.StorageTables.Driver.AzureTableDriverDynamic"/>
    /// resolved through <see cref="StorageDriverScope"/> (parameter → method →
    /// declaring type → assembly), producing a fully-populated
    /// <see cref="StorableEntity{T}"/> in the parameter slot.
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
    ///
    /// Body parsing supports the same three converters as
    /// <see cref="ResourceAttribute"/> (JSON, form-data, plain-text).
    ///
    /// MIXING CONCERN: this attribute is for the current routing path only.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class StorableEntityFromResourceAttribute : Attribute,
        IBindApiValue, IDocumentParameter, IProvideBindingRequirements
    {
        public string GetKey(ParameterInfo paramInfo) => default;

        public (IReadOnlyList<BindingRequirement> requirements, AssembleParameter assemble)
            GetParameterBinding(ParameterInfo parameter)
        {
            var entityType = ExtractEntityType(parameter);
            if (entityType == null)
                throw new InvalidOperationException(
                    $"parameter '{parameter.Name}' must be StorableEntity<T> where T : IReferenceable");

            var requirement = new BindingRequirement(
                    path: string.Empty,
                    source: BindingSource.Body,
                    parameter: parameter,
                    isOptional: false)
                .AddConverter<JContainer>((raw, param, httpApp, request, onParsed, onFailure) =>
                {
                    var contentString = raw?.ToString(Newtonsoft.Json.Formatting.None);
                    if (contentString.IsNullOrWhiteSpace())
                        return onFailure("Empty request body.");

                    var bindConvert = new BindConvert(request, httpApp as HttpApplication);
                    return DeserializeJsonToEntity(contentString, entityType, bindConvert,
                        onParsed, onFailure);
                })
                .AddConverter<IFormCollection>((raw, param, httpApp, request, onParsed, onFailure) =>
                {
                    return DeserializeFormToEntity(raw, entityType, param, httpApp,
                        onParsed, onFailure);
                })
                .AddConverter<string>((raw, param, httpApp, request, onParsed, onFailure) =>
                {
                    if (raw.IsNullOrWhiteSpace())
                        return onFailure("Empty request body.");

                    var bindConvert = new BindConvert(request, httpApp as HttpApplication);
                    return DeserializeJsonToEntity(raw, entityType, bindConvert,
                        onParsed, onFailure);
                });

            // assemble: converter returned the deserialized entity (T as object);
            // wrap it in a StorableEntity<T> together with the scoped driver
            // via the type's public factory (open-generic reified per call).
            AssembleParameter assemble = boundValues =>
            {
                var entity = boundValues[0];
                var provider = StorageDriverScope.Resolve(parameter);
                var driver = provider.GetDriver();
                var storableType = typeof(StorableEntity<>).MakeGenericType(entityType);
                var factory = storableType.GetMethod(
                    nameof(StorableEntity<IReferenceable>.FromDeserialized),
                    BindingFlags.Public | BindingFlags.Static);
                var storable = factory.Invoke(null, new object[] { entity, driver });
                return (storable, null);
            };

            return (new[] { requirement }, assemble);
        }

        public SelectParameterResult TryCast(BindingData bindingData)
        {
            // The legacy path is intentionally unsupported. [StorableEntityFromResource] only
            // makes sense in v3's BindAndInvokeAsync, which honors
            // IProvideBindingRequirements ahead of legacy IInstigatable* hooks.
            throw new NotSupportedException(
                $"{nameof(StorableEntityFromResourceAttribute)} requires the v3 binding pipeline " +
                $"(MethodDispatcher.BindAndInvokeAsync).");
        }

        public Parameter GetParameter(ParameterInfo paramInfo, HttpApplication httpApp)
        {
            var entityType = ExtractEntityType(paramInfo);
            var typeName = entityType != null
                ? $"StorableEntity<{Parameter.GetTypeName(entityType, httpApp)}>"
                : Parameter.GetTypeName(paramInfo.ParameterType, httpApp);
            return new Parameter(paramInfo)
            {
                Name = paramInfo.Name,
                Required = true,
                Where = "BODY",
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
            if (def != typeof(StorableEntity<>))
                return null;
            return t.GenericTypeArguments[0];
        }

        private static TResult DeserializeJsonToEntity<TResult>(
                string contentString, Type entityType, BindConvert bindConvert,
            Func<object, TResult> onParsed,
            Func<string, TResult> onFailure)
        {
            try
            {
                if (!IsObjectNotArray(contentString))
                    return onFailure("Content is not a valid JSON object.");

                var entity = Newtonsoft.Json.JsonConvert.DeserializeObject(
                    contentString, entityType, bindConvert);
                return onParsed(entity);
            }
            catch (Exception ex)
            {
                return onFailure(ex.Message);
            }

            static bool IsObjectNotArray(string content)
            {
                foreach (var ch in content)
                {
                    if (ch == '{')
                        return true;
                    if (ch == '[')
                        return false;
                }
                return false;
            }
        }

        // Mirror of ResourceAttribute.ParseContentDelegate(IFormCollection,...)
        // but assigning members on `entityType` instead of the wrapping
        // StorableEntity<T> parameter type.
        private static TResult DeserializeFormToEntity<TResult>(
                IFormCollection formData, Type entityType, ParameterInfo parameterInfo,
                IApplication httpApp,
            Func<object, TResult> onParsed,
            Func<string, TResult> onFailure)
        {
            var obj = entityType
                .GetPropertyAndFieldsWithAttributesInterface<IProvideApiValue>(true)
                .Aggregate(Activator.CreateInstance(entityType),
                    (param, memberProvideApiValueTpl) =>
                    {
                        var (member, provideApiValue) = memberProvideApiValueTpl;
                        if (!member.IsSettable())
                            return param;

                        return ResourceAttribute.ParseFormContentDelegate(
                                provideApiValue.GetPropertyName(member), formData,
                                member, parameterInfo, httpApp,
                            paramValue =>
                            {
                                member.SetValue(ref param, paramValue);
                                return param;
                            },
                            why =>
                            {
                                return httpApp.Bind<string, object>(default(string), member,
                                    defaultValue =>
                                    {
                                        member.SetValue(ref param, defaultValue);
                                        return param;
                                    },
                                    _ => param);
                            });
                    });
            return onParsed(obj);
        }
    }
}
