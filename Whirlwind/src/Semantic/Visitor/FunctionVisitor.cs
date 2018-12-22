﻿using Whirlwind.Types;
using Whirlwind.Parser;

using System.Collections.Generic;
using System.Linq;
using System;

namespace Whirlwind.Semantic.Visitor
{
    partial class Visitor
    {
        TextPosition _dominantPosition;

        private void _visitFunction(ASTNode function, List<Modifier> modifiers)
        {
            bool isAsync = false;
            string name = "";
            var arguments = new List<Parameter>();
            IDataType dataType = new SimpleType();

            TextPosition namePosition = new TextPosition(),
                rtPosition = new TextPosition();

            foreach (var item in function.Content)
            {
                switch (item.Name)
                {
                    case "TOKEN":
                        {
                            Token tok = ((TokenNode)item).Tok;

                            switch (tok.Type)
                            {
                                case "ASYNC":
                                    isAsync = true;
                                    break;
                                case "IDENTIFIER":
                                    name = tok.Value;
                                    namePosition = item.Position;
                                    break;
                                case ";":
                                    {
                                        _createFunction(arguments, dataType, name, namePosition, isAsync, modifiers);
                                        // try to complete body
                                    }
                                    break;
                            }
                        }
                        break;
                    case "args_decl_list":
                        arguments = _generateArgsDecl((ASTNode)item);
                        break;
                    case "types":
                        dataType = _generateType((ASTNode)item);
                        rtPosition = item.Position;
                        break;
                    case "func_body":
                        {
                            _createFunction(arguments, dataType, name, namePosition, isAsync, modifiers);

                            _nodes.Add(new IncompleteNode((ASTNode)item));
                            MergeToBlock();
                        }
                        break;
                }
            }
        }

        private void _createFunction(
            List<Parameter> parameters, IDataType dataType, string name, TextPosition namePosition, bool isAsync, List<Modifier> modifiers
            )
        {
            _nodes.Add(new BlockNode(isAsync ? "AsyncFunction" : "Function"));

            var fnType = new FunctionType(parameters, dataType, isAsync);

            _nodes.Add(new IdentifierNode(name, fnType, true));

            MergeBack();

            modifiers.Add(Modifier.CONSTANT);

            if (!_table.AddSymbol(new Symbol(name, fnType, modifiers)))
                throw new SemanticException($"Unable to redeclare symbol by name {name}", namePosition);
        }

        private void _visitFunctionBody(ASTNode body, FunctionType type)
        {
            _table.AddScope();
            _table.DescendScope();

            _declareArgs(type.Parameters);

            if (!type.ReturnType.Coerce(_visitFuncBody(body)))
                throw new SemanticException("Return type of signature does not match return type of body", _dominantPosition);

            _table.AscendScope();
        }

        private void _declareArgs(List<Parameter> args)
        {
            // no symbol checking necessary since params have already been full filtered and override scope
            foreach (var arg in args)
            {
                if (arg.Indefinite)
                {
                    if (_isVoid(arg.DataType))
                    {
                        // add va_args param
                    }
                    else
                    {
                        _table.AddSymbol(new Symbol(
                            arg.Name,
                            new ListType(arg.DataType),
                            arg.Constant ? new List<Modifier>() { Modifier.CONSTANT } : new List<Modifier>()
                            ));
                    }
                }
                else
                {
                    _table.AddSymbol(new Symbol(
                        arg.Name,
                        arg.DataType,
                        arg.Constant ? new List<Modifier>() { Modifier.CONSTANT } : new List<Modifier>()
                    ));
                }
            }
        }

        private IDataType _visitFuncBody(ASTNode node)
        {
            IDataType rtType = new SimpleType();

            foreach (var item in node.Content)
            {
                switch (item.Name)
                {
                    case "func_guard":
                        _nodes.Add(new ExprNode("FunctionGuard", new SimpleType()));
                        _visitExpr((ASTNode)((ASTNode)item).Content[2]);
                        MergeBack(2);
                        break;
                    case "main":
                        _visitBlock((ASTNode)item, new StatementContext(true, false, false));
                        rtType = _extractReturnType((ASTNode)item);
                        break;
                    case "expr":
                        _nodes.Add(new StatementNode("ExpressionReturn"));
                        _visitExpr((ASTNode)item);

                        rtType = _nodes.Last().Type;
                        MergeBack();

                        MergeToBlock();
                        break;
                }
            }

            return rtType;
        }

        private IDataType _extractReturnType(ASTNode ast)
        {
            var positions = new List<TextPosition>();
            _getReturnPositions(ast, ref positions);

            int pos = 0;
            var returnData = _extractReturnType((BlockNode)_nodes.Last(), positions, ref pos);

            if (!returnData.Item1)
                throw new SemanticException("Inconsistent return type", positions.First());

            return returnData.Item2;
        }

