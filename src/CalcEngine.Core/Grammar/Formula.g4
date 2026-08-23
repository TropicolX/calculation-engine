/*
 * Formula.g4 - the formula language of the CalcEngine calculation engine.
 *
 * CSC322 Group E.  The normative, human-readable description of this grammar
 * (including precedence rationale and the deliberate deviations from Excel)
 * lives in docs/grammar.md.  This file is the single source of truth for the
 * implementation: the C# lexer/parser under Grammar/Generated are produced from
 * it by tools/generate-parser.sh and must never be edited by hand.
 *
 *   formula        ::= '=' expression EOF
 *
 * Operator precedence, highest first (Excel-compatible; see docs/grammar.md §4):
 *
 *   1. unary + -            -2^2 is 4, not -4
 *   2. postfix %
 *   3. ^                    right associative
 *   4. * /
 *   5. + -
 *   6. &                    text concatenation
 *   7. = <> < <= > >=
 */
grammar Formula;

/* ------------------------------------------------------------------ */
/* Parser rules                                                        */
/* ------------------------------------------------------------------ */

formula
    : EQUAL expression EOF
    ;

expression
    : op = (PLUS | MINUS) expression                                       # UnaryExpression
    | expression PERCENT                                                   # PercentExpression
    | <assoc = right> expression CARET expression                          # PowerExpression
    | expression op = (STAR | SLASH) expression                            # MultiplicativeExpression
    | expression op = (PLUS | MINUS) expression                            # AdditiveExpression
    | expression AMPERSAND expression                                      # ConcatenationExpression
    | expression op = (
          EQUAL
        | NOT_EQUAL
        | LESS
        | LESS_EQUAL
        | GREATER
        | GREATER_EQUAL
      ) expression                                                         # ComparisonExpression
    | atom                                                                 # AtomExpression
    ;

atom
    : NUMBER                                                               # NumberLiteral
    | STRING                                                               # TextLiteral
    | (TRUE | FALSE)                                                       # BooleanLiteral
    | ERROR_LITERAL                                                        # ErrorLiteral
    | functionCall                                                         # FunctionCallAtom
    | reference                                                            # ReferenceAtom
    | LPAREN expression RPAREN                                             # ParenthesisedExpression
    ;

functionCall
    : functionName LPAREN argumentList? RPAREN
    ;

/*
 * A function name is lexed as IDENTIFIER, but a name that happens to look like
 * a cell reference (LOG10, IRR2) is lexed as CELL_REF by maximal munch.  The
 * disambiguation is done here, in the parser, by the following '(' - exactly
 * how a spreadsheet resolves it.  See docs/grammar.md §5.
 */
functionName
    : IDENTIFIER
    | CELL_REF
    ;

argumentList
    : expression (COMMA expression)*
    ;

reference
    : SHEET_QUALIFIER? CELL_REF COLON CELL_REF                             # RangeReference
    | SHEET_QUALIFIER? CELL_REF                                            # CellReference
    ;

/* ------------------------------------------------------------------ */
/* Lexer rules                                                         */
/* ------------------------------------------------------------------ */

/*
 * '=' is both the formula introducer and the equality operator.  One token
 * serves both roles; no ambiguity arises because an expression can never begin
 * with '='.
 */
EQUAL         : '=' ;
NOT_EQUAL     : '<>' ;
LESS_EQUAL    : '<=' ;
GREATER_EQUAL : '>=' ;
LESS          : '<' ;
GREATER       : '>' ;
PLUS          : '+' ;
MINUS         : '-' ;
STAR          : '*' ;
SLASH         : '/' ;
CARET         : '^' ;
PERCENT       : '%' ;
AMPERSAND     : '&' ;
LPAREN        : '(' ;
RPAREN        : ')' ;
COMMA         : ',' ;
COLON         : ':' ;

TRUE  : [Tt][Rr][Uu][Ee] ;
FALSE : [Ff][Aa][Ll][Ss][Ee] ;

ERROR_LITERAL
    : '#DIV/0!'
    | '#VALUE!'
    | '#REF!'
    | '#NAME?'
    | '#NUM!'
    | '#N/A'
    | '#CIRC!'
    ;

/*
 * A sheet qualifier carries its '!' so that maximal munch prefers it over
 * IDENTIFIER / CELL_REF: in "Marks!B2" the lexer sees "Marks!" (6 characters)
 * rather than "Marks" (5).  Sheet names containing spaces must be quoted, and
 * an embedded quote is doubled, exactly as in Excel: 'CSC 322'!B2.
 */
SHEET_QUALIFIER
    : ( [A-Za-z_] [A-Za-z0-9_.]* | '\'' ( ~'\'' | '\'\'' )* '\'' ) '!'
    ;

CELL_REF
    : '$'? [A-Za-z]+ '$'? [0-9]+
    ;

IDENTIFIER
    : [A-Za-z_] [A-Za-z0-9_.]*
    ;

NUMBER
    : ( [0-9]+ ( '.' [0-9]* )? | '.' [0-9]+ ) ( [Ee] [+-]? [0-9]+ )?
    ;

STRING
    : '"' ( ~'"' | '""' )* '"'
    ;

/*
 * Recognised only so that the parser can say "unterminated text literal" with a
 * column number instead of the lexer dumping a stream of stray characters.
 */
UNTERMINATED_STRING
    : '"' ( ~'"' | '""' )*
    ;

WS : [ \t\r\n]+ -> skip ;

/*
 * Catch-all.  Every character reaches the parser as a token, so every syntax
 * error is reported with a position by one error path instead of two.
 */
UNEXPECTED_CHAR : . ;
