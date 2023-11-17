using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Azure.Search.Documents.Models;

using EastFive.Api;
using EastFive.Api.Resources;
using EastFive.Api.Serialization;
using EastFive.Extensions;
using EastFive.Linq.Async;
using Microsoft.AspNetCore.Http.Headers;
using Newtonsoft.Json;

namespace EastFive.Azure.Search.Api
{
    [SearchResultsResponseGeneric]
	public delegate IHttpResponse SearchResultsResponse<T>(IEnumerableAsync<T> items,
        IDictionary<string, Func<FacetResult[]>> facetResults =default,
        Func<long> getTotals = default);

    public class SearchResultsResponseGenericAttribute : HttpGenericDelegateAttribute, IProvideResponseType
    {
        public override HttpStatusCode StatusCode => HttpStatusCode.OK;

        public override string Example => "[]";

        [InstigateMethod]
        public IHttpResponse EnumerableAsyncHttpResponse<T>(IEnumerableAsync<T> items,
            IDictionary<string, Func<FacetResult[]>> facetResults = default, Func<long> getTotals = default)
        {
            var response = new SearchResponse<T>(this.httpApp, request, this.parameterInfo,
                this.StatusCode,
                items, facetResults, getTotals);
            return UpdateResponse(parameterInfo, httpApp, request, response);
        }

        public override Response GetResponse(ParameterInfo paramInfo, HttpApplication httpApp)
        {
            var baseResponse = base.GetResponse(paramInfo, httpApp);
            baseResponse.IsMultipart = true;
            return baseResponse;
        }

        public Type GetResponseType(ParameterInfo parameterInfo)
        {
            return parameterInfo.ParameterType.GenericTypeArguments.First();
        }

        public class SearchResponse<T> : EastFive.Api.HttpResponse
        {
            private IEnumerableAsync<T> items;
            private IApplication application;
            private ParameterInfo parameterInfo;
            private IDictionary<string, Func<FacetResult[]>> facetResults;
            private Func<long> getTotals;

            public SearchResponse(IApplication application,
                IHttpRequest request, ParameterInfo parameterInfo, HttpStatusCode statusCode,
                IEnumerableAsync<T> items, IDictionary<string, Func<FacetResult[]>> facetResults, Func<long> getTotals = default)
                : base(request, statusCode)
            {
                this.application = application;
                this.items = items;
                this.parameterInfo = parameterInfo;
                this.facetResults = facetResults;
                this.getTotals = getTotals;
            }

            public override void WriteHeaders(Microsoft.AspNetCore.Http.HttpContext context, ResponseHeaders headers)
            {
                base.WriteHeaders(context, headers);
                headers.ContentType = new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            }

            public override async Task WriteResponseAsync(Stream responseStream)
            {
                using (var streamWriter =
                    this.Request.TryGetAcceptCharset(out Encoding writerEncoding) ?
                        new StreamWriter(responseStream, writerEncoding)
                        :
                        new StreamWriter(responseStream, new UTF8Encoding(false)))
                {
                    var settings = new JsonSerializerSettings();
                    settings.Converters.Add(new Converter(this.Request));
                    settings.DefaultValueHandling = DefaultValueHandling.Include;

                    var enumerator = items.GetEnumerator();
                    await streamWriter.WriteAsync('{');
                    await streamWriter.FlushAsync();
                    bool first = true;
                    try
                    {
                        while (await enumerator.MoveNextAsync())
                        {
                            if (first)
                            {
                                if (this.facetResults.IsNotDefaultOrNull())
                                {
                                    var facetString = this.facetResults
                                            .Select(
                                                keyGetFacetResult =>
                                                {
                                                    var key = keyGetFacetResult.Key;
                                                    var facetResults = keyGetFacetResult.Value();
                                                    var facetResultString = facetResults
                                                        .Select(facetResult => SerializeFacet(facetResult))
                                                        .Join(",");
                                                    return $"{{\"key\":\"{key}\",\n\"results\":[{facetResultString}]}}";
                                                })
                                            .Join(",");
                                    if (facetString.HasBlackSpace())
                                    {
                                        await streamWriter.WriteAsync($"\"facets\":[{facetString}],");
                                    }
                                }

                                if(this.getTotals.IsNotDefaultOrNull())
                                {
                                    var totals = this.getTotals();
                                    await streamWriter.WriteAsync($"\"total\":{totals},");
                                }

                                await streamWriter.WriteAsync($"\"results\":[");
                                await streamWriter.FlushAsync();

                            }
                            else
                            {
                                await streamWriter.WriteAsync(',');
                                await streamWriter.FlushAsync();
                            }
                            first = false;

                            var obj = enumerator.Current;
                            var objType = (obj == null) ?
                                typeof(T)
                                :
                                obj.GetType();
                            if (!objType.ContainsAttributeInterface<IProvideSerialization>())
                            {
                                var contentJsonString = JsonConvert.SerializeObject(obj, settings);
                                await streamWriter.WriteAsync(contentJsonString);
                                await streamWriter.FlushAsync();
                                continue;
                            }

                            var serializationProvider = objType
                                .GetAttributesInterface<IProvideSerialization>()
                                .OrderByDescending(x => x.GetPreference(this.Request))
                                .First();
                            await serializationProvider.SerializeAsync(responseStream,
                                application, this.Request, this.parameterInfo, obj);
                            await streamWriter.FlushAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        await streamWriter.WriteAsync(ex.Message);
                    }
                    finally
                    {
                        await streamWriter.WriteAsync("]}");
                        await streamWriter.FlushAsync();
                    }
                }

                string SerializeFacet(FacetResult facet)
                {
                    return $"{{\"value\":\"{facet.Value}\",\"count\":{facet.Count}}}";
                }
            }
        }
    }
}

