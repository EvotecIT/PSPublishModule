"""Read-only Windows/Synology database backup health, with projected metadata only."""
import datetime as dt
import json
import os
import pathlib
import re
import subprocess

UTC = dt.timezone.utc
EXPECTED = {'ddct': 'sqlserver', 'ddnext': 'sqlite', 'sectigo': 'sqlite'}
NAS_TASK = 'Evotec database backup verification and retention'
INSTALL = pathlib.Path(os.environ.get('EVOTEC_DATABASE_BACKUP_ROOT', str(pathlib.Path(os.environ.get('ProgramData', r'C:\ProgramData')) / 'PowerForge/DatabaseBackups')))
NAS_CONFIG = '/usr/local/etc/powerforge-database-backup-evomagic.json'
NAS_TOOL = '/usr/local/sbin/powerforge-database-backup'
NAS_SCRIPT = '/usr/bin/python3 ' + NAS_TOOL + ' --config ' + NAS_CONFIG + ' maintain'


def timestamp(value):
    value = value.replace('Z', '+00:00')
    value = re.sub(r'\.(\d+)(?=[+-]\d{2}:\d{2}$)',
                   lambda match: '.' + match.group(1).ljust(6, '0')[:6], value)
    result = dt.datetime.fromisoformat(value)
    if result.tzinfo is None:
        raise ValueError('Backup timestamps require offsets')
    return result.astimezone(UTC)


def evaluate(local, nas, schedule, nas_schedule, restore, now=None):
    now = now or dt.datetime.now(UTC)
    problems = []
    latest = nas.get('latest', {})
    if nas.get('healthy') is not True:
        problems.append('NAS backup status failed or stale')
    if nas.get('keepDaily') != 14 or nas.get('maxAgeHours') != 30:
        problems.append('NAS retention/freshness policy differs')
    if not latest.get('verified') or not latest.get('manifestSha256'):
        problems.append('NAS verification receipt missing')
    current = schedule.get('currentRun') or {}
    receipts_match = (local.get('status') == 'success' and local.get('stamp') == latest.get('stamp') and
                      local.get('captureSha256') == latest.get('captureSha256'))
    # NAS publication precedes the final local success receipt. Allow only a short, identified
    # handover, never a failed/unknown attempt or an arbitrary mismatch with an old capture.
    receipt_pending = (not receipts_match and current.get('status') in ('running', 'success') and
                       current.get('stage') in ('transfer', 'complete') and
                       (schedule.get('running') or current.get('status') == 'success') and
                       current.get('stamp') == latest.get('stamp') and
                       current.get('captureSha256') == latest.get('captureSha256') and
                       bool(latest.get('verifiedAtUtc')) and
                       -300 <= (now - timestamp(latest['verifiedAtUtc'])).total_seconds() <= 300)
    if not receipt_pending and not receipts_match:
        problems.append('Local successful capture and NAS receipt differ')
    if local.get('cleanupErrorType'):
        problems.append('Local backup staging cleanup failed')
    if not schedule.get('enabled') or not schedule.get('daily') or not schedule.get('expectedAction'):
        problems.append('Windows backup schedule is disabled or changed')
    if schedule.get('lastResult') != 0 and not schedule.get('running'):
        problems.append('Latest Windows backup task failed or has not completed')
    if not schedule.get('nextRunUtc') or not now < timestamp(schedule['nextRunUtc']) <= now + dt.timedelta(days=2):
        problems.append('Windows backup next occurrence missing or invalid')
    if not nas_schedule.get('healthy'):
        problems.append('NAS verification/retention task is disabled or changed')
    maintenance = nas.get('maintenance') or {}
    if maintenance.get('healthy') is not True or not maintenance.get('completedAtUtc') or not -300 <= (now - timestamp(maintenance['completedAtUtc'])).total_seconds() <= 36 * 3600:
        problems.append('NAS scheduled verification/retention missing, failed or stale')
    if restore.get('healthy') is not True or set(restore.get('databaseIds', [])) != set(EXPECTED) or not restore.get('completedAtUtc') or not -300 <= (now - timestamp(restore['completedAtUtc'])).total_seconds() <= 30 * 86400:
        problems.append('Database restore drill missing, failed or older than 30 days')
    if current.get('cleanupErrorType') or current.get('heldStagingPaths'):
        problems.append('Latest attempt left staging requiring cleanup')
    if current.get('status') == 'failed':
        problems.append('Most recent backup attempt failed at ' + str(current.get('stage')))
    run_budget = schedule.get('runBudgetMinutes')
    if type(run_budget) is not int or not 1 <= run_budget <= 1500:
        problems.append('Windows backup run budget missing or invalid')
    elif current.get('status') == 'running' and (not current.get('startedAtUtc') or (now - timestamp(current['startedAtUtc'])).total_seconds() > run_budget * 60):
        problems.append('Backup attempt has exceeded its run budget')
    rows = []
    databases = latest.get('databases', [])
    if len(databases) != len(EXPECTED) or {d.get('id'): d.get('provider') for d in databases} != EXPECTED:
        problems.append('NAS backup set is missing a required database')
    for identity, provider in EXPECTED.items():
        db = next((d for d in databases if d.get('id') == identity), {})
        age = (now - timestamp(db['capturedAtUtc'])).total_seconds() if db.get('capturedAtUtc') else None
        errors = list(problems)
        if db.get('provider') != provider or db.get('verified') is not True or not db.get('sizeBytes', 0) > 0:
            errors.append('Database snapshot absent or unverified')
        if age is None or not -300 <= age <= 30 * 3600:
            errors.append('Database capture is stale or future-dated')
        rows.append({'id': identity, 'provider': provider, 'healthy': not errors, 'problems': errors,
                     'capturedAtUtc': db.get('capturedAtUtc'), 'ageHours': round(age / 3600, 2) if age is not None else None,
                     'sizeBytes': db.get('sizeBytes'), 'nasVerifiedAtUtc': latest.get('verifiedAtUtc'),
                     'restoreDrillAtUtc': restore.get('completedAtUtc'), 'snapshotCount': nas.get('snapshotCount'),
                     'retentionDaily': nas.get('keepDaily')})
    return {'healthy': all(r['healthy'] for r in rows), 'checkedAtUtc': now.isoformat(), 'databases': rows,
            'windowsSchedule': schedule, 'nasSchedule': nas_schedule, 'receiptPending': receipt_pending}


