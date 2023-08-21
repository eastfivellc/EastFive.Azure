using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public interface IRefBlob
    {
        string Path { get; }
        string Container { get; }
    }

    public class RefBlob : IRefBlob
    {
        public string Path { get; private set; }

        public string Container { get; private set; }

        public RefBlob(string container, string path)
        {
            this.Container = container;
            this.Path = path;
        }
    }

    public static class RefBlobExtensions
    {
        public static IRefBlob AsBlobRef(this string container, string path)
        {
            return new RefBlob(container, path);
        }
    }
}
