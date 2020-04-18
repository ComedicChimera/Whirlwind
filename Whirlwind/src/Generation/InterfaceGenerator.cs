﻿using System;
using System.Collections.Generic;
using System.Linq;

using LLVMSharp;

using Whirlwind.Semantic;
using Whirlwind.Types;

namespace Whirlwind.Generation
{
    partial class Generator
    {
        // TODO: external interfaces
        private void _generateInterf(BlockNode node)
        {
            var idNode = (IdentifierNode)node.Nodes[0];
            var interfType = (InterfaceType)idNode.Type;

            string name = idNode.IdName;

            _table.Lookup(idNode.IdName, out Symbol interfSymbol);
            bool exported = interfSymbol.Modifiers.Contains(Modifier.EXPORTED);

            string llvmPrefix = exported ? _randPrefix : "";
            name += _genericSuffix;

            var interfStruct = LLVM.StructCreateNamed(_ctx, llvmPrefix + name);
            _globalStructs[name] = interfStruct; // forward declare interface struct

            _thisPtrType = LLVM.PointerType(interfStruct, 0);
            var methods = _generateInterfBody(node, interfType, _thisPtrType, name, false, exported);

            var vtableStruct = LLVM.StructCreateNamed(_ctx, llvmPrefix + name + ".__vtable");
            vtableStruct.StructSetBody(methods.ToArray(), false);

            _globalStructs[name + ".__vtable"] = vtableStruct;
      
            interfStruct.StructSetBody(new[]
            {
                _i8PtrType,
                LLVM.PointerType(vtableStruct, 0),
                LLVM.Int16Type(),
                LLVM.Int32Type()
            }, false);

            _thisPtrType = LLVM.PointerType(_interfBoxType, 0);
        }

        private List<LLVMTypeRef> _generateInterfBody(BlockNode node, InterfaceType interfType, LLVMTypeRef desiredThis, 
            string name, bool typeInterf, bool exported)
        {
            var methods = new List<LLVMTypeRef>();

            int methodNdx = 0;
            foreach (var method in interfType.Methods)
            {
                if (method.Key.DataType is FunctionType fnType)
                {
                    string llvmName;
                    if (_getOperatorOverloadName(method.Key.Name, out string overloadName))
                        llvmName = name + ".operator." + overloadName;
                    else
                        llvmName = name + ".interf." + method.Key.Name;

                    if (interfType.Methods.Where(x => x.Key.Name == method.Key.Name).Count() > 1)
                        llvmName += "." + string.Join(",", fnType.Parameters.Select(x => x.DataType.LLVMName()));

                    methods.Add(_convertType(fnType));

                    if (method.Value != MethodStatus.ABSTRACT)
                    {
                        var llvmMethod = _generateFunctionPrototype(llvmName, fnType, exported);

                        if (typeInterf)
                            _appendFunctionBlock(llvmMethod, fnType.ReturnType, _getTIMethodBodyBuilder((BlockNode)node.Block[methodNdx], desiredThis));
                        else
                            _appendFunctionBlock(llvmMethod, fnType.ReturnType, (BlockNode)node.Block[methodNdx]);

                        _addGlobalDecl(llvmName, llvmMethod);
                    }
                }
                // generic case
                else
                {
                    var generateList = _generateGenericMethod(name, method.Key);

                    if (method.Value != MethodStatus.ABSTRACT)
                    {
                        foreach (var generate in generateList)
                        {
                            var llvmMethod = _generateFunctionPrototype(generate.Item1, generate.Item2, exported);

                            if (typeInterf)
                                _appendFunctionBlock(llvmMethod, generate.Item2.ReturnType, _getTIMethodBodyBuilder(generate.Item3, desiredThis));
                            else
                                _appendFunctionBlock(llvmMethod, generate.Item2.ReturnType, generate.Item3);

                            _addGlobalDecl(generate.Item1, llvmMethod);
                        }
                    }
                }
            }

            return methods;
        }

        Dictionary<string, string> _opNameTable = new Dictionary<string, string>
        {
            { "+", "Add" },
            { "-", "Sub" },
            { "*", "Mul" },
            { "/", "Div" },
            { "~/", "Floordiv" },
            { "~^", "Pow" },
            { "%", "Mod" },
            { ">", "Gt" },
            { "<", "Lt" },
            { ">=", "GtEq" },
            { "<=", "LtEq" },
            { "==", "Eq" },
            { "!=", "Neq" },
            { "!", "Not" },
            { "AND", "And" },
            { "OR", "Or" },
            { "&", "BAnd" },
            { "|", "BOr" },
            { "^", "BXor" },
            { "~", "Complement" },
            { ">>", "RShift" },
            { "<<", "LShift" },
            { "~*", "Compose" },
            { ">>=", "Bind" },
            { "[]", "Subscript" },
            { "[:]", "Slice" },
            { "..", "Range" },
            { ":>", "ExtractInto" }
        };

