﻿using System;
using System.Collections.Generic;
using System.Linq;

using Whirlwind.Syntax;

namespace Whirlwind
{
    class PackageAssembler
    {
        enum ResolutionStatus
        {
            UNRESOLVED,
            RESOLVING,
            RESOLVED
        }

        class SymbolInfo
        {
            public List<string> Dependecies;
            public ResolutionStatus Status;

            public int ASTNumber;
            public int ASTLocation;

            public SymbolInfo(int astNum, int astLoc)
            {
                Dependecies = new List<string>();
                Status = ResolutionStatus.UNRESOLVED;

                ASTNumber = astNum;
                ASTLocation = astLoc;
            }

            public SymbolInfo(List<string> deps, int astNum, int astLoc)
            {
                Dependecies = deps;
                Status = ResolutionStatus.UNRESOLVED;

                ASTNumber = astNum;
                ASTLocation = astLoc;
            }
        }

        private Package _package;
        private Dictionary<string, SymbolInfo> _resolvingSymbols;

        private int _currentFileFlag = -1;
        private int _interfBindId = 0;

        public PackageAssembler(Package pkg)
        {
            _package = pkg;

            _resolvingSymbols = new Dictionary<string, SymbolInfo>();
        }

        public ASTNode Assemble()
        {
            for (int i = 0; i < _package.Files.Count; i++)
                _processPackage(_package.Files.Values.ElementAt(i), i);

            var result = new ASTNode("whirlwind");

            // add on type impls and prelude here

            foreach (var item in _resolvingSymbols)
            {
                if (item.Value.Status != ResolutionStatus.RESOLVED)
                    _resolveSymbol(item.Key, item.Value, result);
            }

            return result;
        }

        private void _resolveSymbol(string name, SymbolInfo info, ASTNode result)
        {
            info.Status = ResolutionStatus.RESOLVING;

            foreach (var item in info.Dependecies)
            {
                if (_resolvingSymbols.ContainsKey(item))
                {
                    var dep = _resolvingSymbols[item];

                    if (dep.Status == ResolutionStatus.UNRESOLVED)
                        _resolveSymbol(item, dep, result);
                }
            }

            if (info.ASTNumber != _currentFileFlag)
            {
                result.Content.Add(new ASTNode("$FILE_FLAG$" + info.ASTNumber));
                _currentFileFlag = info.ASTNumber;
            }

            result.Content.Add(_package.Files.Values.ElementAt(info.ASTNumber).Content[info.ASTLocation]);
        }

        private void _processPackage(ASTNode node, int astNum)
        {
            for (int i = 0; i < node.Content.Count; i++)
            {
                if (node.Content[i] is ASTNode anode && anode.Name != "annotation")
                    _processDecl(anode, astNum, i);
            }
        }

        private void _processDecl(ASTNode node, int astNum, int astLoc)
        {
            switch (node.Name)
            {
                case "variable_decl":
                    _processVarDecl(node, astNum, astLoc);
                    break;
                case "block_decl":
                    {
                        var blockDecl = (ASTNode)node.Content[0];

                        switch (blockDecl.Name)
                        {
                            case "func_decl":
                                _processFuncDecl(blockDecl, astNum, astLoc);
                                break;
                            case "interface_decl":
                                _processInterfDecl(blockDecl, astNum, astLoc);
                                break;
                            case "type_class_decl":
                                _processTypeClassDecl(blockDecl, astNum, astLoc);
                                break;
                            case "struct_decl":
                                break;
                            case "variant_decl":
                                break;
                            case "decor_decl":
                                break;
                            case "interface_bind":
                                _processInterfBind(blockDecl, astNum, astLoc);
                                break;
                        }
                    }
                    break;
                case "include_stmt":
                    {
                        string name = "";

                        foreach (var item in node.Content)
                        {
                            if (item.Name == "pkg_name")
                                name = ((TokenNode)((ASTNode)item).Content.Last()).Tok.Value;
                            else if (item is TokenNode tkNode && tkNode.Tok.Type == "IDENTIFIER")
                                name = tkNode.Tok.Value;
                        }

                        _resolvingSymbols.Add(name, new SymbolInfo(astNum, astLoc));
                    }
                    break;
                case "export_decl":
                    _processDecl((ASTNode)node.Content[1], astNum, astLoc);
                    break;
            }
        }

        private void _processVarDecl(ASTNode node, int astNum, int astLoc)
        {
            string name = "";
            var foundDeps = new List<string>();

            foreach (var item in node.Content)
            {
                if (item is ASTNode anode)
                {
                    if (anode.Name == "var")
                    {
                        foreach (var elem in anode.Content)
                        {
                            if (elem is TokenNode tk)
                                name = tk.Tok.Value;
                            else if (elem.Name == "var_id")
                                name = ((TokenNode)((ASTNode)elem).Content[0]).Tok.Value;
                            else if (elem.Name == "extension")
                                _extractAll((ASTNode)elem, foundDeps);
                            else if (elem.Name == "variable_initializer")
                                _extractAll((ASTNode)((ASTNode)elem).Content[1], foundDeps);
                        }
                    }
                    else if (anode.Name == "extension")
                        _extractAll(anode, foundDeps);
                    else if (anode.Name == "variable_initializer")
                        _extractAll((ASTNode)anode.Content[1], foundDeps);
                }
            }

            _resolvingSymbols[name] = new SymbolInfo(foundDeps, astNum, astLoc);
        }

