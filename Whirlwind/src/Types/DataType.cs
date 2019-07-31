﻿using System.Collections.Generic;

namespace Whirlwind.Types
{
    static class InterfaceRegistry
    {
        // store the types interface
        public static readonly Dictionary<DataType, InterfaceType> Interfaces
            = new Dictionary<DataType, InterfaceType>();

        public static bool GetTypeInterface(DataType dt, out InterfaceType typeInterf)
        {
            if (dt is StructType st)
            {
                foreach (var item in Interfaces)
                {
                    if (item.Key is StructType ist && st.Coerce(ist))
                    {
                        typeInterf = item.Value;
                        return true;
                    }
                }
            }
            else if (dt is CustomInstance cnt)
            {
                foreach (var item in Interfaces)
                {
                    if (item.Key is CustomType && item.Key.Equals(cnt.Parent))
                    {
                        typeInterf = item.Value;
                        return true;
                    }
                }
            }
            else
            {
                foreach (var item in Interfaces)
                {
                    if (item.Key.Equals(dt))
                    {
                        typeInterf = item.Value;
                        return true;
                    }
                }
            }

            typeInterf = null;
            return false;
        }
    }

    abstract class DataType
    {
        // store constancy
        public bool Constant = false;

        // check if another data type can be coerced to this type
        public virtual bool Coerce(DataType other)
        {
            if (other is IncompleteType)
                return true;

            if (!Constant && other.Constant)
                return false;

            // super form should never be used as a literal type
            if (other is InterfaceType it && it.SuperForm)
                return false;

            if (other.Classify() == TypeClassifier.VOID || other.Classify() == TypeClassifier.GENERIC_PLACEHOLDER)
                return true;

            if (Classify() != TypeClassifier.REFERENCE && other.Classify() == TypeClassifier.REFERENCE)
                return Coerce(((ReferenceType)other).DataType);

            if (other is GenericAlias gp)
                return Coerce(gp.ReplacementType);

            return _coerce(other);
        }

        // internal coerce method
        protected virtual bool _coerce(DataType other) => false;

        protected abstract bool _equals(DataType other);

        // returns the types interface
        public virtual InterfaceType GetInterface()
        {
            if (InterfaceRegistry.GetTypeInterface(this, out InterfaceType ift))
                return ift;

            InterfaceRegistry.Interfaces[this] = new InterfaceType();

            return InterfaceRegistry.Interfaces[this];
        }

        // check two data types for perfect equality
        public bool Equals(DataType other)
        {
            if (other == null)
                return false;

            if (other is GenericAlias gp)
                return Equals(gp.ReplacementType);

            if (Constant == other.Constant)
                return _equals(other);

            return false;
        }

        public static bool operator ==(DataType a, DataType b)
            => a?.Equals(b) ?? (object)b == null;

        public static bool operator !=(DataType a, DataType b)
            => (!a?.Equals(b)) ?? (object)b != null;

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

    class IncompleteType : DataType
    {
        public override bool Coerce(DataType other) => true;

        public override TypeClassifier Classify() => TypeClassifier.INCOMPLETE;

        protected override bool _equals(DataType other) => false;

        public override DataType ConstCopy()
            => new IncompleteType() { Constant = true };
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
        INCOMPLETE,
        SELF // self referential type
    }
}
