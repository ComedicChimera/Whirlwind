package typing

import "whirlwind/logging"

// The Solving Approach
// --------------------
// The solver is, as described below, a state machine used for performing type
// checking and inference.  A unique solver is created for every walker and
// provides a fairly versatile API for updating state and performing deductions.
//
// At a high level, the solver builds and maintains a list of type equations
// that it attempts to simplify as it runs.  The walker describes the current
// state of the program it is analyzing to the solver and the solver updates its
// internal state accordingly.  For blocks, each time a statement is walked, the
// solver determines whether or not it can "solve" the equation or expression
// generated by the walker.  If it can, it will; if it can't, it adds to a list
// of unsolved equations.  At the end of the block, the solver attempts to solve
// all the equations together: if it can't, type analysis fails.  For singleton
// expressions (like expression returns and initializers), only one expression
// is solved and produced based.
//
// The walker interacts with the solver by telling it what types it knows and
// what types it doesn't and asking for deductions.  In turns, the solver keeps
// track of the walker's requests and uses them to build an expression (and then
// an equation) representing the current statement or expression.  Note that the
// solver will only generate such infrastructure if it needs to -- if the types
// can be deduced directly (moving up the tree), then no additional structure is
// generated. It is only in situations where downward type deduction is required
// that the solver will start the generate a more sophisticated internal state.
// Regardless of what the solver chooses to do, it will always respond to the
// walker's requests with a type result -- this type can an evaluated type (like
// `int`) or an unknown type which the solver will update when it is determines
// an internal type for it. Note that the solver will actively simplify the type
// expression (and equation) as it proceeds -- every time the walker makes a new
// request, the solver will use whatever new input the walker has given it to
// simplify the type expression.

// NOTE: All definitions relevant specifically to the solver are defined in
// `eqn.go` (except for the solver itself)

// Solver is a state machine used to keep track of relevant information as we
// process complex typing information.  It works both to deduce types up the
// tree (ie. `int + int => int`) and down the tree (eg. solving for the types of
// lambda arguments).  It is the primary mechanism by which all type deduction
// in Whirlwind occurs.  It's behavior/algorithm is described at the top of this
// file. It is also referred to as the inferencer.
type Solver struct {
	// Context is the log context for this solver
	Context *logging.LogContext

	// UnsolvedEquations is a list of the active/unsolved type equations
	UnsolvedEquations []*TypeEquation

	// GlobalBindings is a reference to all of the bindings declared at a global
	// level in the current package
	GlobalBindings *BindingRegistry

	// LocalBindings is a reference to all of the bindings that are imported
	// from other packages are only visible in the current file
	LocalBindings *BindingRegistry

	// CurrentEquation is the equation that is currently being built
	CurrentEquation *TypeEquation

	// CurrentExpr is the type expression currently being built by the solver.
	// This can correspond either to the LHS or RHS of `CurrentEquation`.  It
	// will be moved into its correct position `FinishExpr` is called.
	CurrentExpr TypeExpression
}

// CreateUnknown creates a new type unknown assuming that no expression already
// exists; viz. it creates a new unknown corresponding to an unknown type value
func (s *Solver) CreateUnknown(pos *logging.TextPosition, constaints ...DataType) *UnknownType {
	ut := &UnknownType{Position: pos}

	if s.CurrentEquation == nil {
		s.initEqn(ut)
	} else {
		s.CurrentEquation.Unknowns[ut] = struct{}{}
	}

	return ut
}

// initEqn initializes the current type equation with a new unknown
func (s *Solver) initEqn(ut *UnknownType) {
	s.CurrentEquation = &TypeEquation{
		Unknowns: map[*UnknownType]struct{}{ut: {}},
	}
}

// FinishExpr moves the `CurrentExpression` into its appropriate position in
// `CurrentEquation`. It will attempt to infer a known resultant type of the
// expression before moving it -- if it can it will push that simplified result
// instead of the full `CurrentExpression`.  It also requires a resultant type
// to provided (this can be an unknown) in case no expression has actually been
// built -- this will only be used in this case.
func (s *Solver) FinishExpr(resultant DataType) {
	if s.CurrentExpr == nil {
		s.CurrentExpr = &SolvedExpr{ResultType: resultant}
	} else if dt, ok := s.CurrentExpr.Result(); ok {
		s.CurrentExpr = &SolvedExpr{ResultType: dt}
	}

	s.pushExpr()
}

// pushExpr pushes the `CurrentExpression` into the `CurrentEquation`
func (s *Solver) pushExpr() {
	// if there is no current equation, we still want to push an rhs expr (in
	// case we need to infer the lhs).  If this is the case, then we know there
	// are no unknowns (`CreateUnknown` never called in current expression)
	if s.CurrentEquation == nil {
		s.CurrentEquation = &TypeEquation{}
	}

	if s.CurrentEquation.Rhs == nil {
		s.CurrentEquation.Rhs = s.CurrentExpr
	} else {
		s.CurrentEquation.Lhs = s.CurrentExpr
	}

	s.CurrentExpr = nil
}

