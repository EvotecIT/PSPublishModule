"""Qualify unchanged pinned external platyPS help-name logic with offline metadata only."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def qualify(module_source, output):
    scripts=Path(__file__).resolve().parent
    repo=scripts.parent.parent
    assert digest(module_source)=='2f884e73e086dd5f1a192ef806141affcb6226fc5052a7e0001860b2033c9b10', 'Pinned module source mismatch.'
    source=module_source.read_bytes().decode('utf-8-sig')
    start=source.index('function GetHelpFileName\r\n')
    end=source.index('\r\nfunction MySetContent',start)
    payload=source[start:end].rstrip('\r\n').encode()
    assert hashlib.sha256(payload).hexdigest()=='c1daf1ab91ccdc8dcb05a7fb2302d2736fa9553c983f00d23b7c1af025e33560'
    assert not output.exists(), 'Use a new task-owned evidence directory.'
    (output/'input').mkdir(parents=True)
    original=output/'input/GetHelpFileName.psm1'
    original.write_bytes(payload)
    cli=repo/'PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll'
    hosts=[('net10.0',shutil.which('pwsh')),('net472',str(Path(os.environ['SystemRoot'])/'System32/WindowsPowerShell/v1.0/powershell.exe'))]
    assert cli.is_file() and all(host for _,host in hosts)
    summary={'compilerCommit':subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip(),'moduleSourceSha256':digest(module_source),'functionSourceSha256':digest(original),'targets':[]}
    summary['compilerSourceHashes']={'PowerForge.PowerShell/Services/Compilation/PowerShellCompilationParameterTypePolicy.cs':digest(repo/'PowerForge.PowerShell/Services/Compilation/PowerShellCompilationParameterTypePolicy.cs')}
    summary['compilerBinarySha256']=digest(cli)
    for framework,host in hosts:
        build=subprocess.run(['dotnet',str(cli),'powershell','build',str(original),'--kind','dll','--mode','Hybrid','--framework',framework,'--out',str(output/framework),'--name','Offline.HelpName','--allow-unreviewed-dependencies','--output','json'],capture_output=True,timeout=180)
        (output/(framework+'-build.json')).write_bytes(build.stdout)
        (output/(framework+'-build.stderr')).write_bytes(build.stderr)
        parsed=json.loads(build.stdout.decode('utf-8-sig'))
        assert build.returncode==0 and parsed['success'], 'Build failed; inspect preserved evidence.'
        result=parsed['result']
        unit=next(unit for unit in result['manifest']['unitDispositionLedger']['entries'] if unit['name']=='GetHelpFileName')
        assert unit['emittedClrMethod'] and unit['usesNativeFunctionBinding'] and not unit['retainedHostedSource']
        observations=[]
        for kind,path in [('original',original),('generated',result['artifactPath'])]:
            run=subprocess.run([host,'-NoLogo','-NoProfile','-NonInteractive','-File',str(scripts/'Invoke-HelpFileNameProbe.ps1'),'-ModulePath',str(path)],capture_output=True,timeout=60)
            (output/(framework+'-'+kind+'.jsonl')).write_bytes(run.stdout)
            (output/(framework+'-'+kind+'.stderr')).write_bytes(run.stderr)
            assert run.returncode==0, 'Probe failed; inspect preserved evidence.'
            rows=[json.loads(line) for line in run.stdout.decode('utf-8-sig').splitlines() if line.startswith('{')]
            assert len(rows)==10
            observations.append(rows)
        differences=[{'index':index,'original':left,'generated':right} for index,(left,right) in enumerate(zip(*observations)) if left!=right]
        (output/(framework+'-differences.json')).write_bytes((json.dumps(differences,indent=2)+'\n').encode())
        assert not differences, 'Original/generated mismatch.'
        assert all(not row['failure'] for row in observations[1])
        assert all(row['records']==['OfflineDynamicHelp-help.xml'] for row in observations[1] if row['case']==4)
        assert any(row['records'] for row in observations[1] if row['case']==1)
        assert all(len(row['warning'])==1 for row in observations[1] if row['case'] in [2,3])
        summary['targets'].append({'framework':framework,'observations':10,'originalSha256':digest(output/(framework+'-original.jsonl')),'generatedSha256':digest(output/(framework+'-generated.jsonl'))})
        print(framework,'qualified',flush=True)
    (output/'summary.json').write_bytes((json.dumps(summary,indent=2)+'\n').encode())


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--module-source',type=Path,required=True,help='External pinned platyPS.psm1; no full-module import occurs.')
    parser.add_argument('--output',type=Path,required=True,help='New task-owned artifact/evidence directory.')
    args=parser.parse_args()
    if os.name!='nt': parser.error('This qualification claims both Windows PowerShell hosts only.')
    qualify(args.module_source.resolve(),args.output.resolve())
