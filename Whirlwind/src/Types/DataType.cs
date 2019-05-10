﻿using System.Collections.Generic;
using System.Linq;

namespace Whirlwind.Types
{
    static class InterfaceRegistry
    {
        // store the types interface
        public static readonly Dictionary<DataType, InterfaceType> Interfaces
            = new Dictionary<DataType, InterfaceType>();
    }

    abstract class DataType
    {
        // store constancy
        public bool Constant = false;

        // check if another data type can be coerced to this type
        public virtual bool Coerce(DataType other)
        {
            if (other.Classify() == TypeClassifier.VOID || other.Classify() == TypeClassifier.GENERIC_PLACEHOLDER)
                return true;

            if (Classify() != TypeClassifier.REFERENCE && other.Classify() == TypeClassifier.REFERENCE)
                return Coerce(((ReferenceType)other).DataType);

            return _coerce(other);
        }

        // internal coerce method
        protected virtual bool _coerce(DataType other) => false;

        protected abstract bool _equals(DataType other);

        // returns the types interface
        public virtual InterfaceType GetInterface()
        {
            if (InterfaceRegistry.Interfaces.Keys.Any(x => x.Coerce(this)))
                return InterfaceRegistry.Interfaces.Where(x => x.Key.Coerce(this)).First().Value;

            InterfaceRegistry.Interfaces[this] = new InterfaceType();

            return InterfaceRegistry.Interfaces[this];
        }

        // check two data types for perfect equality
        public bool Equals(DataType other)
        {
            if (Constant == other.Constant)
                return _equals(other);

            return false;
        }

        public static bool operator ==(DataType a, DataType b)
            => a.Equals(b);

        public static bool operator !=(DataType a, DataType b)
            => !a.Equals(b);

        // get a given data type classifier as a string
        public abstract TypeClassifier Classify();

        // returns a constant copy of a given data type
        public abstract DataType ConstCopy();
    }

    class VoidType : DataType
    {
        public override bool Coerce(DataType other) => true;

        public override TypeClassifier Classify() => TypeClassifier.VOID;

        protected override bool _equals(DataType other) => false;

        public override DataType ConstCopy()
            => new VoidType() { Constant = true };
    }

    enum TypeClassifier
    {
        SIMPLE,
        ARRAY,
        LIST,
        DICT,
        POINTER,
        STRUCT,
        STRUCT_INSTANCE,
        TUPLE,
        INTERFACE,
        INTERFACE_INSTANCE,
        TYPE_CLASS,
        TYPE_CLASS_INSTANCE,
        FUNCTION,
        FUNCTION_GROUP,
        GENERIC,
        GENERIC_ALIAS,
        GENERIC_PLACEHOLDER,
        PACKAGE,
        VOID,
        REFERENCE,
        AGENT,
        SELF // self referential type
    }
}
