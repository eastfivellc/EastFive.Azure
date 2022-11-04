using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Serialization;
using EastFive.Linq;

namespace EastFive.Net.Http
{
    public class MessageHandlerCacheAzureStorageTables : MessageHandlerCache
    {
        private BlackBarLabs.Persistence.Azure.StorageTables.AzureStorageRepository repository;

        private const int propSizeLimit = 32000;

        public MessageHandlerCacheAzureStorageTables(BlackBarLabs.Persistence.Azure.StorageTables.AzureStorageRepository repository,
            HttpMessageHandler innerHandler = default(HttpMessageHandler))
            : base(innerHandler)
        {
            this.repository = repository;
        }

        public class CacheDocument : Microsoft.Azure.Cosmos.Table.TableEntity
        {
            public byte[] Data1 { get; set; }
            public byte[] Data2 { get; set; }
            public byte[] Data3 { get; set; }
            public byte[] Data4 { get; set; }
            public byte[] Data5 { get; set; }
            public byte[] Data6 { get; set; }
            public byte[] Data7 { get; set; }
            public byte[] Data8 { get; set; }
            public byte[] Data9 { get; set; }
            public byte[] Data10 { get; set; }
            public byte[] Data11 { get; set; }
            public byte[] Data12 { get; set; }
            public byte[] Data13 { get; set; }
            public byte[] Data14 { get; set; }
            public byte[] Data15 { get; set; }
            public byte[] Data16 { get; set; }
            public byte[] Data17 { get; set; }
            public byte[] Data18 { get; set; }
            public byte[] Data19 { get; set; }
            public byte[] Data20 { get; set; }
            public byte[] Data21 { get; set; }
            public byte[] Data22 { get; set; }
            public byte[] Data23 { get; set; }
            public byte[] Data24 { get; set; }
            public byte[] Data25 { get; set; }
            public byte[] Data26 { get; set; }
            public byte[] Data27 { get; set; }
            public byte[] Data28 { get; set; }
            public byte[] Data29 { get; set; }
            public byte[] Data30 { get; set; }
            public byte[] Data31 { get; set; }
            public byte[] Data32 { get; set; }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (IsNoCache(request))
                return await base.SendAsync(request, cancellationToken);
            
            var lookupGuid = request.RequestUri.AbsoluteUri.MD5HashGuid();
            return await await this.repository.FindByIdAsync(lookupGuid,
                (CacheDocument doc) =>
                {
                    return GenerateResponse(
                        doc.Data1
                            .NullToEmpty()
                            .Concat(doc.Data2.NullToEmpty())
                            .Concat(doc.Data3.NullToEmpty())
                            .Concat(doc.Data4.NullToEmpty())
                            .Concat(doc.Data5.NullToEmpty())
                            .Concat(doc.Data6.NullToEmpty())
                            .Concat(doc.Data7.NullToEmpty())
                            .Concat(doc.Data8.NullToEmpty())
                            .Concat(doc.Data9.NullToEmpty())
                            .Concat(doc.Data10.NullToEmpty())
                            .Concat(doc.Data11.NullToEmpty())
                            .Concat(doc.Data12.NullToEmpty())
                            .Concat(doc.Data13.NullToEmpty())
                            .Concat(doc.Data14.NullToEmpty())
                            .Concat(doc.Data15.NullToEmpty())
                            .Concat(doc.Data16.NullToEmpty())
                            .Concat(doc.Data17.NullToEmpty())
                            .Concat(doc.Data18.NullToEmpty())
                            .Concat(doc.Data19.NullToEmpty())
                            .Concat(doc.Data20.NullToEmpty())
                            .Concat(doc.Data21.NullToEmpty())
                            .Concat(doc.Data22.NullToEmpty())
                            .Concat(doc.Data23.NullToEmpty())
                            .Concat(doc.Data24.NullToEmpty())
                            .Concat(doc.Data25.NullToEmpty())
                            .Concat(doc.Data26.NullToEmpty())
                            .Concat(doc.Data27.NullToEmpty())
                            .Concat(doc.Data28.NullToEmpty())
                            .Concat(doc.Data29.NullToEmpty())
                            .Concat(doc.Data30.NullToEmpty())
                            .Concat(doc.Data31.NullToEmpty())
                            .Concat(doc.Data32.NullToEmpty())
                            .ToArray()).AsTask();
                },
                async () =>
                {
                    var response = await base.SendAsync(request, cancellationToken);
                    return await SaveAsync(lookupGuid, response);
                });
        }

