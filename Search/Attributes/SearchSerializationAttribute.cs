using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using EastFive;
using EastFive.Serialization;
using EastFive.Linq;
using EastFive.Extensions;
using System.Dynamic;
using EastFive.Reflection;
using System.Reflection;
using System.Reflection.Emit;
using EastFive.Serialization.Text;

namespace EastFive.Azure.Search
{
    public class SearchSerializationAttribute : Attribute, IProvideSearchSerialization
    {
        public Type BuildSearchResultsType<T>()
        {
            var unsearchableType = typeof(T);
            var aName = new AssemblyName("DynamicAssemblyExample");
            AssemblyBuilder ab =
                AssemblyBuilder.DefineDynamicAssembly(
                    aName,
                    AssemblyBuilderAccess.Run);

            // The module name is usually the same as the assembly name.
            ModuleBuilder mb = ab.DefineDynamicModule(aName.Name ?? "DynamicAssemblyExample");

            TypeBuilder tb = mb.DefineType(
                $"{unsearchableType.Name}_SearchResult",
                 TypeAttributes.Public);

            var populatedTypeBuilder = unsearchableType
                .GetPropertyAndFieldsWithAttributesInterface<IProvideSearchField>()
                .Aggregate(
                    tb,
                    (dyn, memberInfoAndAttr) =>
                    {
                        var (memberInfo, searchFieldProvider) = memberInfoAndAttr;
                        var fieldName = searchFieldProvider.GetKeyName(memberInfo);
                        var type = memberInfo.GetPropertyOrFieldType();
                        if (type.IsSubClassOfGeneric(typeof(IReferenceable)))
                            type = typeof(string);
                        if (type.IsSubClassOfGeneric(typeof(IReferenceableOptional)))
                            type = typeof(string);
                        if (type == typeof(Guid))
                            type = typeof(string);
                        if(type.IsEnum)
                            type = typeof(string);
                        if (type.TryGetNullableUnderlyingType(out Type nonNullableType))
                            if(nonNullableType.IsEnum)
                                type = typeof(string);

                        FieldBuilder fbNumber = tb.DefineField(
                            $"_{fieldName}",
                            type,
                            FieldAttributes.Public);

                        PropertyBuilder pbNumber = tb.DefineProperty(
                            fieldName,
                            PropertyAttributes.HasDefault,
                            type,
                            null);

                        // The property "set" and property "get" methods require a special
                        // set of attributes.
                        MethodAttributes getSetAttr = MethodAttributes.Public |
                            MethodAttributes.SpecialName | MethodAttributes.HideBySig;

                        // Define the "get" accessor method for Number. The method returns
                        // an integer and has no arguments. (Note that null could be
                        // used instead of Types.EmptyTypes)
                        MethodBuilder mbNumberGetAccessor = tb.DefineMethod(
                            $"get_{fieldName}",
                            getSetAttr,
                            type,
                            Type.EmptyTypes);

                        ILGenerator numberGetIL = mbNumberGetAccessor.GetILGenerator();
                        // For an instance property, argument zero is the instance. Load the
                        // instance, then load the private field and return, leaving the
                        // field value on the stack.
                        numberGetIL.Emit(OpCodes.Ldarg_0);
                        numberGetIL.Emit(OpCodes.Ldfld, fbNumber);
                        numberGetIL.Emit(OpCodes.Ret);

                        // Define the "set" accessor method for Number, which has no return
                        // type and takes one argument of type int (Int32).
                        MethodBuilder mbNumberSetAccessor = tb.DefineMethod(
                            $"set_{fieldName}",
                            getSetAttr,
                            null,
                            new Type[] { type });

                        ILGenerator numberSetIL = mbNumberSetAccessor.GetILGenerator();
                        // Load the instance and then the numeric argument, then store the
                        // argument in the field.
                        numberSetIL.Emit(OpCodes.Ldarg_0);
                        numberSetIL.Emit(OpCodes.Ldarg_1);
                        numberSetIL.Emit(OpCodes.Stfld, fbNumber);
                        numberSetIL.Emit(OpCodes.Ret);

                        // Last, map the "get" and "set" accessor methods to the
                        // PropertyBuilder. The property is now complete.
                        pbNumber.SetGetMethod(mbNumberGetAccessor);
                        pbNumber.SetSetMethod(mbNumberSetAccessor);

                        
                        return tb;
                    });


            // Finish the type.
            var searchResultsType = populatedTypeBuilder.CreateType();
            return searchResultsType;
        }

        public TResponse CastResult<TResponse, TIntermediary>(TIntermediary intermediary)
        {
            var intermediaryProps = typeof(TIntermediary).GetProperties();
            var newObj = typeof(TResponse)
                .GetPropertyAndFieldsWithAttributesInterface<IProvideSearchField>()
                .Aggregate(
                    Activator.CreateInstance<TResponse>(),
                    (dyn, memberInfoAndAttr) =>
                    {
                        var (memberInfo, searchFieldProvider) = memberInfoAndAttr;

                        var key = searchFieldProvider.GetKeyName(memberInfo);
                        return intermediaryProps
                            .Where(prop => prop.Name == key)
                            .First(
                                (prop, next) =>
                                {
                                    var v = prop.GetValue(intermediary);
                                    if (v is string)
                                    {
                                        var vString = (string)v;
                                        var assigner = memberInfo.ParseTextAsAssignment<TResponse>(
                                            memberInfo.GetPropertyOrFieldType(), vString, StringComparison.Ordinal);
                                        return assigner(dyn);
                                    }
                                    var updatedObj = memberInfo.SetPropertyOrFieldValue(dyn, v);
                                    return (TResponse)updatedObj;
                                },
                                () => dyn);
                    });
            return newObj;
        }

        public object GetSerializedObject<T>(T item)
        {
            var newObj = typeof(T)
                .GetPropertyAndFieldsWithAttributesInterface<IProvideSearchField>()
                .Aggregate(
                    new ExpandoObject(),
                    (dyn, memberInfoAndAttr) =>
                    {
                        var (memberInfo, searchFieldProvider) = memberInfoAndAttr;
                        var v = memberInfo.GetPropertyOrFieldValue(item);
                        var memberType = memberInfo.GetPropertyOrFieldType();
                        var dict = dyn as IDictionary<string, Object>;
                        var key = searchFieldProvider.GetKeyName(memberInfo);
                        if (v.IsDefaultOrNull())
                        {
                            // dict.Add(key, v);
                            return dyn;
                        }

                        if (memberType.IsSubClassOfGeneric(typeof(IReferenceable)))
                            v = ((IReferenceable)v).id;

                        if (memberType.IsEnum)
                            v = Enum.GetName(memberType, v);

                        if(memberType.TryGetNullableUnderlyingType(out Type nonNullableType))
                            if(nonNullableType.IsEnum)
                                v = v.NullableHasValue()?
                                    Enum.GetName(nonNullableType, v)
                                    :
                                    default(string);

                        dict.Add(key, v);
                        return dyn;
                    });
            return newObj;
        }
    }
}
