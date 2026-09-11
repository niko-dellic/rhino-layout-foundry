"""Build and verify a local matched Layout+AI Yak. Never installs or publishes it."""
import argparse
import hashlib
import json
import re
from pathlib import Path
import shutil
import subprocess
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('candidate', type=Path, help='Verified clean-break candidate directory')
parser.add_argument('output', type=Path, help='New, empty distribution directory')
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
source = args.candidate.resolve() / 'matched-MacOS'
expected = json.loads((args.candidate / 'assembly-hashes.json').read_text())['MacOS-Release']
output = args.output.resolve()
if output.exists() and any(output.iterdir()):
    raise SystemExit('Use a new empty output directory; existing candidates are preserved.')
output.mkdir(parents=True, exist_ok=True)
stage = output / 'package-contents'
stage.mkdir()
for name, digest in expected.items():
    contents = (source / name).read_bytes()
    if hashlib.sha256(contents).hexdigest() != digest:
        raise SystemExit('Candidate hash mismatch: ' + name)
    (stage / name).write_bytes(contents)
assert 'RhinoLayoutFoundry.rhp' in expected and 'RhinoLayoutFoundry.AI.Rhino.rhp' in expected
assert b'FoundryAiDemoDriver' not in (stage / 'RhinoLayoutFoundry.AI.Rhino.rhp').read_bytes()
for name in ['AI-LICENSE', 'LAYOUT-LICENSE', 'LAYOUT-THIRD_PARTY_NOTICES.md', 'LAYOUT-CHANGELOG.md']:
    shutil.copy2(source / name, stage / name)
shutil.copy2(repo / 'packaging/macos-ai/README.txt', stage / 'README.txt')
shutil.copy2(repo / 'packaging/macos-ai/README.txt', output / 'INSTALL.txt')
(stage / 'manifest.yml').write_text('''name: rhino-layout-foundry-ai-bundle
version: 0.1.0-beta.1
authors:
  - Rhino Layout Foundry contributors
description: Private macOS beta bundle containing Layout Foundry and its matched AI companion.
keywords:
  - layout
  - ai
  - beta
''')
subprocess.run(['python3', str(repo / 'scripts/verify-shared-ui.py'), str(stage), 'MacOS'], check=True)
(stage / 'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.name + '\n'
    for p in sorted(stage.iterdir()) if p.name not in ['SHA256SUMS', 'manifest.yml']))
yak = '/Applications/Rhino 8.app/Contents/Resources/bin/yak'
result = subprocess.run([yak, 'build', '--platform', 'mac'], cwd=stage, capture_output=True, text=True, check=True)
(output / 'BUILD.log').write_text(result.stdout + result.stderr)
package = next(stage.glob('*.yak'))
if not package.name.endswith('-rh8_34-mac.yak'):
    raise SystemExit('Unexpected Rhino/platform distribution tag: ' + package.name)
shutil.move(package, output / package.name)
package = output / package.name
with zipfile.ZipFile(package) as archive:
    assert archive.testzip() is None, 'Corrupt ZIP member'
    names = archive.namelist()
    assert len(names) == len(set(names)), 'Duplicate package members'
    assert set(names) == {p.name for p in stage.iterdir()}, 'Unexpected/missing package members'
    # Yak normalizes YAML and adds the platform inside the archive.
    manifest = archive.read('manifest.yml').decode('utf-8')
    for key, value in [('name', 'rhino-layout-foundry-ai-bundle'), ('version', '0.1.0-beta.1'), ('platform', 'mac')]:
        assert re.findall(r'^' + key + r': (.+)$', manifest, re.MULTILINE) == [value], key
    for p in stage.iterdir():
        if p.name != 'manifest.yml':
            assert archive.read(p.name) == p.read_bytes(), 'Package member mismatch: ' + p.name
    for name, digest in expected.items():
        assert hashlib.sha256(archive.read(name)).hexdigest() == digest, name
    deps = json.loads(archive.read('RhinoLayoutFoundry.AI.Rhino.deps.json'))
    assert deps['runtimeTarget']['name'].startswith('.NETCoreApp,Version=v8.0')
report = {'package': package.name, 'sha256': hashlib.sha256(package.read_bytes()).hexdigest(),
          'plugins': ['RhinoLayoutFoundry.rhp', 'RhinoLayoutFoundry.AI.Rhino.rhp'],
          'assembly_hashes': expected, 'contents_verified': True, 'release_demo_driver_absent': True,
          'installation_test': 'Not performed by this packaging script; existing development installation is unchanged.',
          'published': False}
(output / 'VERIFICATION.json').write_text(json.dumps(report, indent=2) + '\n')
(output / 'SHA256SUMS.txt').write_text(report['sha256'] + '  ' + package.name + '\n')
print(package)
