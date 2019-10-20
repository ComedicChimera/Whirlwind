﻿using System.Collections.Generic;
using System.Linq;

using Whirlwind.Syntax;
using Whirlwind.Types;

namespace Whirlwind.Semantic.Visitor
{
    partial class Visitor
    {
        private void _visitStruct(ASTNode node, List<Modifier> modifiers)
        {
            _nodes.Add(new BlockNode("Struct"));
            TokenNode name = (TokenNode)node.Content[1];

            var structType = new StructType(_namePrefix + name.Tok.Value);

            // descent for self referential >:(
            _table.AddScope();
            _table.DescendScope();

            // declare self referential type (ok early, b/c reference)
            if (_isGenericSelfContext)
            {
                // if there's context, the symbol exists
                _table.Lookup("$GENERIC_SELF", out Symbol genSelf);
                _table.AddSymbol(new Symbol(name.Tok.Value, genSelf.DataType));
            }
            else
                _table.AddSymbol(new Symbol(name.Tok.Value, new SelfType(_namePrefix + name.Tok.Value, structType)));

            // since struct members are all variables
            _selfNeedsPointer = true;

            // needs a default constructor
            bool needsDefaultConstr = true;

            foreach (var subNode in ((ASTNode)node.Content[node.Content.Count - 2]).Content)
            {
                if (subNode.Name == "struct_var")
                {
                    var processingStack = new List<TokenNode>();
                    DataType type = new NoneType();
                    var memberModifiers = new List<Modifier>();

                    foreach (var item in ((ASTNode)subNode).Content)
                    {
                        if (item is TokenNode tkNode)
                        {
                            switch (tkNode.Tok.Type)
                            {
                                case "IDENTIFIER":
                                    processingStack.Add(tkNode);
                                    break;
                                case "VOL":
                                    memberModifiers.Add(Modifier.VOLATILE);
                                    break;
                                case "OWN":
                                    memberModifiers.Add(Modifier.OWNED);
                                    break;
                            }
                        }                          
                        else if (item.Name == "types")
                        {
                            type = _generateType((ASTNode)item);

                            if (memberModifiers.Contains(Modifier.OWNED) && !(type is PointerType pt && pt.IsDynamicPointer))
                                throw new SemanticException("Own modifier must be used on a dynamic pointer",
                                    ((ASTNode)subNode).Content[0].Position);

                            foreach (var member in processingStack)
                            {
                                if (!structType.AddMember(new Symbol(member.Tok.Value, type, memberModifiers)))
                                    throw new SemanticException("Structs cannot contain duplicate members", member.Position);
                            }
                        }
                        else if (item.Name == "initializer")
                        {
                            _nodes.Add(new ExprNode("MemberInitializer", type));

                            _nodes.Add(new IncompleteNode((ASTNode)((ASTNode)item).Content[1]));
                            MergeBack();

                            foreach (var member in processingStack)
                            {
                                _nodes.Add(new IdentifierNode(member.Tok.Value, type));
                            }

                            MergeBack(processingStack.Count);

                            // merge to block
                            MergeToBlock();
                        }
                    }
                }
                else if (subNode.Name == "constructor_decl")
                {
                    ASTNode decl = (ASTNode)subNode;

                    var fnType = _visitConstructor(decl, new List<Modifier>());

                    if (!structType.AddConstructor(fnType))
                        throw new SemanticException("Unable to declare duplicate constructors", decl.Content[2].Position);

                    needsDefaultConstr = false;

                    MergeToBlock();
                }
            }

            if (needsDefaultConstr)
                structType.AddConstructor(new FunctionType(new List<Parameter>(), new NoneType(), false));

            _nodes.Add(new IdentifierNode(name.Tok.Value, structType));
            MergeBack();

            // update self type if necessary
            if (_table.Lookup(name.Tok.Value, out Symbol selfSym) && selfSym.DataType is SelfType)
                ((SelfType)selfSym.DataType).Initialized = true;

            _table.AscendScope();

            if (!_table.AddSymbol(new Symbol(name.Tok.Value, structType, modifiers)))
                throw new SemanticException($"Unable to redeclare symbol: `{name.Tok.Value}`", name.Position);

            // undo self needs pointer
            _selfNeedsPointer = false;
        }

        private FunctionType _visitConstructor(ASTNode decl, List<Modifier> modifiers)
        {
            List<Parameter> args = new List<Parameter>();

            _nodes.Add(new BlockNode("Constructor"));

            foreach (var item in decl.Content)
            {
                if (item.Name == "args_decl_list")
                    args = _generateArgsDecl((ASTNode)item);
                else if (item.Name == "func_body")
                {
                    _nodes.Add(new IncompleteNode((ASTNode)item));
                    MergeToBlock();
                }
            }

            FunctionType ft = new FunctionType(args, new NoneType(), false)
            {
                Constant = true
            };

            _nodes.Add(new ValueNode("ConstructorSignature", ft));
            MergeBack();

            return ft;
        }
    }
}
