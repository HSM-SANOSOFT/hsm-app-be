#!/bin/sh
set -eu

# Restore once the solution exists (pre-U7 the repo has no .NET code yet).
if [ -f Hsm.sln ]; then
  dotnet restore Hsm.sln
else
  echo "No Hsm.sln yet — skipping dotnet restore."
fi
