"""Qualify pinned external builders offline; never import the full Infoblox module."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def execute(args, timeout):
    return subprocess.run(args, capture_output=True, timeout=timeout, check=False)


def qualify(module_root, output):
    scripts = Path(__file__).resolve().parent
    repo = scripts.parent.parent
    expected = json.loads((scripts / 'Corpus/m29a-infoblox-request-builders.json').read_text(encoding='utf-8'))
    cli = repo / 'PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll'
    hosts = [('net10.0', shutil.which('pwsh')), ('net472', str(Path(os.environ['SystemRoot']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'))]
    assert cli.is_file() and all(host for _, host in hosts), 'Build the CLI and provision both claimed hosts first.'
    sources = []
    for source in expected['sourceHashes']:
        relative = source['path'].replace('\\', '/')
        path = (scripts / 'Corpus/ExternalWorkflows/PSSharedGoods/FullModule/Public/Objects/Remove-EmptyValue.ps1'
                if relative.startswith('PSSharedGoods/') else module_root / relative)
        assert digest(path) == source['sha256'], f'Pinned source mismatch: {relative}'
        sources.append(path)
    assert not output.exists(), 'Use a new task-owned output directory; existing evidence is preserved.'
    output.mkdir(parents=True)
    # BOMs are file encoding markers, not part of the authored function bodies.
    payload = b'\r\n'.join(path.read_bytes().removeprefix(b'\xef\xbb\xbf') for path in sources)
    payload += b'''\r\n$script:InfobloxConfiguration=@{offline=$true}
function Set-OfflineInfobloxConfiguration { param([bool]$Connected); $script:InfobloxConfiguration=if($Connected){@{offline=$true}}else{$null} }
Export-ModuleMember -Function Add-InfobloxNetwork,Add-InfobloxDHCPRange,Set-InfobloxDHCPRange,Set-OfflineInfobloxConfiguration -Alias Add-InfobloxSubnet
'''
    original = output / 'OfflineBuilders.psm1'
    original.write_bytes(payload)
    assert digest(original) == expected['packetSha256'], 'Authored packet or wrapper changed.'
    required = ['Add-InfobloxNetwork', 'Add-InfobloxDHCPRange', 'Set-InfobloxDHCPRange']
    summary = {'compilerCommit': subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip(), 'targets': []}
    for framework, host in hosts:
        built = execute(['dotnet', str(cli), 'powershell', 'build', str(original), '--kind', 'dll', '--mode', 'Hybrid', '--framework', framework,
                         '--out', str(output / framework), '--name', 'Offline.Infoblox.Builders', '--allow-unreviewed-dependencies', '--output', 'json'], 180)
        (output / (framework + '-build.json')).write_bytes(built.stdout)
        (output / (framework + '-build.stderr')).write_bytes(built.stderr)
        build = json.loads(built.stdout.decode('utf-8-sig'))
        assert built.returncode == 0 and build['success'], 'Artifact build failed; inspect preserved evidence.'
        build = build['result']
        ledger = build['manifest']['unitDispositionLedger']['entries']
        assert all(any(unit['name'] == name and unit['emittedClrMethod'] and not unit['retainedHostedSource'] for unit in ledger) for name in required)
        observations = []
        for kind, path in [('original', str(original)), ('generated', build['artifactPath'])]:
            run = execute([host, '-NoLogo', '-NoProfile', '-NonInteractive', '-File', str(scripts / 'Invoke-InfobloxRequestBuilderProbe.ps1'), '-ModulePath', path], 120)
            (output / (framework + '-' + kind + '.jsonl')).write_bytes(run.stdout)
            (output / (framework + '-' + kind + '.stderr')).write_bytes(run.stderr)
            assert run.returncode == 0, 'Probe failed; inspect preserved evidence.'
            rows = [json.loads(line) for line in run.stdout.decode('utf-8-sig').splitlines() if line.startswith('{')]
            assert len(rows) == 78, 'Observation count changed.'
            observations.append(rows)
        differences = [{'index': index, 'original': left, 'generated': right} for index, (left, right) in enumerate(zip(*observations)) if left != right]
        (output / (framework + '-differences.json')).write_bytes((json.dumps(differences, indent=2) + '\n').encode())
        assert not differences, 'Original/generated mismatch; inspect preserved evidence.'
        rows = observations[1]
        requests = [row for row in rows if row['requests']]
        assert len(requests) == 30
        assert all(any(row['case'] == case and row['mode'] == 'normal' and not row['failure'] for row in rows)
                   for case in ['network-many', 'range-many', 'update-many'])
        summary['targets'].append({'framework': framework, 'observations': len(rows), 'requestsObserved': len(requests), 'compiledFunctions': required,
                                   'originalSha256': digest(output / (framework + '-original.jsonl')), 'generatedSha256': digest(output / (framework + '-generated.jsonl'))})
        print(framework, 'qualified', len(rows), 'observations', flush=True)
    (output / 'summary.json').write_bytes((json.dumps(summary, indent=2) + '\n').encode())


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--module-root', type=Path, required=True, help='External pinned PowerInfoblox source, revision recorded in the evidence ledger.')
    parser.add_argument('--output', type=Path, required=True, help='New task-owned artifact/evidence directory; clean it after retaining the required evidence.')
    arguments = parser.parse_args()
    if os.name != 'nt':
        parser.error('This qualification claims Windows PowerShell 5.1 and PowerShell 7 on Windows only.')
    qualify(arguments.module_root.resolve(), arguments.output.resolve())
