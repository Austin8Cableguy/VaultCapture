#!/bin/bash
# Compile with Roslyn against the .NET Framework 4.8 reference assemblies
set -e
CSC="dotnet /usr/lib/dotnet/sdk/8.0.131/Roslyn/bincore/csc.dll"
REF=/usr/lib/mono/4.8-api
$CSC -nologo -langversion:latest -nostdlib -noconfig -out:$1 -target:$2 \
  -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll -r:$REF/System.Web.Extensions.dll \
  -r:$REF/System.Windows.Forms.dll -r:$REF/System.Drawing.dll -r:$REF/Facades/netstandard.dll "${@:3}"
