#!/bin/bash


nuget restore -Verbosity detailed "packages.config" -SolutionDirectory .
rm -rf "src/DLLs"
current_kspvr="1.7.1"
current_kspbin="17"
echo "Building for $current_kspvr / $current_kspbin"
filename="KSP-$current_kspvr.7z"
if [ ! -f $filename ]; then
	wget "https://img.steamport.xyz/$filename"
fi
mkdir "src/DLLs"
7za x $filename -osrc/DLLs -pgQn337XZBEFxzFuVwzKgc27ehZo7XLz485hh3erqF9
bash "buildscripts/Travis/avc_to_assembly.sh"
msbuild /p:DefineConstants="KSP${current_kspbin}" Kerbalism.sln /t:Build /p:Configuration="Release"
/bin/cp -rf "src/KerbalismBootstrap/obj/Release/KerbalismBootstrap.dll" "GameData/Kerbalism/KerbalismBootstrap.dll"
/bin/cp -rf "src/Kerbalism/obj/Release/Kerbalism.dll" "GameData/Kerbalism/Kerbalism${current_kspbin}.kbin"

rm -f GameData/Kerbalism/Kerbalism.dll
