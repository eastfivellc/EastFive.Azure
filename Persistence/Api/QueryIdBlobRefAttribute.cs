using EastFive.Api;
using EastFive.Extensions;
using EastFive.Persistence.Azure.StorageTables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Persistence
{
    public class QueryIdBlobRefAttribute : QueryParameterAttribute
    {
        /// <summary>The property name set by a <see cref="ApiPropertyAttribute"/>'s 
        ///     <code>PropertyName</code> attribute for the IBlobRef property.
        /// </summary>
        /// <remarks>
        /// Must set one of <para name="PropertyName">PropertyName</para> or
        /// <param name="ContainerName"></param> so the appropriate container can be resolved.
        /// </remarks>
        public string PropertyName { get; set; }

        /// <summary>Directly specifies the container name for the IBlobRef.</summary>
        /// <remarks>
        /// Must set one of <para name="PropertyName">PropertyName</para> or
        /// <param name="ContainerName"></param> so the appropriate container can be resolved.
        /// </remarks>
        public string ContainerName { get; set; }

        public override string Name
        {
            get
            {
                var name = base.Name;
                if (name.HasBlackSpace())
                    return name;
                return "id";
            }
            set => base.Name = value;
        }

        private class BlobRef : IBlobRef
        {
            public string Id { get; private set; }

            public string ContainerName { get; private set; }
        }
    }

    public class BlobRefProperty : PropertyAttribute
    {
        public override TResult Convert<TResult>(HttpApplication httpApp, Type type, object value,
            Func<object, TResult> onCasted,
            Func<string, TResult> onInvalid)
        {
            if (value is Guid?)
            {
                var guidMaybe = value as Guid?;
                if (typeof(Guid).GUID == type.GUID)
                {
                    if (!guidMaybe.HasValue)
                        return onInvalid("Value did not provide a UUID.");
                    return onCasted(guidMaybe.Value);
                }
                if (typeof(Guid?).GUID == type.GUID)
                {
                    return onCasted(guidMaybe);
                }
            }

            return onInvalid($"Could not convert `{value.GetType().FullName}` to `{type.FullName}`.");
        }
    }

    public class BlobRefPropertyOptional : BlobRefProperty
    {
        public override SelectParameterResult TryCast(BindingData bindingData)
        {
            var parameterRequiringValidation = bindingData.parameterRequiringValidation;
            var baseValue = base.TryCast(bindingData);
            if (baseValue.valid)
                return baseValue;

            baseValue.valid = true;
            baseValue.fromBody = true;
            baseValue.value = GetValue();
            return baseValue;

            object GetValue()
            {
                var parameterType = parameterRequiringValidation.ParameterType;
                if (parameterType.IsSubClassOfGeneric(typeof(EastFive.Api.Property<>)))
                {
                    var refType = parameterType.GenericTypeArguments.First();
                    return refType.GetDefault();
                    //var parameterTypeGeneric = RefOptionalHelper.CreateEmpty(refType);
                    //return parameterTypeGeneric;
                }

                return parameterType.GetDefault();
            }
        }

        public override Api.Resources.Parameter GetParameter(ParameterInfo paramInfo, HttpApplication httpApp)
        {
            var parameter = base.GetParameter(paramInfo, httpApp);
            parameter.Required = false;
            return parameter;
        }
    }
}
