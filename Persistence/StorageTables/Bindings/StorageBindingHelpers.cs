using System;
using System.Linq;
using System.Reflection;

using Microsoft.AspNetCore.Http;

using EastFive.Api;
using EastFive.Api.Bindings;
using EastFive.Api.Serialization;
using EastFive.Extensions;
using EastFive.Reflection;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Public deserialization helpers used by the V3 storage TypeBinders
    /// (<c>StorableEntityBinder</c>, etc.) to materialize an entity instance
    /// from a request body. Lifted from the private copies that previously
    /// lived on <c>StorableEntityFromResourceAttribute</c> — same semantics,
    /// reachable from the binder layer.
    /// </summary>
    public static class StorageBindingHelpers
    {
        /// <summary>
        /// Deserialize a JSON object string into <paramref name="entityType"/>
        /// using the application's <see cref="BindConvert"/>. Rejects JSON
        /// arrays at the top level (callers want an object, not a collection).
        /// </summary>
        public static TResult DeserializeJsonToEntity<TResult>(
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
        }

        /// <summary>
        /// Deserialize an <see cref="IFormCollection"/> into
        /// <paramref name="entityType"/> by aggregating per-member
        /// <c>[IProvideApiValue]</c> assignments via
        /// <c>ResourceAttribute.ParseFormContentDelegate</c>.
        /// </summary>
        public static TResult DeserializeFormToEntity<TResult>(
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

        /// <summary>
        /// Extract <c>T</c> from a parameter typed as
        /// <c>StorableEntity&lt;T&gt;</c>, <c>StorageEntity&lt;T&gt;</c>, or
        /// <c>IQueryable&lt;T&gt;</c>. Returns null if the parameter is not
        /// a matching closed generic. Pure shape inspection — no attribute
        /// scanning.
        /// </summary>
        public static Type ExtractEntityType(Type parameterType, Type genericDefinition)
        {
            if (parameterType is null || genericDefinition is null)
                return null;
            if (!parameterType.IsGenericType)
                return null;
            if (parameterType.GetGenericTypeDefinition() != genericDefinition)
                return null;
            return parameterType.GenericTypeArguments[0];
        }

        private static bool IsObjectNotArray(string content)
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
}
