using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Tectonic
{
    /// <summary>
    /// A compile-time guaranteed unique, serialisable identifier.
    /// </summary>
    /// <remarks>
    /// TagValues are guaranteed unique by using the C# namespace checks - the
    /// value of a TagValue is derived from the name of the field to which it
    /// is assigned, and the type containing the field; thus two TagValues
    /// cannot be the same without two fields on the same .NET type having the
    /// same name, which is a compile-time error.
    /// </remarks>
    /// <example>
    /// public class ExampleClass
    /// {
    ///     public static readonly TagValue ExampleValue1 = TagValue.Create();
    ///     public static readonly TagValue ExampleValue2 = TagValue.Create();
    /// }
    /// </example>
    public struct TagValue
        : IEquatable<TagValue>
    {
        /// <summary>
        /// The type against which this TagValue is defined.
        /// </summary>
        private Type type;
        
        /// <summary>
        /// A text description to return from the ToString method; if not set,
        /// this defaults to the Value field.
        /// </summary>
        private string description;

        /// <summary>
        /// Create a new instance of TagValue against the specified type, with
        /// the specified value and optional description.
        /// </summary>
        /// <param name="type">
        /// The type against which this TagValue is defined.
        /// </param>
        /// <param name="description">
        /// A text description to return from the ToString method; if not set,
        /// this defaults to the Value field.
        /// </param>
        /// <param name="value">
        /// A string value that uniquely defines this TagValue within
        /// <paramref name="type"/>; if not set, this will default to the name
        /// of the calling method, or the name of the property/field in the
        /// case of property &amp; field initialisers.
        /// </param>
        public TagValue(Type type, string description = null, [CallerMemberName]string value = "")
            : this()
        {
            this.type = type;
            this.Value = value;
            this.description = description ?? value;
        }

        /// <summary>
        /// Create a new instance of TagValue against the declaring type of the
        /// calling method, with the specified value and optional description.
        /// </summary>
        /// <param name="description">
        /// A text description to return from the ToString method; if not set,
        /// this defaults to the Value field.
        /// </param>
        /// <param name="value">
        /// A string value that uniquely defines this TagValue within the
        /// declaring type of the calling method; if not set, this will default
        /// to the name of the calling method, or the name of the property or
        /// field in the case of property &amp; field initialisers.
        /// </param>
        public static TagValue Create(string description = null, [CallerMemberName]string value = "")
        {
            var frame = new StackFrame(1);

            return new TagValue(frame.GetMethod().DeclaringType, description, value);
        }

        /// <summary>
        /// The type against which this TagValue is defined.
        /// </summary>
        public Type Type
        {
            get
            {
                return this.type;
            }
        }

        /// <summary>
        /// A string value that uniquely defines this TagValue when combined
        /// with Type.
        /// </summary>
        public string Value
        {
            get;
            private set;
        }

        /// <summary>
        /// Returns a string representation of the value or description of
        /// this TagValue.
        /// </summary>
        /// <returns>
        /// A string representation of the value or description of this
        /// TagValue.
        /// </returns>
        public override string ToString()
        {
            return this.description;
        }

        /// <summary>
        /// Indicates whether the current TagValue is equal to another TagValue,
        /// comparing by value.
        /// </summary>
        /// <param name="other">
        /// A TagValue to compare with this TagValue.
        /// </param>
        /// <returns>
        /// true if the current instance is equal by value to
        /// <paramref name="other"/>; otherwise, false.
        /// </returns>
        public bool Equals(TagValue other)
        {
            return this.type == other.type
                && this.Value == other.Value;
        }
    }
}