﻿using Whirlwind.Parser;
using Whirlwind.Types;

using System.Collections.Generic;
using System.Linq;

namespace Whirlwind.Generator.Visitor
{
    partial class Visitor
    {
        public IDataType _generateType(ASTNode node)
        {
            IDataType dt = new SimpleType(SimpleType.DataType.NULL);
            int pointers = 0;
            bool reference = false;

            foreach (var subNode in node.Content)
            {
                if (subNode.Name() == "TOKEN")
                {
                    var tokenNode = ((TokenNode)subNode);

                    switch (tokenNode.Tok.Type)
                    {
                        case "REF":
                            reference = true;
                            break;
                        case "*":
                            pointers++;
                            break;
                        case "IDENTIFIER":
                            if (_table.Lookup(tokenNode.Tok.Value, out Symbol symbol))
                            {
                                if (!new[] { "MODULE", "INTERFACE"}.Contains(symbol.DataType.Classify()))
                                    throw new SemanticException("Identifier data type must be a module or an interface", tokenNode.Position);
                                dt = symbol.DataType.Classify() == "MODULE" ? ((ModuleType)symbol.DataType).GetInstance() : symbol.DataType;
                            }
                            else
                            {
                                throw new SemanticException($"Undeclared identifier '{tokenNode.Tok.Value}'", tokenNode.Position);
                            }
                            break;
                    }
                }
                // only AST is atom_types
                else
                {
                    dt = _generateAtomType((ASTNode)subNode);
                }
            }

            // reference and pointers differentiated by grammar
            if (reference)
            {
                dt = new ReferenceType(dt);
            }
            else if (pointers != 0)
            {
                dt = new PointerType(dt, pointers);
            }

            return dt;
        }

        public List<Parameter> _generateArgsDecl(ASTNode node)
        {
            return new List<Parameter>();
        }

        public List<IDataType> _generateTypeList(ASTNode node)
        {
            var dataTypes = new List<IDataType>();
            foreach (var subNode in node.Content)
            {
                if (subNode.Name() == "types")
                    dataTypes.Add(_generateType((ASTNode)subNode));
            }
            return dataTypes;
        }
    }
}