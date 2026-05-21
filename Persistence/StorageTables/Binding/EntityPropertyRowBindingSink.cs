using System;
using System.Collections.Generic;
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
        public void WriteInt64(long value)    => throw Scalar("write an integer");
        public void WriteDouble(double value) => throw Scalar("write a double");
        public void WriteDateTime(DateTime value) => throw Scalar("write a datetime");
        public void WriteBytes(byte[] value)  => throw Scalar("write bytes");
        public void WriteNull()               => throw Scalar("write null");

        public IBindingSink Scope(string key) => new ColumnSink(this, key);

        public IBindingSink AppendItem() =>
            throw new InvalidOperationException(
                "A row sink has no array shape. Compose at the column level via Scope(name).");

        /// <summary>One column inside the row. Accepts a single scalar (or null).</summary>
        private sealed class ColumnSink : IBindingSink
        {
            private readonly EntityPropertyRowBindingSink parent;
            private readonly string column;

            public ColumnSink(EntityPropertyRowBindingSink parent, string column)
            {
                this.parent = parent;
                this.column = column;
            }

            public void WriteString(string value)     => parent.Row[column] = new EntityProperty(value);
            public void WriteGuid(Guid value)         => parent.Row[column] = new EntityProperty(value);
            public void WriteBool(bool value)         => parent.Row[column] = new EntityProperty(value);
            public void WriteInt64(long value)        => parent.Row[column] = new EntityProperty(value);
            public void WriteDouble(double value)     => parent.Row[column] = new EntityProperty(value);
            public void WriteDateTime(DateTime value) => parent.Row[column] = new EntityProperty(value);
            public void WriteBytes(byte[] value)      => parent.Row[column] = new EntityProperty(value);

            /// <summary>
            /// Encoded as a Binary <see cref="EntityProperty"/> with a null byte array,
            /// matching the null-detection branch in <see cref="EntityPropertyBindingSource"/>.
            /// </summary>
            public void WriteNull()                   => parent.Row[column] = new EntityProperty(default(byte[]));

            public IBindingSink Scope(string key) =>
                throw new InvalidOperationException(
                    $"Nested composites (column-within-column) are not supported by the EDM row sink; got '{column}.{key}'.");

            public IBindingSink AppendItem() =>
                throw new InvalidOperationException(
                    $"Array-valued columns are not yet supported by the EDM row sink (column '{column}'). " +
                    "Defer collection writes until packed-byte encoding ships.");
        }
    }
}
