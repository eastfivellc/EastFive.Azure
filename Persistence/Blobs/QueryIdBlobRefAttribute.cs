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
using EastFive.Azure.Persistence.Blobs;

namespace EastFive.Azure.Persistence
{
    //[Obsolete("Please just use standard bindings")]
    //public class QueryIdBlobRefAttribute : BlobRefProperty
    //{
    //    public override SelectParameterResult TryCast(BindingData bindingData)
    //    {
    //        var parameterRequiringValidation = bindingData.parameterRequiringValidation;
    //        var key = GetKey(parameterRequiringValidation);

    //        return bindingData.method.DeclaringType
    //            .GetPropertyAndFieldsWithAttributesInterface<IProvideApiValue>()
    //            .Where(property => property.Item2.PropertyName == key)
    //            .Where(property => typeof(IBlobRef).IsAssignableFrom(property.Item1.GetPropertyOrFieldType()))
    //            .First(
    //                (memberAndAttr, next) =>
    //                {
    //                    var blobContainerName = ContainerName.HasBlackSpace()?
    //                        ContainerName
    //                        :
    //                        memberAndAttr.Item1.BlobContainerName();
    //                    return bindingData.fetchQueryParam(parameterRequiringValidation,
    //                        v =>
    //                        {
    //                            var blobRef = (v as IBlobRef);
    //                            if(blobRef.ContainerName.HasBlackSpace())
    //                                return SelectParameterResult.Query(blobRef, key, parameterRequiringValidation);

    //                            var apiBlobRef = (v as IApiBoundBlobRef);
    //                            apiBlobRef.ContainerName = blobContainerName;

    //                            return SelectParameterResult.Query(apiBlobRef, key, parameterRequiringValidation);
    //                        },
    //                        whyQuery =>
    //                        {
    //                            return bindingData.fetchDefaultParam(parameterRequiringValidation,
    //                                (v) =>
    //                                {
    //                                    var blobRef = (v as IApiBoundBlobRef);
    //                                    blobRef.ContainerName = blobContainerName;
    //                                    return SelectParameterResult.File(blobRef, key, parameterRequiringValidation);
    //                                },
    //                                (whyFile) =>
    //                                {
    //                                    return SelectParameterResult.FailureQuery(
    //                                        whyQuery, key, parameterRequiringValidation);
    //                                });
    //                        });
    //                },
    //                () =>
    //                {
    //                    return SelectParameterResult.FailureQuery(
    //                        $"Could not match API field with key:`{key}` of type IBlobRef",
    //                        key,
    //                        parameterRequiringValidation);
    //                });
    //    }
    //}

    //[Obsolete("Please just use standard bindings")]
    //public class BlobRefProperty : PropertyAttribute
    //{
    //    /// <summary>Directly specifies the container name for the IBlobRef.</summary>
    //    /// <remarks>
    //    /// Must set one of <para name="PropertyName">PropertyName</para> or
    //    /// <param name="ContainerName"></param> so the appropriate container can be resolved.
    //    /// </remarks>
    //    public string ContainerName { get; set; }

    //    public override SelectParameterResult TryCast(BindingData bindingData)
    //    {
    //        var parameterRequiringValidation = bindingData.parameterRequiringValidation;
    //        var key = GetKey(parameterRequiringValidation);

    //        return bindingData.method.DeclaringType
    //            .GetPropertyAndFieldsWithAttributesInterface<IProvideApiValue>()
    //            .Where(property => property.Item2.PropertyName == key)
    //            .Where(property => typeof(IBlobRef).IsAssignableFrom(property.Item1.GetPropertyOrFieldType()))
    //            .First(
    //                (memberAndAttr, next) =>
    //                {
    //                    return bindingData.fetchBodyParam(parameterRequiringValidation,
    //                        v =>
    //                        {
    //                            var blobRef = (v as IBlobRef);
    //                            if (blobRef.ContainerName.HasBlackSpace())
    //                                return SelectParameterResult.Body(blobRef, key, parameterRequiringValidation);

    //                            if(!(v is IApiBoundBlobRef))
    //                                return SelectParameterResult.FailureBody(
    //                                    $"{v.GetType().FullName} is not {nameof(IApiBoundBlobRef)} and has empty {nameof(IBlobRef.ContainerName)}",
    //                                    key, parameterRequiringValidation);

    //                            var apiBlobRef = (v as IApiBoundBlobRef);
    //                            apiBlobRef.ContainerName = ContainerName.HasBlackSpace() ?
    //                                ContainerName
    //                                :
    //                                memberAndAttr.Item1.BlobContainerName();

    //                            return SelectParameterResult.Body(apiBlobRef, key, parameterRequiringValidation);
    //                        },
    //                        whyQuery =>
    //                        {
    //                            return SelectParameterResult.FailureBody(
    //                                whyQuery, key, parameterRequiringValidation);
    //                        });
    //                },
    //                () => SelectParameterResult.FailureBody(
    //                    $"Could not match API field with key:`{key}` of type IBlobRef",
    //                    key,
    //                    parameterRequiringValidation));
    //    }
    //}

    //[Obsolete("Please just use standard bindings")]
    //public class BlobRefPropertyOptional : BlobRefProperty
    //{
    //    public override SelectParameterResult TryCast(BindingData bindingData)
    //    {
    //        var parameterRequiringValidation = bindingData.parameterRequiringValidation;
    //        var parameterType = parameterRequiringValidation.ParameterType;
    //        var baseValue = base.TryCast(bindingData);

    //        if (baseValue.valid)
    //        {
    //            if (parameterType.IsSubClassOfGeneric(typeof(EastFive.Api.Property<>)))
    //            {
    //                var concretePropertyType = typeof(EastFive.Api.Property<>)
    //                    .MakeGenericType(parameterType.GenericTypeArguments);
    //                baseValue.value = Activator.CreateInstance(concretePropertyType, baseValue.value);
    //            }
    //            return baseValue;
    //        }

    //        baseValue.valid = true;
    //        baseValue.fromBody = true;
    //        baseValue.value = GetValue();
    //        return baseValue;

    //        object GetValue()
    //        {
    //            if (parameterType.IsSubClassOfGeneric(typeof(EastFive.Api.Property<>)))
    //            {
    //                var refType = parameterType.GenericTypeArguments.First();
    //                return refType.GetDefault();
    //                //var parameterTypeGeneric = RefOptionalHelper.CreateEmpty(refType);
    //                //return parameterTypeGeneric;
    //            }
    //            return parameterType.GetDefault();
    //        }
    //    }

    //    public override Api.Resources.Parameter GetParameter(ParameterInfo paramInfo, HttpApplication httpApp)
    //    {
    //        var parameter = base.GetParameter(paramInfo, httpApp);
    //        parameter.Required = false;
    //        return parameter;
    //    }
    //}
}
