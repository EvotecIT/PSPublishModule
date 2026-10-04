"""Qualify unchanged DNS request/property selection against offline providers."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def qualify(module_root, output):
    scripts = Path(__file__).resolve().parent
    repo = scripts.parent.parent
    cli = repo / 'PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll'
    expected = {
        'Public/Get-InfobloxDNSRecord.ps1': 'b63f1b44e4df8b488c2e957867423e79436016ae18c76aabbd86266949602c47',
        'Private/Resolve-InfobloxDNSRecordType.ps1': '35c8c6676c01b80d77e003086b944e0f782c6bf0e289052ea29ce7a20c493cfd',
        'Private/Get-InfobloxDNSRecordPreferredField.ps1': '47366dca0d1c44380b58f751aee54f0b589ea151ad97808807c58653ec0a978e',
    }
    hosts = [('net10.0', shutil.which('pwsh')), ('net472', str(Path(os.environ['SystemRoot']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'))]
    assert cli.is_file() and all(host for _, host in hosts)
    for name, value in expected.items():
        assert digest(module_root / name) == value, f'Pinned source mismatch: {name}'
    assert not output.exists(), 'Use a unique task-owned evidence directory.'
    (output / 'source').mkdir(parents=True)
    original = output / 'source/OfflineDns.psm1'
    payload = b'\r\n'.join((module_root / name).read_bytes().removeprefix(b'\xef\xbb\xbf') for name in expected)
    payload += b'''\r\n$script:InfobloxConfiguration=@{offline=$true}
function Set-OfflineDnsConfiguration {param([bool]$Connected);$script:InfobloxConfiguration=if($Connected){@{offline=$true}}else{$null}}
Export-ModuleMember -Function Get-InfobloxDNSRecord,Set-OfflineDnsConfiguration -Alias Get-InfobloxDNSRecords
'''
    original.write_bytes(payload)
    summary = {'compilerCommit': subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip(),
               'sourceHashes': expected, 'packetSha256': digest(original), 'targets': []}
    for framework, host in hosts:
        built = subprocess.run(['dotnet', str(cli), 'powershell', 'build', str(original), '--kind', 'dll', '--mode', 'Hybrid',
                                '--framework', framework, '--out', str(output / framework), '--name', 'Offline.Infoblox.Dns',
                                '--allow-unreviewed-dependencies', '--output', 'json'], capture_output=True, timeout=180)
        (output / (framework + '-build.json')).write_bytes(built.stdout)
        (output / (framework + '-build.stderr')).write_bytes(built.stderr)
        build = json.loads(built.stdout.decode('utf-8-sig'))
        assert built.returncode == 0 and build['success'], 'Build failed; inspect preserved evidence.'
        build = build['result']
        ledger = build['manifest']['unitDispositionLedger']['entries']
        for name in ('Get-InfobloxDNSRecord', 'Resolve-InfobloxDNSRecordType', 'Get-InfobloxDNSRecordPreferredField'):
            assert any(u['name'] == name and u['emittedClrMethod'] and not u['retainedHostedSource'] for u in ledger), name
        observations = []
        for kind, path in [('original', original), ('generated', build['artifactPath'])]:
            run = subprocess.run([host, '-NoLogo', '-NoProfile', '-NonInteractive', '-File', str(scripts / 'Invoke-InfobloxDnsRecordProbe.ps1'),
                                  '-ModulePath', str(path)], capture_output=True, timeout=120)
            (output / (framework + '-' + kind + '.stdout')).write_bytes(run.stdout)
            (output / (framework + '-' + kind + '.stderr')).write_bytes(run.stderr)
            assert run.returncode == 0 and not run.stderr.strip(), 'Execution failed; inspect preserved evidence.'
            # Warning display is host rendering, while the captured records below preserve warning content.
            rows = [json.loads(row) for row in run.stdout.decode('utf-8-sig').splitlines() if row.startswith('{')]
            assert len(rows) == 41, len(rows)
            assert rows[0]['calls'][-1]['first'] == ['name', 'ipv4addr', 'view', 'zone']
            # The 5.1 JSON serializer renders this authored null-array observation as null.
            assert rows[8]['calls'][-1]['first'] == (None if framework == 'net472' else [None])
            assert rows[12]['failure'] and not rows[12]['calls']
            assert not rows[-1]['calls'] and not rows[-2]['calls']
            observations.append(rows)
        assert observations[0] == observations[1], 'Original/generated mismatch; inspect preserved observations.'
        canonical = (json.dumps(observations[0], sort_keys=True, separators=(',', ':')) + '\n').encode('utf-8')
        summary['targets'].append({'framework': framework, 'observations': 41,
                                   'originalSha256': hashlib.sha256(canonical).hexdigest(),
                                   'generatedSha256': hashlib.sha256(canonical).hexdigest()})
    summary['compilerSourceHashes'] = {name: digest(repo / name) for name in (
        'PowerForge.PowerShell/Services/Compilation/Binding/PowerShellNativeFunctionBindingPolicy.cs',
        'PowerForge.PowerShell/Services/Compilation/Binding/PowerShellSemanticBinder.Statements.cs')}
    summary['compilerBinaryHashes'] = {name: digest(cli.parent / name) for name in (
        'PowerForge.Cli.dll', 'PowerForge.PowerShell.dll', 'PowerForge.dll')}
    (output / 'summary.json').write_bytes((json.dumps(summary, indent=2) + '\n').encode('utf-8'))
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--module-root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    qualify(args.module_root.resolve(), args.output.resolve())
