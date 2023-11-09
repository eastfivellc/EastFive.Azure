using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace EastFive.Azure.Search
{
    public interface IProvideSearchSerialization
    {
        Type BuildSearchResultsType<T>();
        TResponse CastResult<TResponse, TIntermediary>(TIntermediary doc);
        object GetSerializedObject<T>(T item);
    }
}
