using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public interface IBlobRef
    {
        public string ContainerName { get; }
        public string Id { get; } 
    }

    public static class BlobRefExtensions
    {
        public static Task<byte[]> ReadBytesAsync(this IBlobRef blobRef)
        {
            throw new NotImplementedException();
        }

        public static Task<IBlobRef> WriteBytesAsync(byte[] bytes)
        {
            throw new NotImplementedException();
        }
    }
}
