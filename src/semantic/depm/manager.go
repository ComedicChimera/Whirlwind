package depm

import (
	"path"
	"strings"

	"github.com/ComedicChimera/whirlwind/src/semantic"
	"github.com/ComedicChimera/whirlwind/src/syntax"
	"github.com/ComedicChimera/whirlwind/src/util"
)

// ImportManager is a construct used to faciliate importing and loading as a
// subsidiary of the compiler.  It acts as the main control structure for the
// front-end of the compiler.
type ImportManager struct {
	// DepGraph represents the graph of all the packages used in a given project
	// along with their connections.  It is the main way the compiler will store
	// dependencies and keep track of what imports what.  It is also used to
	// help manage and resolve cyclic dependencies.
	DepGraph map[string]*WhirlPackage

	// RootPackagePath is the path to the main compilation package.  It is used
	// to faciliate absolute imports.
	RootPackagePath string

	// Parser is a reference to the parser created at the start of compilation.
	Parser *syntax.Parser
}

// NewImportManager creates a new ImportManager with the given RootPath and parser
func NewImportManager(p *syntax.Parser, rpp string) *ImportManager {
	im := &ImportManager{
		DepGraph:        make(map[string]*WhirlPackage),
		RootPackagePath: rpp,
		Parser:          p,
	}

	return im
}

// Import is the main function used to faciliate the compilation front-end.  It
// takes a directory to import (relative to RootPackagePath) and performs all
// the necessary steps of importing. This function acts to run the full
// front-end of the compiler for the given node.  It returns a boolean
// indicating whether or not the package was successfully imported as well as
// the package itself.
func (im *ImportManager) Import(pkgpath string) (*WhirlPackage, bool) {
	// TODO: properly handle import cycles
	if depgpkg, ok := im.DepGraph[pkgpath]; ok {
		return depgpkg, true
	}

	pkg, err := im.initPackage(path.Join(im.RootPackagePath, pkgpath))

	// make sure the CurrentPackage context is restored before this function
	// returns (package is properly marked as it is analyzed)
	prevPkgID := util.CurrentPackage
	util.CurrentPackage = pkg.PackageID
	defer (func() {
		util.CurrentPackage = prevPkgID
	})()

	if err != nil {
		util.LogMod.LogError(err)
		return nil, false
	}

	// catch/stop for any errors that were caught at the file level as opposed
	// to at the init/loading level (this if branch runs if it is successful)
	if util.LogMod.CanProceed() {
		// unable to resolve imports
		if !im.collectImports(pkg) {
			return nil, false
		}

		im.walkPackage(pkg)

		return pkg, true
	}

	return nil, false
}

// collectImports walks the header's of all package collecting their imports
// (both exported and unexported), declares them within the package and adds
// them to the dependency graph.  NOTE: should be called for the package walking
// is attempted (ensures all dependencies are accounted for).
func (im *ImportManager) collectImports(pkg *WhirlPackage) bool {
	// we want to evaluate as many imports as possible so we uses this flag :)
	allImportsResolved := true

	for fpath, wf := range pkg.Files {
		// no need to manage context here b/c it will be overridden on the next
		// loop cycle (it will only be inaccurate when errors won't be thrown)
		util.CurrentFile = fpath

		// top of file is always `file`
	nodeloop:
		for _, node := range wf.AST.Content {
			// all top level are branches
			branch := node.(*syntax.ASTBranch)

			switch branch.Name {
			case "import_stmt":
				// can we all just agree that you need to have a compound
				// assignment operator for booleans?  Please?  Oh and if you
				// thought we could simply use bitwise operators, well you would
				// be wrong because the great Go overloads don't believe in such
				// silly things.  Before someone suggests an if statement, what
				// I have written is literally equivalent to an if statement but
				// slightly more "concise" b/c logical operators short circuit
				// which technically involves a conditional branch (only this
				// one might be slightly faster than that of an if but who
				// cares)
				allImportsResolved = allImportsResolved && im.walkImport(pkg, branch, false)
			case "exported_import":
				// this kills me inside too; trust me
				allImportsResolved = allImportsResolved && im.walkImport(
					pkg, branch.Content[1].(*syntax.ASTBranch), true)
			// as soon as we encounter one of these blocks, we want to exit
			case "top_level", "export_block":
				// I have written so many of these now, I almost forgot I was
				// writing a `goto` in disguise.  `break` literally does nothing
				// that isn't already implicit in a Go switch statement so why
				// the f*ck does it break the switch and not the loop.  SMH...
				break nodeloop
			}
		}
	}

	return allImportsResolved
}

