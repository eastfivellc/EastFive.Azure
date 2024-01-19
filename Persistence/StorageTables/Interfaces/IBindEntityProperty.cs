using System;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence.StorageTables
{
	public interface IBindEntityProperty
    {
        TResult BindEntityProperty<TResult>(EntityProperty value, Type type,
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind);
    }
}

