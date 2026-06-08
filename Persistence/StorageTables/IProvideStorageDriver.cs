using System.Linq;
using System.Reflection;

using EastFive.Linq;
using EastFive.Reflection;
using EastFive.Persistence.Azure.StorageTables.Driver;

namespace EastFive.Azure.Persistence.StorageTables
{
    /// <summary>
    /// Decorator interface implemented by per-datastore attributes (e.g.
    /// <c>[RosemaryDataStorage]</c>, <c>[RosemaryDataLake]</c>). The loader
    /// attribute walks parameter → method → declaring type → assembly to find
    /// the first <see cref="IProvideStorageDriver"/> in scope and uses it to
    /// build the driver for the load.
    /// </summary>
    /// <remarks>
    /// MIXING CONCERN: this interface returns a concrete
    /// <see cref="AzureTableDriverDynamic"/> today. When the driver is
    /// abstracted into <c>IStorageDriver</c>, this interface's return type
    /// becomes that interface and every implementor adapts.
    /// </remarks>
    public interface IProvideStorageDriver
    {
        AzureTableDriverDynamic GetDriver();
    }

    /// <summary>
    /// Resolves the first <see cref="IProvideStorageDriver"/> in scope for a
    /// given parameter following the lexical-scope walk:
    /// parameter → method → declaring type → assembly.
    /// </summary>
    public static class StorageDriverScope
    {
        /// <summary>
        /// Walks parameter → method → declaring type → assembly. Returns the
        /// first <see cref="IProvideStorageDriver"/> attribute found.
        /// Throws when none is in scope — that's a controller-wiring bug.
        /// </summary>
        public static IProvideStorageDriver Resolve(ParameterInfo parameter)
        {
            var method = parameter.Member as MethodInfo;
            var declaringType = method?.DeclaringType;

            return parameter.AttributeInterfacesInParameter<IProvideStorageDriver>()
                .Concat(method.AttributeInterfacesInMethod<IProvideStorageDriver>())
                .Concat(declaringType.AttributeInterfacesInType<IProvideStorageDriver>(
                    inherit: true, multiple: true))
                .Concat((declaringType?.Assembly).AttributeInterfacesInAssembly<IProvideStorageDriver>())
                .First<IProvideStorageDriver, IProvideStorageDriver>(
                    (provider, next) => provider,
                    () => throw new System.InvalidOperationException(
                        $"No [IProvideStorageDriver] attribute in scope for parameter " +
                        $"'{parameter.Name}' of '{method?.DeclaringType?.FullName}.{method?.Name}'. " +
                        $"Apply one (e.g. [RosemaryDataStorage]) at the parameter, method, " +
                        $"declaring type, or assembly."));
        }

        /// <summary>
        /// Resolves the first <see cref="IProvideStorageDriver"/> in scope for a
        /// resource type following the lexical-scope walk: declaring type →
        /// assembly. Used by generated static query helpers that have no
        /// <see cref="ParameterInfo"/> to anchor on.
        /// Throws when none is in scope — that's a controller-wiring bug.
        /// </summary>
        public static IProvideStorageDriver Resolve(System.Type type)
        {
            return type.AttributeInterfacesInType<IProvideStorageDriver>(
                    inherit: true, multiple: true)
                .Concat(type.Assembly.AttributeInterfacesInAssembly<IProvideStorageDriver>())
                .First<IProvideStorageDriver, IProvideStorageDriver>(
                    (provider, next) => provider,
                    () => throw new System.InvalidOperationException(
                        $"No [IProvideStorageDriver] attribute in scope for type " +
                        $"'{type.FullName}'. Apply one (e.g. [RosemaryDataStorage]) at the " +
                        $"type or assembly."));
        }
    }
}