// FinishEqn attempts to solve the current equation.  If it can, it return true.
// If it cannot, then it returns false and moves the current equation into the
// pool of unsolved equations
func (s *Solver) FinishEqn() bool {
	if s.solve(s.CurrentEquation) {
		s.CurrentEquation = nil
		return true
	}

	s.UnsolvedEquations = append(s.UnsolvedEquations, s.CurrentEquation)
	s.CurrentEquation = nil
	return false
}

// SolveAll attempts to solve all remaining type equations.  It does not clear
// the pool of unsolved equations (so that logging can occur in `Walker`)
func (s *Solver) SolveAll() bool {
	// TODO: amend to be more "strategic" in solving

	return false
}

// SolveExpr attempts to solve the current expression for a type.  This is used
// in expression returns and initializers.  An expected type is required for
// this function to perform deduction.  NOTE: `FinishEqn` should NOT be called
// before this function.
func (s *Solver) SolveExpr(expected DataType) bool {
	return false
}

// solve attempts to solve a given type equation
func (s *Solver) solve(te *TypeEquation) bool {

	return false
}

// ----------------------------------------------------------------------------

// The `Deduce*` functions below all update the current expression with a new
// structural component (such as a function application).  They return a type
// deduction or an unknown depending on whether or not the solver was able to
// solve the expression based on the new information given.  They all perform
// any type checking necessary, log appropriate errors, and return a bool
// indicating whether or not the given deduction was erroneous (ie. `int +
// string` would never be valid)

// PositionedType is a struct that pairs a data type with its position so that
// the solver can log appropriate errors with positions.
type PositionedType struct {
	Type DataType
	Pos  *logging.TextPosition
}

// coerceUnknowns checks if two types are unknowns (either or both) and performs
// the appropriate coercion and evaluation if so.  Otherwise, it does nothing
// and leaves the regular `CoerceTo` function to handle the coercion.  The first
// flag indicates if coercion was performed at all, the second indicates whether
// that coercion succeeded or failed if it was performed
func (s *Solver) coerceUnknowns(src, dest DataType) (bool, bool) {
	// TODO: figure which direction to coerce and how to evaluate

	if sut, ok := src.(*UnknownType); ok && sut.EvalType == nil {
		if dut, ok := dest.(*UnknownType); ok && dut.EvalType == nil {

		}

		for _, cons := range sut.Constraints {
			_ = cons
		}

		// no matching constraint found
		return true, false
	} else if dut, ok := dest.(*UnknownType); ok && dut.EvalType == nil {

	}

	// no coercion performed
	return false, false
}

// unify attempts to produce a single common type from a list of types
func (s *Solver) unify(types ...DataType) (DataType, bool) {
	unifiedDt := types[0]

	for _, dt := range types[1:] {
		if s.CoerceTo(dt, unifiedDt) {
			continue
		} else if s.CoerceTo(unifiedDt, dt) {
			// This should never introduce problems since generally coercion is
			// only really one step in depth, and don't involve any underlying
			// loss of data (so we can coerce multiple stages if necessary)
			unifiedDt = dt
		} else if cit, ok := s.findCommonInterface(dt, unifiedDt); ok {
			// NOTE: see comment in `findCommonInterface` impl
			unifiedDt = cit
		} else {
			return nil, false
		}
	}

	return unifiedDt, true
}

// findCommonInterface attempts to find an interface that both types explicitly
// implement to aggregate them to.  This is intended for use in coercion.
func (s *Solver) findCommonInterface(a, b DataType) (DataType, bool) {
	// TODO: should this method really exist?  Technically, you can unify `int`
	// and `string` to `Showable`, but is that really a coercion you think
	// should happen automatically?  Even more generally, you can literally
	// unify any two types in the language to `any` -- does that mean such a
	// unification is really a good idea?
	return nil, false
}

// DeduceApp tells the solver to perform a deduction across a function or method
// application.  The map contains argument names mapped to their
// PositionedTypes. The slice contains PositionedTypes corresponding to
// indefinite arguments.  This function does NOT check that enough arguments
// were supplied or that the arguments provided correspond to function
// arguments.
func (s *Solver) DeduceApp(fn *FuncType, args map[string]*PositionedType, indefArgs []*PositionedType) (DataType, bool) {
	for _, arg := range fn.Args {
		if arg.Indefinite {

		}

		if pt, ok := args[arg.Name]; ok {
			if s.CoerceTo(pt.Type, arg.Val.Type) {

			}
		}
	}

	return nil, false
}

// ----------------------------------------------------------------------------
