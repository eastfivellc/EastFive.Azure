using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Api;
using EastFive.Serialization;
using EastFive.Reflection;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure.Persistence
{
    public class QueryIdBlobRefAttribute : BlobRefProperty
    {
        public override SelectParameterResult TryCast(BindingData bindingData)
        {
            var parameterRequiringValidation = bindingData.parameterRequiringValidation;
            var key = GetKey(parameterRequiringValidation);

            return bindingData.method.DeclaringType
                .GetPropertyAndFieldsWithAttributesInterface<IProvideApiValue>()
                .Where(property => property.Item2.PropertyName == key)
                .Where(property => typeof(IBlobRef).IsAssignableFrom(property.Item1.GetPropertyOrFieldType()))
                .First(
                    (memberAndAttr, next) =>
                    {
                        var blobContainerName = ContainerName.HasBlackSpace()?
                            ContainerName
                            :
                            memberAndAttr.Item1.BlobContainerName();
                        return bindingData.fetchQueryParam(parameterRequiringValidation,
                            v =>
                            {
                                var blobRef = new BlobRef
                                {
                                    Id = (v as IBlobRef).Id,
                                    ContainerName = blobContainerName,
                                };
                                return SelectParameterResult.Query(blobRef, key, parameterRequiringValidation);
                            },
                            whyQuery =>
                            {
                                return bindingData.fetchDefaultParam(parameterRequiringValidation,
                                    (v) =>
                                    {
                                        var blobRef = new BlobRef
                                        {
                                            Id = (v as IBlobRef).Id,
                                            ContainerName = blobContainerName,
                                        };
                                        return SelectParameterResult.File(blobRef, key, parameterRequiringValidation);
                                    },
                                    (whyFile) =>
                                    {
                                        return SelectParameterResult.FailureQuery(
                                            whyQuery, key, parameterRequiringValidation);
                                    });
                            });
                    },
                    () => SelectParameterResult.FailureQuery(
                        $"Could not match API field with key:`{key}` of type IBlobRef",
                        key,
                        parameterRequiringValidation));
        }
    }

    public class BlobRefProperty : PropertyAttribute
    {
        /// <summary>Directly specifies the container name for the IBlobRef.</summary>
        /// <remarks>
        /// Must set one of <para name="PropertyName">PropertyName</para> or
        /// <param name="ContainerName"></param> so the appropriate container can be resolved.
        /// </remarks>
        public string ContainerName { get; set; }

        public override SelectParameterResult TryCast(BindingData bindingData)
        {
            var parameterRequiringValidation = bindingData.parameterRequiringValidation;
            var key = GetKey(parameterRequiringValidation);

            return bindingData.method.DeclaringType
                .GetPropertyAndFieldsWithAttributesInterface<IProvideApiValue>()
                .Where(property => property.Item2.PropertyName == key)
                .Where(property => typeof(IBlobRef).IsAssignableFrom(property.Item1.GetPropertyOrFieldType()))
                .First(
                    (memberAndAttr, next) =>
                    {
                        var blobContainerName = ContainerName.HasBlackSpace() ?
                            ContainerName
                            :
                            memberAndAttr.Item1.BlobContainerName();
                        return bindingData.fetchBodyParam(parameterRequiringValidation,
                            v =>
                            {
                                var blobRef = new BlobRef
                                {
                                    Id = (v as IBlobRef).Id,
                                    ContainerName = blobContainerName,
                                };
                                return SelectParameterResult.Query(blobRef, key, parameterRequiringValidation);
                            },
                            whyQuery =>
                            {
                                return SelectParameterResult.FailureQuery(
                                    whyQuery, key, parameterRequiringValidation);
                            });
                    },
                    () => SelectParameterResult.FailureQuery(
                        $"Could not match API field with key:`{key}` of type IBlobRef",
                        key,
                        parameterRequiringValidation));
        }

        protected class BlobRef : IBlobRef
        {
            public string Id { get; set; }

            public string ContainerName { get; set; }
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
