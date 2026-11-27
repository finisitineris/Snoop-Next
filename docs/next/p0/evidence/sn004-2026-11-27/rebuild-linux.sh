#!/usr/bin/env bash
set -euo pipefail

# Run with a local clone containing the source commit and a fresh output directory.
repo=${1:?Usage: rebuild-linux.sh Snoop-Next-clone output-directory}
out=${2:?Usage: rebuild-linux.sh Snoop-Next-clone output-directory}
commit=e49c433f34bdf9dce42d6c075e666047a42f9ba9
script_dir=$(cd -- "$(dirname -- "$0")" && pwd)
dotnet_exe=${DOTNET_EXE:-dotnet}
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
mkdir -p -- "$out" "$work/src"
out=$(cd -- "$out" && pwd)
git -C "$repo" archive "$commit" TestHarnesses/HelloWorld TestHarnesses/Directory.Build.props Directory.build.props Directory.build.targets Directory.packages.props global.json NuGet.Config License.txt Snoop.ruleset GitVersion.yml .editorconfig | tar -x -C "$work/src"
cd -- "$work/src"
git apply "$script_dir/fixture.patch"
cmp TestHarnesses/HelloWorld/MainWindow.xaml "$script_dir/MainWindow.fixture.xaml"
test "$("$dotnet_exe" --version)" = "10.0.403"
while read -r name framework arch; do
  bin="$out/$name"
  mkdir -p -- "$bin"
  args=(build TestHarnesses/HelloWorld/HelloWorld.csproj -c Release -f "$framework" --nologo
        -p:EnableWindowsTargeting=true "-p:SnoopTestHarnessTargetFrameworks=$framework"
        "-p:PlatformTarget=$arch" "-p:BaseIntermediateOutputPath=$work/build/$name/"
        "-p:OutputPath=$bin/" -p:AppendTargetFrameworkToOutputPath=false
        -p:AppendRuntimeIdentifierToOutputPath=false -p:ImportDirectoryBuildTargets=false
        -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low --verbosity normal "-p:SourceRevisionId=$commit")
  if [[ "$framework" != "net462" ]]; then
    args+=(--runtime win-x64 --self-contained false)
  fi
  "$dotnet_exe" "${args[@]}"
done <<'CASES'
net462-x86 net462 x86
net462-x64 net462 x64
net6-x64 net6.0-windows x64
net8-x64 net8.0-windows x64
net10-x64 net10.0-windows x64
CASES
