using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Microsoft.Azure.Cosmos.Table;
using BlackBarLabs.Persistence.Azure.StorageTables;
using System.Runtime.InteropServices;

namespace EastFive.Azure.Persistence.StorageTables
{
    public static class KeyExtensions
    {
        public const int MaxGuidsPerProperty = 4000; //64000 / sizeof(default(Guid));
        public const int PartitionKeyRemainder = 13;

        #region PartitionKeyGeneration
        public static string GeneratePartitionKey(this string id)
        {
            var hashCode = GetHashCode(id);

            return (hashCode % PartitionKeyRemainder).ToString(CultureInfo.InvariantCulture);
        }

        private static int GetHashCode(string str)
        {
            var src = new Span<char>(str.ToCharArray());
            var hash1 = (5381 << 16) + 5381;
            var hash2 = hash1;
            
            // 32 bit machines. 
            var pint = MemoryMarshal.Cast<char, int>(src);
            var len = str.Length;
            var pIndex = 0;
            while (len > 2)
            {
                hash1 = ((hash1 << 5) + hash1 + (hash1 >> 27)) ^ pint[pIndex + 0];
                hash2 = ((hash2 << 5) + hash2 + (hash2 >> 27)) ^ pint[pIndex + 1];
                pIndex += 2;
                len -= 4;
            }
 
            if (len > 0)
            {
                hash1 = ((hash1 << 5) + hash1 + (hash1 >> 27)) ^ pint[pIndex + 0];
            }
            return hash1 + (hash2 * 1566083941);
        }
        #endregion


        #region ByteArray
        
        /// <summary>
        /// Index 0 is even so starts with even (comp sci, not math)
        /// </summary>
        public static IEnumerable<KeyValuePair<TEven, TOdd>> SelectEvenOdd<TSelect, TEven, TOdd>(
            this IEnumerable<TSelect> items, Func<TSelect, TEven> evenSelect, Func<TSelect, TOdd> oddSelect)
        {
            var itemsEnumerator = items.GetEnumerator();
            while (itemsEnumerator.MoveNext())
            {
                var evenValue = evenSelect.Invoke(itemsEnumerator.Current);
                if (!itemsEnumerator.MoveNext())
                    break;
                yield return new KeyValuePair<TEven, TOdd>(evenValue, oddSelect.Invoke(itemsEnumerator.Current));
            }
        }

        internal static List<Guid> GetGuidStorageString(this string storageString)
        {
            var list = GetGuidStorage(storageString);
            return list ?? new List<Guid>();
        }

        public static string SetGuidStorageString(this List<Guid> steps)
        {
            return Encode(steps);
        }
        
        public static string SetGuidStorageString(this Guid [] steps)
        {
            return SetGuidStorageString(new List<Guid>(steps));
        }

        private static List<Guid> GetGuidStorage(string storage)
        {
            return storage == null ? new List<Guid>() : Decode<List<Guid>>(storage);
        }
        #endregion

        #region Hashes

        #endregion

        #region Object Storage String
        internal static List<Guid> GetTDocumentStorageString(this string storageString)
        {
            return GetGuidStorage(storageString);
        }

        public static string SetTDocumentStorageString<TDocument>(List<TDocument> steps)
        {
            return Encode(steps);
        }

        private static List<TDocument> GetTDocumentStorage<TDocument>(string storage)
        {
            return storage == null ? new List<TDocument>() : Decode<List<TDocument>>(storage);
        }
        #endregion 


        internal static T Decode<T>(string value)
        {
            value = value.Replace("-", "");
            if (String.IsNullOrWhiteSpace(value))
            {
                return default(T);
            }
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore, // prevents XXE attacks, such as Billion Laughs
                    MaxCharactersFromEntities = 1024,
                    XmlResolver = null,                   // prevents external entity DoS attacks, such as slow loading links or large file requests
                };
                using (var strReader = new StringReader(value))
                using (var xmlReader = XmlReader.Create(strReader, settings))
                {
                    var serializer = new DataContractSerializer(typeof(T));
                    T result = (T)serializer.ReadObject(xmlReader);
                    return result;
                }
            }
            catch (SerializationException)
            {
                return default(T);
            }
        }

        internal static string Encode<T>(T value)
        {
            if (EqualityComparer<T>.Default.Equals(value))
            {
                return String.Empty;
            }
            string serializedString;
            using (MemoryStream memoryStream = new MemoryStream())
            using (StreamReader reader = new StreamReader(memoryStream))
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(T));
                serializer.WriteObject(memoryStream, value);
                memoryStream.Position = 0;
                serializedString = reader.ReadToEnd();
            }
            return serializedString;
        }

    }
}
