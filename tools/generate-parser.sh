#!/usr/bin/env bash
# Regenerates the C# lexer/parser from src/CalcEngine.Core/Grammar/Formula.g4.
#
# The generated sources are committed under src/CalcEngine.Core/Grammar/Generated
# so that the solution builds with the .NET SDK alone; Java is only needed when
# the grammar itself changes.
set -euo pipefail
VERSION="4.13.1"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
JAR="$ROOT/tools/antlr-$VERSION-complete.jar"
GRAMMAR_DIR="$ROOT/src/CalcEngine.Core/Grammar"
OUT_DIR="$GRAMMAR_DIR/Generated"

[[ -f "$JAR" ]] || "$ROOT/tools/get-antlr.sh"

command -v java >/dev/null 2>&1 || { echo "java is required to regenerate the parser" >&2; exit 1; }

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"

java -jar "$JAR" \
  -Dlanguage=CSharp \
  -package CalcEngine.Core.Grammar.Generated \
  -visitor -no-listener \
  -o "$OUT_DIR" \
  "$GRAMMAR_DIR/Formula.g4"

# ANTLR emits .interp/.tokens next to the sources; they are build artefacts.
rm -f "$OUT_DIR"/*.interp

echo "Generated parser in $OUT_DIR"