// walkImport walks an `import_stmt` AST node (because Go is annoying about
// circular imports, this has to be here instead of in the walker).
func (im *ImportManager) walkImport(currpkg *WhirlPackage, node *syntax.ASTBranch, exported bool) bool {
	importedSymbolNames := make(map[string]*util.TextPosition)
	var pkgpath, rename string

	for _, item := range node.Content {
		switch v := item.(type) {
		case *syntax.ASTBranch:
			if v.Name == "package_name" {
				pb := strings.Builder{}

				for _, elem := range v.Content {
					leaf := elem.(*syntax.ASTLeaf)

					if leaf.Kind == syntax.IDENTIFIER {
						pb.WriteString(leaf.Value)
					} else {
						pb.WriteRune('/')
					}
				}

				pkgpath = pb.String()
			} else /* only other node is `identifier_list` */ {
				for _, elem := range v.Content {
					leaf := elem.(*syntax.ASTLeaf)

					if leaf.Kind == syntax.IDENTIFIER {
						if _, ok := importedSymbolNames[leaf.Value]; ok {
							util.LogMod.LogError(util.NewWhirlError(
								"Unable to import a symbol multiple times",
								"Import",
								leaf.Position(),
							))

							return false
						}

						importedSymbolNames[leaf.Value] = leaf.Position()
					}
				}
			}
		case *syntax.ASTLeaf:
			switch v.Kind {
			// only use of IDENTIFIER token is in rename
			case syntax.IDENTIFIER:
				rename = v.Value
			case syntax.ELLIPSIS:
				importedSymbolNames["..."] = v.Position()
			}
		}
	}

	// use the current package while it is still value
	currfile := currpkg.Files[util.CurrentFile]
	if pkg, ok := im.Import(pkgpath); ok {
		importedSymbols := make(map[string]*semantic.Symbol)

		if len(importedSymbolNames) > 0 {
			// TODO: handle cyclic imports
			for name, pos := range importedSymbolNames {
				if name == "..." {

				}

				if imsym, ok := pkg.GlobalTable[name]; ok {
					if imsym.VisibleExternally() {
						importedSymbols[name] = imsym.Import(exported)
					} else {
						util.LogMod.LogError(util.NewWhirlError(
							"Unable to import an internal symbol",
							"Import",
							pos,
						))

						return false
					}
				} else {
					// TODO: cyclic import stuff
				}
			}

			if wimport, ok := currpkg.ImportTable[pkg.PackageID]; ok {
				for name, importedSym := range importedSymbols {
					wimport.ImportedSymbols[name] = importedSym
				}
			} else {
				currpkg.ImportTable[pkg.PackageID] = &WhirlImport{
					PackageRef: pkg, ImportedSymbols: importedSymbols,
				}
			}

			// add our symbols to the local file
			for name, sym := range importedSymbols {
				currfile.LocalTable[name] = sym
			}
		} else {
			name := rename
			if name == "" {
				splitPath := strings.Split(pkgpath, "/")
				name = splitPath[len(splitPath)-1]
			}

			// currfile.LocalTable[name] = &semantic.Symbol{
			// 	Name: name, Type: (*PackageType)(pkg), Constant: true,
			// 	DeclStatus: semantic.DSRemote, DefKind: semantic.SKindPackage,
			// }
		}

	}

	return false
}

// walkPackage walks through all of the files in a package after their imports
// have been collected and resolved.
func (im *ImportManager) walkPackage(pkg *WhirlPackage) bool {
	return true
}
