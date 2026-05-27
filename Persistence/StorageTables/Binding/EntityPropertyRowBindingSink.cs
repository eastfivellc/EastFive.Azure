using System;
using System.Collections.Generic;
using EastFive.Serialization;
using EastFive.Serialization.Binding;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence.StorageTables.Binding
{
    /// <summary>
    /// <see cref="IBindingSink"/> over an Azure Table row. Collects writes into
    /// <see cref="Row"/> as a flat dictionary of column-name → <see cref="EntityProperty"/>.
    /// <para>
    /// The row sink itself is composite-only: scalar writes against it throw
    /// (a row is not a scalar). Use <see cref="Scope"/> to descend into a column
    /// and emit a scalar value there.
    /// </para>
    /// <para>
    /// Null encoding is a single sentinel — an <see cref="EntityProperty"/> wrapping
    /// a <c>null</c> byte array. The matching <c>EntityPropertyBindingSource.IsNullScalar</c>
    /// recognizes both <c>null</c> and zero-length binary as null, so any nullable
    /// target type (Nullable&lt;T&gt;, IRefOptional&lt;T&gt;) round-trips correctly
    /// regardless of its underlying EdmType.
    /// </para>
    /// </summary>
    public sealed class EntityPropertyRowBindingSink : IBindingSink
    {
        public IDictionary<string, EntityProperty> Row { get; }

        public EntityPropertyRowBindingSink()
            : this(new Dictionary<string, EntityProperty>())
        { }

        public EntityPropertyRowBindingSink(IDictionary<string, EntityProperty> row)
        {
            Row = row ?? throw new ArgumentNullException(nameof(row));
        }

        private static InvalidOperationException Scalar(string op) =>
            new($"Cannot {op} directly on a row sink. Use Scope(column) to descend into a column first.");

        public void WriteString(string value) => throw Scalar("write a string");
        public void WriteGuid(Guid value)     => throw Scalar("write a guid");
        public void WriteBool(bool value)     => throw Scalar("write a bool");
        public void WriteInt32(int value)     => throw Scalar("write an integer");
        public void WriteInt64(long value)    => throw Scalar("write an integer");
        public void WriteDouble(double value) => throw Scalar("write a double");
        public void WriteDateTime(DateTime value) => throw Scalar("write a datetime");
        public void WriteBytes(byte[] value)  => throw Scalar("write bytes");
        public void WriteNull()               => throw Scalar("write null");

        public IBindingSink Scope(string key) => new ColumnSink(this, key);

        public IBindingSink AppendItem() =>
            throw new InvalidOperationException(
                "A row sink has no array shape. Compose at the column level via Scope(name).");

        /// <summary>
        /// One column inside the row. Accepts either a single scalar (or null),
        /// or a homogeneous sequence of items of one supported type
        /// (Guid / int / long / double / DateTime / string). Items are packed
        /// to a single binary <see cref="EntityProperty"/> using the legacy
        /// <c>ByteArrayExtensions</c> conventions.
        /// </summary>
        private sealed class ColumnSink : IBindingSink
        {
            private enum ArrayMode { None, Guid, Int32, Int64, Double, DateTime, String }

            private readonly EntityPropertyRowBindingSink parent;
            private readonly string column;
            private ArrayMode mode;
            private List<Guid>     guidItems;
            private List<int>      intItems;
            private List<long>     longItems;
            private List<double>   doubleItems;
            private List<DateTime> dtItems;
            private List<string>   stringItems;

            public ColumnSink(EntityPropertyRowBindingSink parent, string column)
            {
                this.parent = parent;
                this.column = column;
            }

            private void GuardScalar()
            {
                if (mode != ArrayMode.None)
                    throw new InvalidOperationException(
                        $"Column '{column}' already has array items; cannot mix scalar and array writes.");
            }

            public void WriteString(string value)     { GuardScalar(); parent.Row[column] = new EntityProperty(value); }
            public void WriteGuid(Guid value)         { GuardScalar(); parent.Row[column] = new EntityProperty(value); }
            public void WriteBool(bool value)         { GuardScalar(); parent.Row[column] = new EntityProperty(value); }
            public void WriteInt32(int value)         { GuardScalar(); parent.Row[column] = new EntityProperty(value); }
            public void WriteInt64(long value)        { GuardScalar(); parent.Row[column] = new EntityProperty(value); }
            public void WriteDouble(double value)     { GuardScalar(); parent.Row[column] = new EntityProperty(value); }
            public void WriteDateTime(DateTime value) { GuardScalar(); parent.Row[column] = new EntityProperty(value); }
            public void WriteBytes(byte[] value)      { GuardScalar(); parent.Row[column] = new EntityProperty(value); }

            /// <summary>
            /// Encoded as a Binary <see cref="EntityProperty"/> with a null byte array,
            /// matching the null-detection branch in <see cref="EntityPropertyBindingSource"/>.
            /// </summary>
            public void WriteNull()                   { GuardScalar(); parent.Row[column] = new EntityProperty(default(byte[])); }

            public IBindingSink Scope(string key) =>
                throw new InvalidOperationException(
                    $"Nested composites (column-within-column) are not supported by the EDM row sink; got '{column}.{key}'.");

            /// <summary>
            /// Begin (or continue) collecting array items into this column. The item
            /// sink's first scalar write picks the element type for this column;
            /// subsequent items must use the same write method.
            /// </summary>
            public IBindingSink AppendItem()
            {
                if (mode == ArrayMode.None && parent.Row.ContainsKey(column))
                    throw new InvalidOperationException(
                        $"Column '{column}' already has a scalar value; cannot append array items.");
                return new ItemSink(this);
            }

            private void EnterMode(ArrayMode requested, string kind)
            {
                if (mode == ArrayMode.None)
                {
                    mode = requested;
                    switch (requested)
                    {
                        case ArrayMode.Guid:     guidItems   = new List<Guid>();     break;
                        case ArrayMode.Int32:    intItems    = new List<int>();      break;
                        case ArrayMode.Int64:    longItems   = new List<long>();     break;
                        case ArrayMode.Double:   doubleItems = new List<double>();   break;
                        case ArrayMode.DateTime: dtItems     = new List<DateTime>(); break;
                        case ArrayMode.String:   stringItems = new List<string>();   break;
                    }
                    return;
                }
                if (mode != requested)
                    throw new InvalidOperationException(
                        $"Column '{column}' already collecting {mode} items; cannot append '{kind}'. All items in an array column must share one element type.");
            }

            internal void AddGuid(Guid g)
            {
                EnterMode(ArrayMode.Guid, "guid");
                guidItems.Add(g);
                parent.Row[column] = new EntityProperty(guidItems.ToArray().ToByteArrayOfGuids());
            }

            internal void AddInt32(int v)
            {
                EnterMode(ArrayMode.Int32, "int32");
                intItems.Add(v);
                parent.Row[column] = new EntityProperty(intItems.ToByteArrayOfInts());
            }

            internal void AddInt64(long v)
            {
                EnterMode(ArrayMode.Int64, "int64");
                longItems.Add(v);
                parent.Row[column] = new EntityProperty(longItems.ToByteArrayOfLongs());
            }

            internal void AddDouble(double v)
            {
                EnterMode(ArrayMode.Double, "double");
                doubleItems.Add(v);
                parent.Row[column] = new EntityProperty(doubleItems.ToByteArrayOfDoubles());
            }

            internal void AddDateTime(DateTime v)
            {
                EnterMode(ArrayMode.DateTime, "datetime");
                dtItems.Add(v);
                parent.Row[column] = new EntityProperty(dtItems.ToByteArrayOfDateTimes());
            }

            internal void AddString(string v)
            {
                EnterMode(ArrayMode.String, "string");
                stringItems.Add(v);
                parent.Row[column] = new EntityProperty(stringItems.ToUTF8ByteArrayOfStrings());
            }

            internal string Column => column;

            /// <summary>
            /// Sink for a single appended item inside an array-valued column.
            /// Accepts exactly one scalar write, which is forwarded to the
            /// matching <c>AddX</c> on the owning column.
            /// </summary>
            private sealed class ItemSink : IBindingSink
            {
                private readonly ColumnSink column;
                private bool written;

                public ItemSink(ColumnSink column) { this.column = column; }

                private void GuardOnce()
                {
                    if (written)
                        throw new InvalidOperationException(
                            $"Array item in column '{column.Column}' has already been written; each AppendItem() accepts exactly one scalar.");
                    written = true;
                }

                private InvalidOperationException Unsupported(string what) =>
                    new($"Array column '{column.Column}' does not support '{what}' items. Supported item types: Guid, int, long, double, DateTime, string.");

                public void WriteGuid(Guid value)         { GuardOnce(); column.AddGuid(value); }
                public void WriteInt32(int value)         { GuardOnce(); column.AddInt32(value); }
                public void WriteInt64(long value)        { GuardOnce(); column.AddInt64(value); }
                public void WriteDouble(double value)     { GuardOnce(); column.AddDouble(value); }
                public void WriteDateTime(DateTime value) { GuardOnce(); column.AddDateTime(value); }
                public void WriteString(string value)     { GuardOnce(); column.AddString(value); }

                public void WriteBool(bool value)         => throw Unsupported("bool");
                public void WriteBytes(byte[] value)      => throw Unsupported("bytes");
                public void WriteNull()                   => throw Unsupported("null");

                public IBindingSink Scope(string key) =>
                    throw new InvalidOperationException(
                        $"Composite-shaped items in array column '{column.Column}' are not supported.");

                public IBindingSink AppendItem() =>
                    throw new InvalidOperationException(
                        $"Nested arrays in column '{column.Column}' are not supported.");
            }
        }
    }
}
