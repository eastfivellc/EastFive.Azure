using System;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence.StorageTables
{
	public interface ICastEntityProperty
	{
        TResult CastEntityProperty<TResult>(object value, Type valueType,
            Func<EntityProperty, TResult> onValue,
            Func<TResult> onNoCast);
    }
}