        private bool _getOperatorOverloadName(string baseName, out string outputName)
        {
            string trimmedName = baseName.Trim('_');

            if (_opNameTable.ContainsKey(trimmedName))
            {
                outputName = _opNameTable[trimmedName];
                return true;
            }

            outputName = "";
            return false;
        }

        private FnBodyBuilder _getTIMethodBodyBuilder(BlockNode block, LLVMTypeRef thisPtrType)
            => delegate (LLVMValueRef method)
            {
                var thisVref = _getNamedValue("this").Vref;

                var thisElemPtr = LLVM.BuildStructGEP(_builder, thisVref, 0, "this_elem_ptr_tmp");
                var i8ThisPtr = LLVM.BuildLoad(_builder, thisElemPtr, "this_i8ptr_tmp");

                _setVar("this", LLVM.BuildBitCast(_builder, i8ThisPtr, thisPtrType, "__this"));

                return _generateBlock(block.Block);
            };

        // TODO: exported interface bindings
        private void _generateInterfBind(BlockNode node)
        {
            var it = (InterfaceType)node.Nodes[0].Type;
            var dtBindName = _getLookupName(node.Nodes[1].Type);
            var bindThisPtrType = _getBindThisPtrType(node.Nodes[1].Type);

            _generateInterfBody(node, it, bindThisPtrType, dtBindName, true, false);
        }

        private void _generateGenericBind(BlockNode node)
        {
            var gt = (GenericType)node.Nodes[0].Type;
            
            foreach (var item in gt.Generates)
            {
                var dtBindName = _getLookupName(item.Type);
                var bindThisPtrType = _getBindThisPtrType(item.Type);

                _generateInterfBody(item.Block, (InterfaceType)item.Type, bindThisPtrType, dtBindName, true, false);
            }
        }

        private LLVMTypeRef _getBindThisPtrType(DataType dt)
        {
            var baseType = _convertType(dt);

            return LLVM.PointerType(baseType, 0);
        }

        private LLVMValueRef _boxToInterf(LLVMValueRef vref, DataType dt)
        {
            var boxed = LLVM.BuildAlloca(_builder, _interfBoxType, "interf_box_tmp");        

            LLVMValueRef thisPtr;
            if (dt.IsThisPtr || _isReferenceType(dt))
                thisPtr = LLVM.BuildBitCast(_builder, vref, _i8PtrType, "this_i8_ptr_tmp");
            else
            {
                thisPtr = LLVM.BuildAlloca(_builder, _convertType(dt), "this_ptr_tmp");
                LLVM.BuildStore(_builder, vref, thisPtr);

                thisPtr = LLVM.BuildBitCast(_builder, thisPtr, _i8PtrType, "this_i8_ptr_tmp");
            }

            var thisElemPtr = LLVM.BuildStructGEP(_builder, boxed, 0, "this_elem_ptr_tmp");
            LLVM.BuildStore(_builder, thisPtr, thisElemPtr);

            // because this is a fake box (not actually intended to be used a real interface, c val and size are not necessary)

            return boxed;
        }


        private LLVMValueRef _createVtable(InterfaceType child, InterfaceType parent, string genericSuffix = "")
        {
            var methods = new List<LLVMValueRef>();

            void addMethod(InterfaceType it, string methodName)
            {
                it.GetFunction(methodName, out Symbol sym);
                string methodPrefix = _getLookupName(it.Name) + genericSuffix + ".interf.";

                switch (sym.DataType.Classify())
                {
                    case TypeClassifier.FUNCTION:
                        methods.Add(_loadGlobalValue(methodPrefix + sym.Name));
                        break;
                    case TypeClassifier.FUNCTION_GROUP:
                        methods.Concat(
                            ((FunctionGroup)sym.DataType).Functions
                            .Select(x =>
                                _loadGlobalValue(methodPrefix + sym.Name + "." +
                                string.Join(",", x.Parameters.Select(y => y.DataType.LLVMName()))
                            ))
                        );
                        break;
                    case TypeClassifier.GENERIC:
                        methods.Concat(
                            ((GenericType)sym.DataType).Generates
                            .Select(x =>
                                _loadGlobalValue(methodPrefix + sym.Name + ".variant." +
                                string.Join(",", x.GenericAliases.Select(y => y.Value.LLVMName()))
                            ))
                        );
                        break;
                    case TypeClassifier.GENERIC_GROUP:
                        // select every generate of every generic function in the generic group
                        methods.Concat(
                            ((GenericGroup)sym.DataType).GenericFunctions
                            .SelectMany(x => x.Generates.Select(y =>
                                _loadGlobalValue(methodPrefix + sym.Name + ".variant." +
                                string.Join(",", y.GenericAliases.Select(z => z.Value.LLVMName()))
                                )
                            ))
                        );
                        break;
                }
            }

            foreach (var method in parent.Methods)
            {
                if (child.Methods.Single(x => x.Key.Equals(method.Key)).Value == MethodStatus.VIRTUAL)
                    addMethod(parent, method.Key.Name);
                else
                    addMethod(child, method.Key.Name);
            }

            var vtableType = _getGlobalStruct(_getLookupName(parent.Name) + genericSuffix + ".__vtable", false);
            var vtablePtr = LLVM.BuildAlloca(_builder, vtableType, "vtable_ptr_tmp");

            LLVM.BuildStore(_builder, LLVM.ConstNamedStruct(vtableType, methods.ToArray()), vtablePtr);

            return vtablePtr;
        }

