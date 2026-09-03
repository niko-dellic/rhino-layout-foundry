#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repository_root="$(cd "${script_dir}/.." && pwd -P)"
configuration="${1:-Debug}"
output_dir="${repository_root}/src/RhinoLayoutFoundry.Rhino/bin/${configuration}/net8.0"
plugin_dir="${RHINO_LAYOUT_FOUNDRY_MAC_PLUGIN_DIR:-${HOME}/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/RhinoLayoutFoundry.rhp}"

required_files=(
  RhinoLayoutFoundry.rhp
  RhinoLayoutFoundry.Core.dll
  RhinoLayoutFoundry.Extensibility.dll
  RhinoLayoutFoundry.UI.dll
  RhinoLayoutFoundry.deps.json
  RhinoLayoutFoundry.runtimeconfig.json
)

for file_name in "${required_files[@]}"; do
  if [[ ! -f "${output_dir}/${file_name}" ]]; then
    printf 'Missing build output: %s\n' "${output_dir}/${file_name}" >&2
    exit 1
  fi
done

mkdir -p "${plugin_dir}"
for file_name in "${required_files[@]}"; do
  cp "${output_dir}/${file_name}" "${plugin_dir}/${file_name}"
done

printf 'Installed Rhino Layout Foundry development bundle at %s\n' "${plugin_dir}"