def read_backup_snapshot(read_local, read_nas):
    """Bracket the remote read with local reads; retry a publication that straddles them."""
    for _ in range(3):
        before = read_local()
        nas = read_nas()
        after = read_local()
        if before == after:
            return after, nas
    raise RuntimeError('Backup receipts changed repeatedly during monitoring; retry the health scan')


def run_json(arguments, timeout=40):
    process = subprocess.run(arguments, capture_output=True, timeout=timeout)
    if process.returncode:
        raise RuntimeError('Backup health command failed with exit ' + str(process.returncode))
    return json.loads(process.stdout.decode('utf-8-sig'))


def windows_schedule_command(installation):
    """Compare the actual SYSTEM task with the installer's non-secret policy receipt."""
    if installation.get('schemaVersion') != 1 or not isinstance(installation.get('taskName'), str) or not installation['taskName'] or '\\' in installation['taskName']:
        raise ValueError('Installed task identity is invalid')
    daily = installation.get('dailyAt', '')
    if dt.datetime.strptime(daily, '%H:%M').strftime('%H:%M') != daily:
        raise ValueError('Installed daily time is invalid')
    timeout = installation.get('timeoutMinutes')
    if type(timeout) is not int or not 1 <= timeout <= 1440 or installation.get('taskLimitMinutes') != timeout + 60:
        raise ValueError('Installed backup time budget is invalid')
    def literal(value):
        return "'" + value.replace("'", "''") + "'"
    root = installation['installRoot'].rstrip('\\/')
    execute = root + r'\app\PowerForge.Backups.Cli.exe'
    arguments = 'run "' + root + r'\config.json' + '"'
    return ("$ErrorActionPreference='Stop';$t=Get-ScheduledTask -TaskName " + literal(installation['taskName']) +
            ";$i=$t|Get-ScheduledTaskInfo;$limit=[Xml.XmlConvert]::ToTimeSpan($t.Settings.ExecutionTimeLimit);" +
            "[pscustomobject]@{enabled=($t.State -ne 'Disabled');daily=(@($t.Triggers).Count -eq 1 -and $t.Triggers[0].DaysInterval -eq 1 -and ([datetime]$t.Triggers[0].StartBoundary).ToString('HH:mm',[Globalization.CultureInfo]::InvariantCulture) -eq " + literal(daily) +
            ");expectedAction=(@($t.Actions).Count -eq 1 -and $t.Actions.Execute -eq " + literal(execute) +
            " -and $t.Actions.Arguments -eq " + literal(arguments) +
            " -and $t.Principal.UserId -in @('SYSTEM','S-1-5-18') -and $limit.TotalMinutes -eq " + str(installation['taskLimitMinutes']) +
            ");runBudgetMinutes=[int]$limit.TotalMinutes;lastResult=$i.LastTaskResult;running=($t.State -eq 'Running');nextRunUtc=$i.NextRunTime.ToUniversalTime().ToString('o')}|ConvertTo-Json -Compress")


