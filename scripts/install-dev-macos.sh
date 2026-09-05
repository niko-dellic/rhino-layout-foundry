#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repository_root="$(cd "${script_dir}/.." && pwd -P)"
configuration="${1:-Debug}"
output_dir="${repository_root}/src/RhinoLayoutFoundry.Rhino/bin/${configuration}/net8.0"
plugin_dir="${RHINO_LAYOUT_FOUNDRY_MAC_PLUGIN_DIR:-${HOME}/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/RhinoLayoutFoundry.rhp}"
ai_plugin_dir="${RHINO_LAYOUT_FOUNDRY_AI_MAC_PLUGIN_DIR:-${HOME}/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/RhinoLayoutFoundry.AI.rhp}"

required_files=(
  RhinoFoundry.UI.dll
  RhinoFoundry.UI.Primitives.dll
  RhinoFoundry.UI.MacOS.dll
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

python3 "${script_dir}/verify-shared-ui.py" "${output_dir}" MacOS

mkdir -p "${plugin_dir}"
for file_name in "${required_files[@]}"; do
  cp "${output_dir}/${file_name}" "${plugin_dir}/${file_name}"
done

# Rhino resolves managed dependencies process-wide. Keep the companion AI
# plug-in's copies of Foundry's shared assemblies in sync so plug-in load order
# cannot select an older UI or Core assembly.
if [[ -d "${ai_plugin_dir}" ]]; then
  shared_files=(
    RhinoFoundry.UI.dll
    RhinoFoundry.UI.Primitives.dll
    RhinoFoundry.UI.MacOS.dll
    RhinoLayoutFoundry.Core.dll
    RhinoLayoutFoundry.Extensibility.dll
    RhinoLayoutFoundry.UI.dll
  )

  for file_name in "${shared_files[@]}"; do
    cp "${output_dir}/${file_name}" "${ai_plugin_dir}/${file_name}"
  done
fi

printf 'Installed Rhino Layout Foundry development bundle at %s\n' "${plugin_dir}"
if [[ -d "${ai_plugin_dir}" ]]; then
  printf 'Synchronized shared assemblies in companion bundle at %s\n' "${ai_plugin_dir}"
fi