        private async Task<HttpResponseMessage> SaveAsync(Guid lookupGuid, HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
                return response;

            // stream is passed to GenerateResponse and disposed there
            var memStream = new MemoryStream();
            await response.Content.CopyToAsync(memStream);
            var data = memStream.ToArray();
            memStream.Seek(0, SeekOrigin.Begin);
            var statusCode = response.StatusCode;
            response.Dispose();

            if (data.Length <= propSizeLimit * 32)
            {
                var doc = new CacheDocument()
                {
                    Data1 = data.Take(propSizeLimit).ToArray(),
                    Data2 = data.Skip(propSizeLimit).Take(propSizeLimit).ToArray(),
                    Data3 = data.Skip(propSizeLimit * 2).Take(propSizeLimit).ToArray(),
                    Data4 = data.Skip(propSizeLimit * 3).Take(propSizeLimit).ToArray(),
                    Data5 = data.Skip(propSizeLimit * 4).Take(propSizeLimit).ToArray(),
                    Data6 = data.Skip(propSizeLimit * 5).Take(propSizeLimit).ToArray(),
                    Data7 = data.Skip(propSizeLimit * 6).Take(propSizeLimit).ToArray(),
                    Data8 = data.Skip(propSizeLimit * 7).Take(propSizeLimit).ToArray(),
                    Data9 = data.Skip(propSizeLimit * 8).Take(propSizeLimit).ToArray(),
                    Data10 = data.Skip(propSizeLimit * 9).Take(propSizeLimit).ToArray(),
                    Data11 = data.Skip(propSizeLimit * 10).Take(propSizeLimit).ToArray(),
                    Data12 = data.Skip(propSizeLimit * 11).Take(propSizeLimit).ToArray(),
                    Data13 = data.Skip(propSizeLimit * 12).Take(propSizeLimit).ToArray(),
                    Data14 = data.Skip(propSizeLimit * 13).Take(propSizeLimit).ToArray(),
                    Data15 = data.Skip(propSizeLimit * 14).Take(propSizeLimit).ToArray(),
                    Data16 = data.Skip(propSizeLimit * 15).Take(propSizeLimit).ToArray(),
                    Data17 = data.Skip(propSizeLimit * 16).Take(propSizeLimit).ToArray(),
                    Data18 = data.Skip(propSizeLimit * 17).Take(propSizeLimit).ToArray(),
                    Data19 = data.Skip(propSizeLimit * 18).Take(propSizeLimit).ToArray(),
                    Data20 = data.Skip(propSizeLimit * 19).Take(propSizeLimit).ToArray(),
                    Data21 = data.Skip(propSizeLimit * 20).Take(propSizeLimit).ToArray(),
                    Data22 = data.Skip(propSizeLimit * 21).Take(propSizeLimit).ToArray(),
                    Data23 = data.Skip(propSizeLimit * 22).Take(propSizeLimit).ToArray(),
                    Data24 = data.Skip(propSizeLimit * 23).Take(propSizeLimit).ToArray(),
                    Data25 = data.Skip(propSizeLimit * 24).Take(propSizeLimit).ToArray(),
                    Data26 = data.Skip(propSizeLimit * 25).Take(propSizeLimit).ToArray(),
                    Data27 = data.Skip(propSizeLimit * 26).Take(propSizeLimit).ToArray(),
                    Data28 = data.Skip(propSizeLimit * 27).Take(propSizeLimit).ToArray(),
                    Data29 = data.Skip(propSizeLimit * 28).Take(propSizeLimit).ToArray(),
                    Data30 = data.Skip(propSizeLimit * 29).Take(propSizeLimit).ToArray(),
                    Data31 = data.Skip(propSizeLimit * 30).Take(propSizeLimit).ToArray(),
                    Data32 = data.Skip(propSizeLimit * 31).Take(propSizeLimit).ToArray(),
                };
                bool success = await this.repository.CreateAsync(lookupGuid, doc,
                    () => true,
                    () => false);
            }
            return GenerateResponse(memStream, statusCode);
        }
    }
}
