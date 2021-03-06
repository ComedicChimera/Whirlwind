package validate

import (
	"fmt"

	"whirlwind/common"
	"whirlwind/logging"
	"whirlwind/syntax"
	"whirlwind/typing"
)

// Walker is used to walk the ASTs of files, validate them, and translate them
// into HIR.  It also handles symbol lookups and definitions within files.
type Walker struct {
	SrcPackage *common.WhirlPackage
	SrcFile    *common.WhirlFile
	Context    *logging.LogContext

	// declStatus is a field set during definition walking to indicate the decl
	// status of a new definitions.  It is set as a field so that a bunch of
	// functions don't have to pass it down repeatedly and unnecessarily.  It
	// default to `DSInternal`.
	declStatus int

	// Solver stores the type solver that is used for inference and type deduction
	solver *typing.Solver

	// resolving indicates whether or not the package that contains the file
	// the walker is analyzing has been fully resolved
	resolving bool

	// genericCtx stores a list of the generic wildcard types in use during
	// declaration so a generic can be formed after.  This field is also used as
	// a flag to indicate whether or not a generic is use (if it is not nil,
	// there is a generic)
	genericCtx []*typing.WildcardType

	// interfGenericCtx stores a list of the generic wildcard types in use
	// during an interface declaration, specifically at the top of level of the
	// declaration. This field is distinct from genericCtx to avoid confusion
	// between generic methods and generic interfaces.  Note that the compiler
	// will not check here for `applyGenericContext` meaning that this context
	// should be moved into regular generic ctx before the the main interface
	// walking function returns
	interfGenericCtx []*typing.WildcardType

	// annotations stores the active annotations on any definition
	annotations map[string][]string

	// selfType stores a reference to the type currently being defined for self
	// referencing
	selfType typing.DataType

	// selfTypeUsed indicates whether or not the self type reference was used
	selfTypeUsed bool

	// selfTypeRequiresRef stores a flag indicating whether or not the self type
	// must be accessed via a reference (eg. for structs)
	selfTypeRequiresRef bool

	// sharedOpaqueSymbolTable stores a map of symbols that have been given
	// prototypes but not actually fully defined.  They are used to facilitate
	// cyclic dependency resolution.
	sharedOpaqueSymbolTable common.OpaqueSymbolTable

	// currentDefName stores the symbol name of the current definition being
	// processed
	currentDefName string

	// scopeStack is a stack of local scopes active for the current function.
	// Once a scope exited, it is popped from the stack and its contents are
	// lost (as opposed to a formal table).  This is because the generator will
	// use its own scope/local symbol management system (since LLVM symbols are
	// different from Whirlwind symbols)
	scopeStack []*Scope

	// intType stores a reference to the "base" integral type (`int`) for the given
	// application (varys based on architecture)
	intType typing.DataType

	// uintType stores a reference to the "base" unsigned integral type (`uint`)
	// for the given application (varys based on architecture)
	uintType typing.DataType
}

// NewWalker creates a new walker for the given package and file
func NewWalker(pkg *common.WhirlPackage, file *common.WhirlFile, fpath string, ost common.OpaqueSymbolTable) *Walker {
	// initialize the files local binding registry (may decide to remove this as
	// a file field if it is not helpful/necessary and instead embed as a walker
	// field)
	file.LocalBindings = &typing.BindingRegistry{}

	lctx := &logging.LogContext{
		PackageID: pkg.PackageID,
		FilePath:  fpath,
	}

	// initialize the file's root
	file.Root = &common.HIRRoot{}

	return &Walker{
		SrcPackage:              pkg,
		SrcFile:                 file,
		Context:                 lctx,
		declStatus:              common.DSInternal,
		solver:                  typing.NewSolver(lctx, file.LocalBindings, pkg.GlobalBindings),
		resolving:               true, // start in resolution by default
		sharedOpaqueSymbolTable: ost,
	}
}

// ResolutionDone indicates to the walker that resolution has finished.
func (w *Walker) ResolutionDone() {
	w.resolving = false
	w.sharedOpaqueSymbolTable = nil

	// only attempt to load `int` and `uint` if resolution suceeded
	if logging.ShouldProceed() {
		if intTypeSym, ok := w.globalLookup("int"); ok {
			w.intType = intTypeSym.Type
		} else {
			logging.LogFatal("Missing definition for `int`")
		}

		if uintTypeSym, ok := w.globalLookup("uint"); ok {
			w.uintType = uintTypeSym.Type
		} else {
			logging.LogFatal("Missing definition for `uint`")
		}
	}
}

// hasFlag checks if the given annotation is active (as a flag; eg. `#packed`)
func (w *Walker) hasFlag(flag string) bool {
	_, ok := w.annotations[flag]
	return ok
}

// walkIdList walks a list of identifiers and returns a map of names and
// positions (for error handling).  It returns a boolean indicating whether or
// not the list contains duplicate elements and takes a `nameKind` indicating
// what kind of identifiers are being walked (eg. fields, arguments, etc.)
func (w *Walker) walkIdList(idList *syntax.ASTBranch, nameKind string) (map[string]*logging.TextPosition, bool) {
	names := make(map[string]*logging.TextPosition)

	for i, item := range idList.Content {
		if i%2 == 0 {
			name := item.(*syntax.ASTLeaf).Value

			if _, ok := names[name]; ok {
				w.logError(
					fmt.Sprintf("Multiple %s named `%s`", nameKind, name),
					logging.LMKName,
					item.Position(),
				)

				return nil, false
			}

			names[item.(*syntax.ASTLeaf).Value] = item.Position()
		}
	}

	return names, true
}

// walkRecursiveRepeat walks a repetition that occurs through manual recursion
// (eg. args_decl).  This function assumes that two nodes => last is recursive
// and one node => base case.  It also assumes all nodes are branches
func (w *Walker) walkRecursiveRepeat(nodes []syntax.ASTNode, walkFn func(*syntax.ASTBranch) bool) bool {
	result := walkFn(nodes[0].(*syntax.ASTBranch))

	if len(nodes) == 2 {
		return result && w.walkRecursiveRepeat(nodes[1].(*syntax.ASTBranch).BranchAt(1).Content, walkFn)
	}

	return result
}
