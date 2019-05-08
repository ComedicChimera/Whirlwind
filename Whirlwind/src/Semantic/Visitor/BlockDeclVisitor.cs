﻿using Whirlwind.Parser;
using Whirlwind.Types;

using System.Collections.Generic;
using System.Linq;

namespace Whirlwind.Semantic.Visitor
{
    partial class Visitor
    {
        private void _visitBlockDecl(ASTNode node, List<Modifier> modifiers)
        {
            ASTNode root = (ASTNode)node.Content[0];

            var genericVars = new List<GenericVariable>();
            var namePosition = new TextPosition();
            if (root.Content.Count > 2 && root.Content[2].Name == "generic_tag")
            {
                genericVars = _primeGeneric((ASTNode)root.Content[1]);
                namePosition = root.Content[1].Position;
            }

            switch (root.Name)
            {
                case "type_class_decl":
                    _visitTypeClass(root, modifiers);
                    break;
                case "func_decl":
                    _visitFunction(root, modifiers);
                    break;
                case "interface_decl":
                    _visitInterface(root, modifiers);
                    break;
                case "interface_bind":
                    _visitInterfaceBind(root);
                    break;
                case "struct_decl":
                    _visitStruct(root, modifiers);
                    break;
                case "decor_decl":
                    _visitDecorator(root, modifiers);
                    break;
                case "variant_decl":
                    _visitVariant(root);
                    break;
            }

            // FIX POSITION DATA
            if (genericVars.Count > 0)
                _makeGeneric(root, genericVars, modifiers, namePosition);
        }

        private void _visitDecorator(ASTNode node, List<Modifier> modifiers)
        {
            _visitFunction((ASTNode)node.Content[1], modifiers);

            FunctionType fnType = (FunctionType)((TreeNode)_nodes.Last()).Nodes[0].Type;

            _nodes.Add(new BlockNode("Decorator"));
         
            foreach (var item in ((ASTNode)node.Content[0]).Content)
            {
                if (item.Name == "expr")
                {
                    _visitExpr((ASTNode)item);

                    if (_nodes.Last().Type.Classify() == TypeClassifier.FUNCTION)
                    {
                        FunctionType decorType = (FunctionType)_nodes.Last().Type;

                        if (decorType.MatchArguments(new ArgumentList(new List<DataType>() { fnType })))
                        {
                            // check for void decorators
                            if (_isVoid(decorType.ReturnType))
                                throw new SemanticException("A decorator must return a value", item.Position);

                            // allows decorator to override function return type ;)
                            if (!fnType.Coerce(decorType.ReturnType))
                            {
                                _table.Lookup(((TokenNode)((ASTNode)node.Content[1]).Content[1]).Tok.Value, out Symbol sym);

                                sym.DataType = decorType.ReturnType;
                            }

                            MergeBack();
                        }
                        else
                            throw new SemanticException("This decorator is not valid for the given function", item.Position);
                    }
                    else
                        throw new SemanticException("Unable to use non-function as a decorator", item.Position);
                }
            }

            PushToBlock();
        }
    }
}