def main():
    try:
        profile = pathlib.Path(os.environ.get('EVOTEC_SYNOLOGY_CONFIG', pathlib.Path.home() / '.config/evotec/synology/hosts.json'))
        host = json.loads(profile.read_text(encoding='utf-8-sig'))['hosts']['STORAGE1']
        ssh = ['ssh', '-i', host['identityFile'], '-p', str(host['port']), '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes',
               '-o', 'StrictHostKeyChecking=yes', '-o', 'UserKnownHostsFile=' + host['knownHostsFile'], '-o', 'ConnectTimeout=12', host['user'] + '@' + host['host']]
        identity = subprocess.run(ssh + ['hostname'], capture_output=True, timeout=20)
        if identity.returncode or identity.stdout.decode().strip() != host['expectedHostname']:
            raise RuntimeError('Synology identity verification failed')
        # Query DSM supported API, then project only this exact task's schedule/action fields.
        task_command = "/usr/syno/bin/synowebapi --exec api=SYNO.Core.TaskScheduler method=list version=2 2>/dev/null"
        listing = run_json(ssh + [task_command])
        if listing.get('success') is not True:
            raise RuntimeError('NAS schedule query failed')
        task = next((t for t in listing.get('data', {}).get('tasks', []) if t.get('name') == NAS_TASK), {})
        task_detail = run_json(ssh + ['/usr/syno/bin/synowebapi --exec api=SYNO.Core.TaskScheduler method=get version=2 id=' + str(int(task['id'])) + ' 2>/dev/null'])
        detail = task_detail.get('data', {})
        daily = detail.get('schedule', {})
        nas_schedule = {'healthy': task_detail.get('success') is True and detail.get('enable') is True and detail.get('owner') == 'root'
                        and detail.get('extra', {}).get('script') == NAS_SCRIPT and daily.get('repeat_hour') == 0
                        and daily.get('date_type') == 0 and daily.get('repeat_date') == 0
                        and daily.get('hour') == 7 and daily.get('minute') == 30
                        and daily.get('week_day') == '0,1,2,3,4,5,6', 'taskId': task.get('id'), 'nextRun': task.get('next_trigger_time')}
        restore_path = INSTALL / 'status/restore-drill.json'
        restore = json.loads(restore_path.read_text(encoding='utf-8-sig')) if restore_path.exists() else {}
        installation = json.loads((INSTALL / 'status/installation.json').read_text(encoding='utf-8-sig'))
        if pathlib.Path(installation['installRoot']).resolve() != INSTALL.resolve():
            raise ValueError('Installed policy belongs to a different installation root')
        command = windows_schedule_command(installation)
        schedule = run_json(['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', command])
        def local_receipts():
            return tuple(json.loads((INSTALL / ('status/' + name)).read_text(encoding='utf-8-sig'))
                         for name in ('last-success.json', 'latest-run.json'))
        (local, current), nas = read_backup_snapshot(local_receipts, lambda: run_json(
            ssh + ['/usr/bin/python3 ' + NAS_TOOL + ' --config ' + NAS_CONFIG + ' status']))
        schedule['currentRun'] = {k: current.get(k) for k in ('status', 'stage', 'stamp', 'captureSha256', 'startedAtUtc', 'completedAtUtc', 'cleanupErrorType', 'heldStagingPaths')}
        return evaluate(local, nas, schedule, nas_schedule, restore)
    except Exception as error:
        message = type(error).__name__ + ': ' + str(error)[:180]
        return {'healthy': False, 'checkedAtUtc': dt.datetime.now(UTC).isoformat(),
                'databases': [{'id': identity, 'healthy': False, 'coverageError': message} for identity in EXPECTED]}


if __name__ == '__main__':
    print(json.dumps(main(), indent=2))