        private Tuple<bool, IDataType> _extractReturnType(BlockNode block, List<TextPosition> positions, ref int pos)
        {
            IDataType rtType = new SimpleType();
            bool returnsValue = false, setReturn = false, terminatingReturn = false;

            foreach (var node in block.Block)
            {
                if (node.Name == "Return" || node.Name == "Yield")
                {
                    var typeList = new List<IDataType>();

                    foreach (var expr in ((StatementNode)node).Nodes)
                    {
                        typeList.Add(expr.Type);
                    }

                    if (typeList.Count > 0)
                    {
                        if (!returnsValue && setReturn)
                            throw new SemanticException("Inconsistent return types", positions[pos]);

                        IDataType dt = typeList.Count == 1 ? typeList[0] : new TupleType(typeList);

                        if (!returnsValue)
                            rtType = dt;
                        else if (!rtType.Coerce(dt))
                        {
                            if (dt.Coerce(rtType))
                            {
                                rtType = dt;

                                _dominantPosition = positions[pos];
                            }
                            else
                                throw new SemanticException("Inconsistent return types", positions[pos]);
                        }

                        returnsValue = true;
                    }
                    else if (returnsValue)
                        throw new SemanticException("Inconsistent return types", positions[pos]);

                    if (!setReturn)
                        setReturn = true;

                    if (!terminatingReturn)
                        terminatingReturn = true;

                    pos++;
                }
                else if (node is BlockNode)
                {
                    int savedPos = pos;
                    var blockReturn = _extractReturnType((BlockNode)node, positions, ref pos);
                    savedPos = (pos - savedPos) > 0 ? (pos - 1) : pos;

                    if (!blockReturn.Item1)
                        throw new SemanticException("Inconsistent return type", positions[savedPos]);

                    if (_isVoid(blockReturn.Item2))
                    {
                        if (returnsValue && setReturn)
                            throw new SemanticException("Inconsistent return type", positions[savedPos]);
                        else
                            continue;
                    }

                    if (!rtType.Coerce(blockReturn.Item2))
                    {
                        if (blockReturn.Item2.Coerce(rtType))
                            rtType = blockReturn.Item2;
                        else
                            throw new SemanticException("Inconsistent return type", positions[savedPos]);
                    }

                    if (!terminatingReturn && blockReturn.Item1)
                        terminatingReturn = true;

                    if (!setReturn)
                        setReturn = true;

                    if (!returnsValue)
                        returnsValue = true;
                        
                }
            }

            return new Tuple<bool, IDataType>(!returnsValue || terminatingReturn, rtType);
        }

        private void _getReturnPositions(ASTNode node, ref List<TextPosition> positions)
        {
            foreach (var item in node.Content)
            {
                if (item.Name == "return_stmt" || item.Name == "yield_stmt")
                    positions.Add(item.Position);
                else if (item.Name != "TOKEN")
                    _getReturnPositions((ASTNode)item, ref positions);
            }
        }

        public List<Parameter> _generateArgsDecl(ASTNode node)
        {
            var argsDeclList = new List<Parameter>(); 
            foreach (var subNode in node.Content)
            {
                if (subNode.Name == "decl_arg")
                {
                    bool optional = false, 
                        constant = false,
                        hasExtension = false;
                    var identifiers = new List<string>();
                    IDataType paramType = new SimpleType();

                    foreach (var argPart in ((ASTNode)subNode).Content)
                    {
                        switch (argPart.Name)
                        {
                            case "TOKEN":
                                if (((TokenNode)argPart).Tok.Type == "IDENTIFIER")
                                    identifiers.Add(((TokenNode)argPart).Tok.Value);
                                else if (((TokenNode)argPart).Tok.Type == "@")
                                    constant = true;
                                break;
                            case "extension":
                                paramType = _generateType((ASTNode)((ASTNode)argPart).Content[1]);
                                hasExtension = true;
                                break;
                            case "initializer":
                                _visitExpr((ASTNode)((ASTNode)argPart).Content[1]);

                                if (!hasExtension)
                                    paramType = _nodes.Last().Type;

                                optional = true;
                                break;
                        }
                    }

                    if (!optional && !hasExtension)
                        throw new SemanticException("Unable to create argument with no type", subNode.Position);

                    if (hasExtension && optional && !paramType.Coerce(_nodes.Last().Type))
                        throw new SemanticException("Initializer type incompatable with type extension", subNode.Position);

                    if (optional)
                    {
                        foreach (var identifier in identifiers)
                            argsDeclList.Add(new Parameter(identifier, paramType, false, constant, _nodes.Last()));

                        _nodes.RemoveAt(_nodes.Count - 1); // remove argument from node stack
                    }
                    else
                    {
                        foreach (var identifier in identifiers)
                            argsDeclList.Add(new Parameter(identifier, paramType, false, constant));
                    }
                }
                else if (subNode.Name == "ending_arg")
                {
                    bool constant = false;
                    string name = "";
                    IDataType dt = new SimpleType();

                    foreach (var item in ((ASTNode)subNode).Content)
                    {
                        if (item.Name == "extension")
                            dt = _generateType((ASTNode)((ASTNode)item).Content[1]);
                        else if (item.Name == "TOKEN" && ((TokenNode)item).Tok.Type == "IDENTIFIER")
                            name = ((TokenNode)item).Tok.Value;
                    }

                    argsDeclList.Add(new Parameter(name, dt, true, constant));
                }
            }

            if (argsDeclList.GroupBy(x => x.Name).Any(x => x.Count() > 1))
                throw new SemanticException("Function cannot be declared with duplicate arguments", node.Position);

            return argsDeclList;
        }

        // generate a parameter list from a function call and generate the corresponding tree
        private List<IDataType> _generateArgsList(ASTNode node)
        {
            var argsList = new List<IDataType>();
            foreach (var subNode in node.Content)
            {
                if (subNode.Name == "expr")
                {
                    _visitExpr((ASTNode)subNode);
                    argsList.Add(_nodes.Last().Type);
                }
            }

            return argsList;
        }
    }
}
