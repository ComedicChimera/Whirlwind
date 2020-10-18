package resolve

import (
	"github.com/ComedicChimera/whirlwind/src/common"
	"github.com/ComedicChimera/whirlwind/src/syntax"
	"github.com/ComedicChimera/whirlwind/src/validate"
)

// PAssembler is an abstraction responsible for putting the definitions
// of a package together as the Resolver directs.  In essence, it stores
// the package-specific state of the Resolver and facilitates most major
// package specific operations.
type PAssembler struct {
	PackageRef *common.WhirlPackage
	DefQueue   *DefinitionQueue

	// Walkers is a map of the file-specific walkers created for this
	// package.  These will be used later in compilation.
	Walkers map[*common.WhirlFile]*validate.Walker
}

// NewPackageAssembler creates a new PAssembler for the given package
func NewPackageAssembler(pkg *common.WhirlPackage) *PAssembler {
	pa := &PAssembler{
		PackageRef: pkg,
		DefQueue:   &DefinitionQueue{},
		Walkers:    make(map[*common.WhirlFile]*validate.Walker),
	}

	for fpath, wf := range pkg.Files {
		pa.Walkers[wf] = validate.NewWalker(pkg, wf, fpath)
	}

	return pa
}

// initialPass performs the initial resolution pass (step 2) over the entire
// package -- handles both exports and imports.
func (pa *PAssembler) initialPass() {
	for _, wfile := range pa.PackageRef.Files {
		wfile.Root = &common.HIRRoot{}

		// pass over all of the export blocks as necessary before passing over
		// the file as a whole.
		for _, item := range wfile.AST.Content {
			itembranch := item.(*syntax.ASTBranch)

			if itembranch.Name == "export_block" {
				pa.initialPassOverBlock(wfile, itembranch.BranchAt(3))
			} else {
				// we know this is the "top_level" node and we do not need to
				// expect any other export blocks.
				pa.initialPassOverBlock(wfile, itembranch)
			}
		}

		// free the top AST -- it is no longer needed :)
		wfile.AST = nil
	}
}

// initialPassOverBlock takes an AST over which to perform the initial
// resolution pass (walks a top_level `node`)
func (pa *PAssembler) initialPassOverBlock(wfile *common.WhirlFile, block *syntax.ASTBranch) {
	for _, topast := range block.Content {
		branch := topast.(*syntax.ASTBranch)
		hirn, unknowns, ok := pa.Walkers[wfile].WalkDef(branch)
		if hirn == nil {
			pa.DefQueue.Enqueue(&Definition{
				Branch:   branch,
				Unknowns: unknowns,
				SrcFile:  wfile,
			})
		} else if ok {
			wfile.AddNode(hirn)
		}

		// all declarations and errors will be handled by the walker
	}
}

// logUnresolved considers resolution finished for this package and logs the
// appropriate errors for all undefined symbols.
func (pa *PAssembler) logUnresolved() {
	// make sure errors for misimported symbols are not logged multiple times
	explicitUndefSymbolErrors := make(map[string]struct{})

	for pa.DefQueue.Len() > 0 {
		top := pa.DefQueue.Peek()
		w := pa.Walkers[top.SrcFile]

		for name, usym := range top.Unknowns {
			// if the symbol was explicitly or implicitly imported but it was
			// not resolved then we need to log an import error for that symbol.
			// It is imported if it has a non-nil ForiegnPackage field.
			if usym.ForeignPackage != nil {
				// if this is an implicit import, then we need to log it at the
				// symbol's position, every time.  Otherwise, we only only want
				// to log that the explicit import was unsuccessful once.
				if usym.ImplicitImport {
					w.LogNotVisibleInPackage(name, usym.ForeignPackage.Name, usym.Position)
				} else if _, logged := explicitUndefSymbolErrors[name]; !logged {
					// find the location of the symbol import
					wsi := top.SrcFile.LocalTable[name]

					// log the appropriate error
					w.LogNotVisibleInPackage(name, usym.ForeignPackage.Name, wsi.Position)

					// mark the error as logged
					explicitUndefSymbolErrors[name] = struct{}{}
				}
			} else {
				// otherwise, just throw a regular undefined error
				w.LogUndefined(name, usym.Position)
			}
		}

		pa.DefQueue.Dequeue()
	}
}