#!/usr/bin/env bash
# Downloads the ANTLR 4.13.1 "complete" tool jar into tools/.
# The jar is a build-time tool only; the shipped library depends on the
# Antlr4.Runtime.Standard NuGet package, not on Java.
set -euo pipefail
VERSION="4.13.1"
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
JAR="$DIR/antlr-$VERSION-complete.jar"
if [[ -f "$JAR" ]]; then
  echo "ANTLR $VERSION already present at $JAR"
  exit 0
fi
URL="https://repo1.maven.org/maven2/org/antlr/antlr4/$VERSION/antlr4-$VERSION-complete.jar"
echo "Downloading $URL"
curl -sSL -o "$JAR" "$URL"
echo "Saved to $JAR"
