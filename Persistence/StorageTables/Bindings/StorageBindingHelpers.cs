using System;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Shared helpers for V3 storage binders. Currently exposes the shape
    /// inspection used by <c>StorableEntityBinder</c> /
    /// <c>StorageEntityBinder</c> to decide whether they should attempt to
    /// bind a parameter type.
    /// </summary>
    public static class StorageBindingHelpers
    {
        /// <summary>
        /// Extract <c>T</c> from a parameter typed as
        /// <c>StorableEntity&lt;T&gt;</c>, <c>StorageEntity&lt;T&gt;</c>, or
        /// <c>IQueryable&lt;T&gt;</c>. Returns null if the parameter is not
        /// a matching closed generic. Pure shape inspection — no attribute
        /// scanning.
        /// </summary>
        public static Type ExtractEntityType(Type parameterType, Type genericDefinition)
        {
            if (parameterType is null || genericDefinition is null)
                return null;
            if (!parameterType.IsGenericType)
                return null;
            if (parameterType.GetGenericTypeDefinition() != genericDefinition)
                return null;
            return parameterType.GenericTypeArguments[0];
        }
    }
}