        private LLVMValueRef _generateVtableGet(LLVMValueRef baseInterf, int vtableNdx)
        {
            var vtableElemPtr = LLVM.BuildStructGEP(_builder, baseInterf, 1, "vtable_elem_ptr_tmp");
            var vtable = LLVM.BuildLoad(_builder, vtableElemPtr, "vtable_tmp");

            var gepRes = LLVM.BuildStructGEP(_builder, vtable,
                (uint)vtableNdx, "vtable_gep_tmp");

            var methodPtr = LLVM.BuildLoad(_builder, gepRes, "method_ptr_tmp");

            return _boxFunction(methodPtr, baseInterf);
        }

        private int _getVTableNdx(InterfaceType it, string name)
        {
            int vtableNdx = 0;
            foreach (var method in it.Methods)
            {
                if (!it.Implements.Any(x => x.GetFunction(name, out Symbol _)))
                    continue;

                if (method.Key.Name == name)
                    break;

                if (method.Key.DataType is GenericType gt)
                    vtableNdx += gt.Generates.Count;
                else
                    vtableNdx++;
            }

            return vtableNdx;
        }

        // argument-less form
        private LLVMValueRef _callMethod(LLVMValueRef root, DataType dt, string methodName, DataType returnType)
        {
            LLVMValueRef thisPtr;
            LLVMValueRef methodRef;

            if (dt is InterfaceType it)
            {
                var vtableElemPtr = LLVM.BuildStructGEP(_builder, root, 1, "vtable_elem_ptr_tmp");
                var vtable = LLVM.BuildLoad(_builder, vtableElemPtr, "vtable_tmp");

                uint vtableNdx = (uint)_getVTableNdx(it, methodName);

                var gepRes = LLVM.BuildStructGEP(_builder, vtable,
                    vtableNdx, "vtable_gep_tmp");

                methodRef = LLVM.BuildLoad(_builder, gepRes, "method_ptr_tmp");
                thisPtr = root;
            }
            else
            {
                // assumes not overloads and no generics (handled by other parts of the code)
                string rootName = _getLookupName(dt);

                var interfType = dt.GetInterface();
                var method = interfType.Methods.Single(x => x.Key.Name == methodName);

                if (method.Value == MethodStatus.VIRTUAL)
                {
                    var rootInterf = interfType.Implements.First(x => x.Methods.Contains(method));

                    rootName = _getLookupName(rootInterf);
                    thisPtr = _cast(root, dt, rootInterf);
                }
                else
                    thisPtr = _boxToInterf(root, dt); // standard up box (with no real vtable)

                methodRef = _globalScope[rootName + ".interf." + methodName].Vref;
            }

            LLVMValueRef[] argArray;
            bool returnCallResult = true;

            if (_isReferenceType(returnType))
            {
                argArray = new LLVMValueRef[2];
                argArray[1] = LLVM.BuildAlloca(_builder, _convertType(returnType), "rtptr_tmp");
                returnCallResult = false;
            }
            else
                argArray = new LLVMValueRef[1];

            argArray[0] = thisPtr;

            if (returnCallResult)
                return LLVM.BuildCall(_builder, methodRef, argArray, "method_call_tmp"); 
            else
            {
                LLVM.BuildCall(_builder, methodRef, argArray, "");
                return argArray[1];
            }

        }

        private InterfaceType _getInterfaceOf(DataType dt)
            => dt is InterfaceType it ? it : dt.GetInterface();

        private DataType _getNoArgMethodRtType(InterfaceType it, string methodName)
        {
            it.GetFunction(methodName, out Symbol symbol);

            if (symbol.DataType is FunctionType ft)
                return ft.ReturnType;
            else if (symbol.DataType is FunctionGroup fg)
            {
                fg.GetFunction(new ArgumentList(), out FunctionType fgft);
                return fgft.ReturnType;
            }
            else
                throw new NotImplementedException();           
        }
    }
}