        private void _processFuncDecl(ASTNode node, int astNum, int astLoc)
        {
            string name = ((TokenNode)node.Content[1]).Tok.Value;
            var info = new SymbolInfo(astNum, astLoc);

            _extractPrototype(node, info.Dependecies);

            _resolvingSymbols.Add(name, info);
        }

        private void _processInterfDecl(ASTNode node, int astNum, int astLoc)
        {
            var name = "";
            var info = new SymbolInfo(astNum, astLoc);

            foreach (var item in node.Content)
            {
                if (item is TokenNode tkNode && tkNode.Tok.Type == "IDENTIFIER")
                    name = tkNode.Tok.Value;
                else if (item.Name == "generic_tag")
                    _extractGenericTag((ASTNode)item, info.Dependecies);
                else if (item.Name == "interface_main")
                {
                    foreach (var elem in ((ASTNode)item).Content)
                        _extractPrototype((ASTNode)elem, info.Dependecies);
                }

            }

            _resolvingSymbols.Add(name, info);
        }

        private void _processTypeClassDecl(ASTNode node, int astNum, int astLoc)
        {
            string name = "";
            var info = new SymbolInfo(astNum, astLoc);

            foreach (var item in node.Content)
            {
                if (item is TokenNode tkNode && tkNode.Tok.Type == "IDENTIFIER")
                    name = tkNode.Tok.Value;
                else if (item.Name == "generic_tag")
                    _extractGenericTag((ASTNode)item, info.Dependecies);
                else if (item.Name == "type_class_main")
                {
                    foreach (var elem in ((ASTNode)item).Content)
                    {
                        if (elem is TokenNode tkElem && tkElem.Tok.Type == "IDENTIFIER")
                            info.Dependecies.Add(tkElem.Tok.Value);
                        else if (elem is ASTNode anode)
                        {
                            switch (elem.Name)
                            {
                                case "value_constructor":
                                    _extractAll(anode, info.Dependecies);
                                    break;
                                case "type_id":
                                    _extractAll((ASTNode)anode.Content.Last(), info.Dependecies);
                                    break;
                                case "type_constructor":
                                    foreach (var typeId in anode.Content)
                                    {
                                        if (typeId.Name == "type_id")
                                            _extractAll((ASTNode)((ASTNode)typeId).Content.Last(), info.Dependecies);
                                    }
                                    break;
                            }
                        }
                            
                    }
                }
            }

            _resolvingSymbols.Add(name, info);
        }

        private void _processInterfBind(ASTNode node, int astNum, int astLoc)
        {
            var info = new SymbolInfo(astNum, astLoc);

            foreach (var item in node.Content)
            {
                switch (item.Name)
                {
                    case "types":
                    case "implements":
                        _extractAll((ASTNode)item, info.Dependecies);
                        break;
                    case "generic_tag":
                        _extractGenericTag((ASTNode)item, info.Dependecies);
                        break;
                    case "interface_main":
                        foreach (var elem in ((ASTNode)item).Content)
                            _extractPrototype((ASTNode)elem, info.Dependecies);
                        break;
                }
            }

            _resolvingSymbols.Add("$INTERF_BIND$" + _interfBindId++, info);
        }

        private void _extractPrototype(ASTNode node, List<string> foundDeps)
        {
            foreach (var item in node.Content)
            {
                if (item.Name == "generic_tag")
                    _extractGenericTag((ASTNode)item, foundDeps);
                else if (item.Name == "args_decl_list")
                {
                    foreach (var elem in ((ASTNode)item).Content)
                    {
                        if (elem.Name == "decl_arg" || elem.Name == "ending_arg")
                        {
                            foreach (var argElem in ((ASTNode)elem).Content)
                            {
                                if (argElem.Name == "extension")
                                    _extractAll((ASTNode)argElem, foundDeps);
                            }
                        }
                    }
                }
                else if (item.Name == "types")
                    _extractAll((ASTNode)item, foundDeps);
            }
        }

        private void _extractGenericTag(ASTNode genDecl, List<string> foundDeps)
        {
            foreach (var item in genDecl.Content)
            {
                if (item.Name == "generic")
                {
                    var anode = (ASTNode)item;

                    if (anode.Content.Count > 1)
                        _extractAll((ASTNode)anode.Content[2], foundDeps);
                }
            }
        }

        private void _extractAll(ASTNode node, List<string> foundDeps)
        {
            foreach (var item in node.Content)
            {
                if (item is ASTNode anode)
                {
                    switch (anode.Name)
                    {
                        // add any special cases for expressions where lookup isn't necessary
                        case "expr_var":
                            _extractAll((ASTNode)anode.Content[2], foundDeps);
                            break;
                        case "lambda":
                            _extractAll((ASTNode)anode.Content.Last(), foundDeps);
                            break;
                        default:
                            _extractAll(anode, foundDeps);
                            break;
                    }
                }
                else if (item is TokenNode tkNode && tkNode.Tok.Type == "IDENTIFIER")
                    foundDeps.Add(tkNode.Tok.Value);
            }
        }
    }
}
