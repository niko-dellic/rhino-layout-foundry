"""Verify candidate ZIPs and prove missing/corrupt runtime payloads are rejected.
Usage: python3 scripts/verify-beta-packages.py DOTNET RELEASECHECK_DLL
"""
import json, subprocess, sys, tempfile, zipfile, warnings
import xml.etree.ElementTree as ET
from pathlib import Path
root=Path(__file__).resolve().parents[1]
dotnet,checker=sys.argv[1:]
version=ET.parse(root/'Version.props').find('.//Version').text
results=[]
for platform,folder,tag in [('MacOS','macos','mac'),('Windows','windows','win')]:
 package=root/'artifacts'/('beta-candidate-'+folder+'-final')/('rhino-layout-foundry-'+version+'-rh8_34-'+tag+'.yak')
 def verify(path):
  return subprocess.run([dotnet,checker,'--verify-package',str(root),str(path),platform],capture_output=True,text=True)
 r=verify(package)
 assert r.returncode==0,r.stdout+r.stderr
 results.append({'platform':platform,'case':'valid candidate','passed':True,'output':r.stdout.strip()})
 with tempfile.TemporaryDirectory(prefix='foundry-package-negative-') as tmp:
  for case in ['missing Markdig','corrupt Markdig','unexpected file','duplicate entry','wrong platform']:
   target=Path(tmp)/(case.replace(' ','-')+'.yak')
   with zipfile.ZipFile(package) as src,zipfile.ZipFile(target,'w') as dst:
    for entry in src.infolist():
     if case=='missing Markdig' and entry.filename=='Markdig.dll':continue
     data=src.read(entry)
     if case=='corrupt Markdig' and entry.filename=='Markdig.dll':data+=b'corruption'
     if case=='wrong platform' and entry.filename=='manifest.yml':data=data.replace(('platform: '+tag).encode(),b'platform: any')
     dst.writestr(entry,data)
    if case=='unexpected file':dst.writestr('unwanted.dll',b'unexpected')
    if case=='duplicate entry':
     with warnings.catch_warnings():
      warnings.simplefilter('ignore',UserWarning)
      dst.writestr('Markdig.dll',b'duplicate')
   r=verify(target)
   assert r.returncode!=0,'Invalid package was accepted: '+case
   results.append({'platform':platform,'case':case,'passed':True,'rejection':r.stderr.strip()})
report=root/'artifacts/beta-qualification/package-verifier.json'
report.write_text(json.dumps(results,indent=2))
print(str(len(results))+' package verification checks passed')
