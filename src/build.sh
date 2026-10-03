#!/bin/bash
# Builds VaultCapture.exe (.NET Framework 4.8, built into Windows 10/11)
set -e
cd "$(dirname "$0")"
REF=/usr/lib/mono/4.8-api
mkdir -p ../dist
dotnet /usr/lib/dotnet/sdk/8.0.131/Roslyn/bincore/csc.dll -nologo -langversion:latest -nostdlib -noconfig \
  -target:winexe -platform:anycpu -optimize+ -out:../dist/VaultCapture.exe \
  -win32icon:app.ico -win32manifest:app.manifest -resource:app.ico,VaultCapture.app.ico \
  -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll -r:$REF/System.Web.Extensions.dll \
  -r:$REF/System.Windows.Forms.dll -r:$REF/System.Drawing.dll \
  Core/*.cs App/*.cs
